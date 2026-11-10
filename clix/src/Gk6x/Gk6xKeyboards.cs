// G-Helper glue for GK6X-family keyboards (Matrix Clix 60%).
//
//  * Detect() identifies the keyboard and registers a Gk6xClix with G-Helper's own keyboard list, so it gets
//    the same tile and the same settings window (AsusKeyboardSettings) as ROG keyboards: lighting mode,
//    colour, brightness, "Sync with laptop", and a per-key "Custom" mode with palette and Fill All.
//  * Lighting lives in onboard profile 2 (Fn+W) and is stored in the keyboard's flash. After each write the
//    profile is re-selected so the keyboard reloads it (and so the Fn+] blue breathing goes away).
//  * Writes are coalesced and skipped when nothing changed. All USB work is off the UI thread. No polling.
//  * Failures are logged and shown as one G-Helper toast per session.

using GHelper.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    /// <summary>Clix options without a control in the shared keyboard window, stored in config.json.</summary>
    public static class Gk6xSettings
    {
        /// <summary>Switch the keyboard to Fn+W on connect and after each write (default on).</summary>
        public static bool AutoSelectProfile => AppConfig.Get("clix_autoselect", 1) != 0;
        /// <summary>
        /// Double buffering (default on): new colours go into whichever of Fn+W (2) / Fn+E (3) is not showing, then
        /// the keyboard switches to it, so the wipe the keyboard needs before every write is never visible.
        /// The other slot is brought up to date a moment later. Off: always Fn+W, with a brief black flash.
        /// (Writing without the wipe was tested 2026-11-10: the keyboard refuses chunk 10 with status 0A.)
        /// </summary>
        public static bool DoubleBuffer => AppConfig.Get("clix_double_buffer", 1) != 0;
        public const byte BufferA = 2, BufferB = 3;
        /// <summary>Pause after each lighting chunk in ms (vendor app: 10).</summary>
        public static int ChunkDelayMs => Math.Clamp(AppConfig.Get("clix_chunk_delay", Gk6xKeyboard.ChunkDelayMs), 0, 50);
        /// <summary>Onboard profile written black once and selected as "lights off" (Fn+R).</summary>
        public const byte DarkProfile = 4;

        // Power states, like G-Helper's laptop backlight options (Extra > Laptop Backlight). Default: lit.
        public static bool Awake { get => AppConfig.Get("clix_awake", 1) != 0; set => AppConfig.Set("clix_awake", value ? 1 : 0); }
        public static bool AwakeOnBattery { get => AppConfig.Get("clix_awake_bat", AppConfig.Get("clix_awake", 1)) != 0; set => AppConfig.Set("clix_awake_bat", value ? 1 : 0); }
        public static bool Sleep { get => AppConfig.Get("clix_sleep", 1) != 0; set => AppConfig.Set("clix_sleep", value ? 1 : 0); }
        public static bool BootShutdown { get => AppConfig.Get("clix_boot", 1) != 0; set => AppConfig.Set("clix_boot", value ? 1 : 0); }

        /// <summary>Master switch: "gk6x_keyboard": 0 in config.json disables all Clix support.</summary>
        public static bool Enabled => AppConfig.Get("gk6x_keyboard", 1) != 0;
    }

    public static class Gk6xKeyboards
    {
        private static string StampKey(byte profile) => $"gk6x_stamp_p{profile}";
        private const string ActiveKey = "gk6x_active";
        /// <summary>Delay before the hidden buffer is brought up to date after a visible change.</summary>
        public const int StaleSyncDelayMs = 1500;
        public const int DebounceMs = 120;
        public const int MinWriteIntervalMs = 0;

        /// <summary>Serialises all USB transactions (detection and writes) so replies can't interleave.</summary>
        private static readonly object ioGate = new();
        private static readonly object stateLock = new();
        private static Gk6xClix? current;
        private static bool toastShown;
        /// <summary>
        /// Safety net: the first write after G-Helper starts happens even if the stamp says it is current, in case
        /// the keyboard lost its lighting (Fn+Esc+Q reset, another app). One small flash write per launch.
        /// </summary>
        private static bool verifiedThisSession;
        private static volatile bool selectOnNextWrite;
        private static volatile bool asleep;
        private static volatile bool shuttingDown;
        /// <summary>Profile selected by the last 0B command this session (0 = unknown).</summary>
        private static byte lastSelected;
        private const string DarkWrittenKey = "gk6x_dark_written";

        private static readonly Gk6xCoalescer coalescer = new(WriteOnce, DebounceMs, MinWriteIntervalMs, Fail);

        public static bool IsConnected { get { lock (stateLock) return current is not null; } }

        public static string Status { get; private set; } = "";
        public static bool LastFailed { get; private set; }

        // ---------------- Hook called from G-Helper (startup and HID device change) ----------------

        public static void Detect()
        {
            if (!Gk6xSettings.Enabled) return;
            try
            {
                Gk6xClix? old;
                lock (stateLock) old = current;
                if (old is not null)
                {
                    if (old.IsDeviceConnected()) return;
                    old.CheckConnection(); // unplugged: let G-Helper drop the old tile before a new one is added
                }

                var candidates = Gk6xHidTransport.FindCandidates();
                if (candidates.Count > 1) { Logger.WriteLine($"GK6X: {candidates.Count} matching collections found, refusing to guess"); return; }
                if (candidates.Count == 0) return;

                Gk6xClix? clix;
                lock (ioGate)
                {
                    var transport = Gk6xHidTransport.TryOpen(candidates[0]);
                    if (transport is null) { Fail("could not open the keyboard's lighting interface"); return; }
                    using var kb = new Gk6xKeyboard(transport, Logger.WriteLine);
                    var id = kb.Identify();
                    if (kb.Model is null) return;
                    clix = new Gk6xClix(id, kb.Model, candidates[0].DevicePath);
                }

                lock (stateLock) current = clix;
                selectOnNextWrite = Gk6xSettings.AutoSelectProfile;
                PeripheralsProvider.Connect(clix);
                bool registered = PeripheralsProvider.ConnectedKeyboards.ToArray().Any(k => ReferenceEquals(k, clix));
                if (!registered)
                {
                    Logger.WriteLine("GK6X: G-Helper did not add the keyboard (already listed?)");
                    lock (stateLock) if (ReferenceEquals(current, clix)) current = null;
                    clix.Dispose();
                    return;
                }
                // Connect() re-applies stored per-key lighting itself; for a single colour do it here, so the
                // once-per-launch check and the Fn+W switch happen in both cases.
                if (!clix.HasTransientLighting && !PeripheralsProvider.IsKeyboardAuraSync) clix.ApplyStoredLighting();
            }
            catch (Exception e)
            {
                Fail("detection failed: " + e.Message);
            }
        }

        internal static void Disconnected(Gk6xClix clix)
        {
            lock (stateLock) if (ReferenceEquals(current, clix)) current = null;
            lastSelected = 0;
        }

        // ---------------- Power hooks (sleep, wake, shutdown, charger) ----------------

        public static void OnSuspend() { asleep = true; ApplyPowerNow(); }
        public static void OnResume() { asleep = false; PowerChanged(); }
        public static void OnShutdown() { shuttingDown = true; ApplyPowerNow(); }
        /// <summary>Charger plugged/unplugged, or a power option changed in Extra.</summary>
        public static void PowerChanged()
        {
            lock (stateLock) if (current is null) return;
            coalescer.Request();
        }

        /// <summary>Lit, unless asleep / shutting down / on battery with the matching option unticked.</summary>
        public static bool ShouldBeLit()
        {
            if (shuttingDown) return Gk6xSettings.BootShutdown;
            if (asleep) return Gk6xSettings.Sleep;
            bool onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
            return onBattery ? Gk6xSettings.AwakeOnBattery : Gk6xSettings.Awake;
        }

        /// <summary>Suspend and shutdown can't wait for the debounce: switch profile synchronously (a few ms).</summary>
        private static void ApplyPowerNow()
        {
            try { WriteOnce(); } catch (Exception e) { Logger.WriteLine("GK6X: power change failed: " + e.Message); }
        }

        /// <summary>The adapter's LED state changed: write it (coalesced).</summary>
        internal static void RequestWrite() => coalescer.Request();

        // ---------------- Work (background task) ----------------

        private static bool WriteOnce()
        {
            Gk6xClix? target;
            lock (stateLock) target = current;
            if (target is null) return false;
            lock (ioGate) return WriteLocked(target);
        }

        private static byte ActiveProfile
        {
            get { int p = AppConfig.Get(ActiveKey, Gk6xSettings.BufferA); return p == Gk6xSettings.BufferB && Gk6xSettings.DoubleBuffer ? Gk6xSettings.BufferB : Gk6xSettings.BufferA; }
            set => AppConfig.Set(ActiveKey, value);
        }

        private static bool WriteLocked(Gk6xClix target)
        {
            uint[] slots = target.SnapshotSlots();
            var id = target.Identity;
            string stamp = $"{id.ModelId}:{id.FirmwareId:X8}:" + Hash(slots);
            bool dbl = Gk6xSettings.DoubleBuffer;
            byte active = ActiveProfile;
            byte other = dbl ? (active == Gk6xSettings.BufferA ? Gk6xSettings.BufferB : Gk6xSettings.BufferA) : active;

            // primary: the colours changed (or first write this launch) -> write and show them
            bool primary = !verifiedThisSession || AppConfig.GetString(StampKey(active)) != stamp;
            byte writeTo = primary ? other : (byte)0;
            // secondary (double buffering): bring the hidden slot up to date, without showing it
            if (!primary && dbl && AppConfig.GetString(StampKey(other)) != stamp) writeTo = other;
            bool hiddenSync = !primary && writeTo != 0;

            bool lit = ShouldBeLit();
            byte shown = primary ? writeTo : active;
            byte targetProfile = lit ? shown : Gk6xSettings.DarkProfile;
            string darkStamp = $"{id.ModelId}:{id.FirmwareId:X8}:p{Gk6xSettings.DarkProfile}:black";
            bool writeDark = !lit && AppConfig.GetString(DarkWrittenKey) != darkStamp;
            bool select = !lit ? lastSelected != targetProfile
                        : selectOnNextWrite || lastSelected == Gk6xSettings.DarkProfile
                          || primary && (dbl || Gk6xSettings.AutoSelectProfile) || lastSelected != 0 && lastSelected != targetProfile && dbl;
            if (writeTo == 0 && !select && !writeDark) { SetStatus("Up to date", false); return false; }

            var transport = Gk6xHidTransport.TryOpenPath(target.DevicePath);
            if (transport is null) { Fail("keyboard not reachable (unplugged?)"); return false; }

            using var kb = new Gk6xKeyboard(transport, Logger.WriteLine);
            // Identified on connect (same device path); the info queries are not repeated for every write.
            kb.AssumeIdentity(id, target.Model);
            long t0 = Environment.TickCount64;

            if (writeTo != 0)
            {
                // Forget the old stamp first: if this write fails part-way the next request must rewrite.
                AppConfig.Set(StampKey(writeTo), "");
                kb.SetLighting(writeTo, slots, true, Gk6xSettings.ChunkDelayMs);
                AppConfig.Set(StampKey(writeTo), stamp);
                verifiedThisSession = true;
            }

            if (writeDark)
            {
                // One time only: make the Fn+R profile dark so "off" is just a profile switch.
                AppConfig.Set(DarkWrittenKey, "");
                kb.SetLighting(Gk6xSettings.DarkProfile, new uint[Gk6xProtocol.StaticSlots], true, Gk6xSettings.ChunkDelayMs);
                AppConfig.Set(DarkWrittenKey, darkStamp);
                Logger.WriteLine($"GK6X: profile {Gk6xSettings.DarkProfile} set to black (lights-off state)");
            }

            // Selecting a profile is what makes the keyboard (re)load it (the vendor app and GK6X both do this).
            if (select)
            {
                if (lit) selectOnNextWrite = false;
                kb.SelectProfile(targetProfile);
                lastSelected = targetProfile;
            }
            if (primary) ActiveProfile = shown;

            Logger.WriteLine($"GK6X: {(writeTo != 0 ? $"profile {writeTo} written{(hiddenSync ? " (hidden copy)" : "")}" : "no write")}" +
                             $"{(select ? $", profile {targetProfile} selected" : "")} in {Environment.TickCount64 - t0} ms");
            SetStatus(writeTo != 0 ? "Applied" : "Up to date", false);

            // After a visible change, update the hidden copy once things are quiet.
            if (primary && dbl) _ = System.Threading.Tasks.Task.Delay(StaleSyncDelayMs).ContinueWith(_ => coalescer.Request());
            return writeTo != 0;
        }

        private static string Hash(uint[] slots)
        {
            // FNV-1a over the slot values: short, stable, and changes whenever any LED changes.
            ulong h = 14695981039346656037UL;
            foreach (uint v in slots) { h ^= v; h *= 1099511628211UL; }
            return h.ToString("X16");
        }

        // ---------------- Status ----------------

        private static void Fail(string message)
        {
            Logger.WriteLine("GK6X: " + message);
            SetStatus(message, true);
            if (toastShown) return;
            toastShown = true;
            try { Program.toast?.RunToast("Matrix Clix: " + message); } catch { }
        }

        private static void SetStatus(string s, bool failed)
        {
            Status = s;
            LastFailed = failed;
        }
    }
}
