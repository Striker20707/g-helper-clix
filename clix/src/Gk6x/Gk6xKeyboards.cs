// G-Helper glue for GK6X-family keyboards (Matrix Clix 60%).
//
//  * Shows the keyboard as its own peripheral tile with its own settings window (Gk6xSettingsForm).
//  * Its colour / brightness / on-off are its own settings, independent of the laptop keyboard, unless the
//    user ticks "Sync with laptop".
//  * Lighting lives in onboard profile 2 (Fn+W). The keyboard keeps it in its own memory, so nothing has to
//    keep running. Optionally G-Helper switches the keyboard to that profile when it connects.
//  * Writes are coalesced and skipped when nothing changed. All USB work is off the UI thread. No polling:
//    work happens only on a settings change, a laptop lighting change (when synced) or a HID device event.
//  * Failures are shown in the settings window and as one G-Helper toast per session, and logged.

using GHelper.Helpers;
using GHelper.Input;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    /// <summary>The user's Clix settings, stored in G-Helper's config.json.</summary>
    public static class Gk6xSettings
    {
        public static Color Colour
        {
            get => Color.FromArgb(AppConfig.Get("clix_color", Color.White.ToArgb()));
            set => AppConfig.Set("clix_color", value.ToArgb());
        }
        /// <summary>0..100 %</summary>
        public static int Brightness
        {
            get => Math.Clamp(AppConfig.Get("clix_brightness", 100), 0, 100);
            set => AppConfig.Set("clix_brightness", Math.Clamp(value, 0, 100));
        }
        public static bool LightsOn
        {
            get => AppConfig.Get("clix_on", 1) != 0;
            set => AppConfig.Set("clix_on", value ? 1 : 0);
        }
        public static bool SyncWithLaptop
        {
            get => AppConfig.Get("clix_sync", 0) != 0;
            set => AppConfig.Set("clix_sync", value ? 1 : 0);
        }
        public static bool AutoSelectProfile
        {
            get => AppConfig.Get("clix_autoselect", 1) != 0;
            set => AppConfig.Set("clix_autoselect", value ? 1 : 0);
        }
        /// <summary>Red keycaps (Esc, Tab, Caps, Shift, Ctrl, Win, Alt, Menu, Fn, Enter, \, Backspace).</summary>
        public static Gk6xAccentMode AccentMode
        {
            get => (Gk6xAccentMode)Math.Clamp(AppConfig.Get("clix_keys_mode", 0), 0, 2);
            set => AppConfig.Set("clix_keys_mode", (int)value);
        }
        public static Color AccentColour
        {
            get => Color.FromArgb(AppConfig.Get("clix_keys_color", Color.White.ToArgb()));
            set => AppConfig.Set("clix_keys_color", value.ToArgb());
        }
        /// <summary>Master switch: "gk6x_keyboard": 0 in config.json disables all Clix support.</summary>
        public static bool Enabled => AppConfig.Get("gk6x_keyboard", 1) != 0;
    }

    public enum Gk6xAccentMode { SameAsOtherKeys = 0, Off = 1, OwnColour = 2 }

    /// <summary>The Clix as a G-Helper peripheral (tile in the main window).</summary>
    public sealed class Gk6xPeripheral : IPeripheral
    {
        public Gk6xIdentity Identity { get; }
        public Gk6xSupportedModel Model { get; }
        public string DevicePath { get; }

        public Gk6xPeripheral(Gk6xIdentity identity, Gk6xSupportedModel model, string devicePath)
        {
            Identity = identity;
            Model = model;
            DevicePath = devicePath;
        }

        public bool IsDeviceReady => true;
        public int Battery => 0;
        public bool Charging => false;
        public bool CanExport() => false;
        public byte[] Export() => Array.Empty<byte>();
        public bool Import(byte[] blob) => false;
        public PeripheralType DeviceType() => PeripheralType.Keyboard;
        public string GetDisplayName() => Model.Name;
        public bool HasBattery() => false;
        public void SynchronizeDevice() { }
        public void ReadBattery() { }
    }

    public static class Gk6xKeyboards
    {
        private const string LastWrittenKey = "gk6x_last_written";
        public const int DebounceMs = 400;
        public const int MinWriteIntervalMs = 1500;

        private static readonly object listLock = new();
        /// <summary>Serialises all USB transactions (detection and sync) so replies can't interleave.</summary>
        private static readonly object ioGate = new();
        private static readonly List<Gk6xPeripheral> connected = new();
        private static volatile bool selectOnNextSync;
        /// <summary>
        /// Safety net: the first sync after G-Helper starts rewrites the lighting even if the stamp says it is current,
        /// in case the keyboard lost it (Fn+Esc+Q reset, another app). One small flash write per launch, nothing more.
        /// </summary>
        private static bool verifiedThisSession;
        private static bool toastShown;

        private static readonly Gk6xCoalescer coalescer = new(SyncOnce, DebounceMs, MinWriteIntervalMs, Fail);

        /// <summary>Human-readable result of the last operation, for the settings window.</summary>
        public static string Status { get; private set; } = "";
        public static bool LastFailed { get; private set; }
        public static event Action? StatusChanged;

        public static List<IPeripheral> Connected
        {
            get { lock (listLock) return connected.Cast<IPeripheral>().ToList(); }
        }

        // ---------------- Hooks called from G-Helper ----------------

        /// <summary>HID device change / startup: find, identify, add or remove the tile.</summary>
        public static void Detect()
        {
            if (!Gk6xSettings.Enabled) return;
            try
            {
                var candidates = Gk6xHidTransport.FindCandidates();
                bool changed = false;
                lock (listLock)
                {
                    int removed = connected.RemoveAll(p => !candidates.Any(c => c.DevicePath == p.DevicePath));
                    changed |= removed > 0;
                }
                if (candidates.Count == 1 && !IsConnected(candidates[0].DevicePath))
                lock (ioGate)
                {
                    var transport = Gk6xHidTransport.TryOpen(candidates[0]);
                    if (transport is null) { Fail("could not open the keyboard's lighting interface"); }
                    else
                    {
                        using var kb = new Gk6xKeyboard(transport, Logger.WriteLine);
                        var id = kb.Identify();
                        if (kb.Model is not null)
                        {
                            lock (listLock) connected.Add(new Gk6xPeripheral(id, kb.Model, candidates[0].DevicePath));
                            changed = true;
                            selectOnNextSync = Gk6xSettings.AutoSelectProfile;
                        }
                    }
                }
                else if (candidates.Count > 1)
                {
                    Logger.WriteLine($"GK6X: {candidates.Count} matching collections found, refusing to guess");
                }
                if (changed)
                {
                    RefreshMainWindow();
                    RequestSync();
                }
            }
            catch (Exception e)
            {
                Fail("detection failed: " + e.Message);
            }
        }

        /// <summary>Laptop Aura colour or user brightness changed (Aura.ApplyAura / ApplyBrightness).</summary>
        public static void OnLaptopLightingChanged()
        {
            if (Gk6xSettings.SyncWithLaptop) RequestSync();
        }

        /// <summary>Clix settings changed in its own window.</summary>
        public static void RequestSync()
        {
            if (!Gk6xSettings.Enabled) return;
            lock (listLock) if (connected.Count == 0) return;
            coalescer.Request();
        }

        /// <summary>"Apply now": rewrite the lighting (e.g. after Fn+Esc+Q cleared the keyboard) and switch to Fn+W.</summary>
        public static void RequestSelectProfile()
        {
            selectOnNextSync = true;
            AppConfig.Set(LastWrittenKey, "");
            lock (listLock) if (connected.Count == 0) return;
            coalescer.Request();
        }

        // ---------------- Work (background task) ----------------

        /// <summary>Main colour, and the red-keycap colour (null = same as the main colour).</summary>
        public static ((byte r, byte g, byte b) main, (byte r, byte g, byte b)? accent) DesiredColours()
        {
            Color c;
            int percent;
            if (Gk6xSettings.SyncWithLaptop)
            {
                c = Color.FromArgb(AppConfig.Get("aura_color"));
                int level = InputDispatcher.GetBacklight(); // 0..3, power-source aware
                percent = level <= 0 ? 0 : level * 100 / 3;
            }
            else
            {
                c = Gk6xSettings.Colour;
                percent = Gk6xSettings.LightsOn ? Gk6xSettings.Brightness : 0;
            }
            var main = Gk6xProtocol.Scale(c.R, c.G, c.B, percent);
            (byte, byte, byte)? accent = Gk6xSettings.AccentMode switch
            {
                Gk6xAccentMode.Off => ((byte)0, (byte)0, (byte)0),
                Gk6xAccentMode.OwnColour => Gk6xProtocol.Scale(Gk6xSettings.AccentColour.R, Gk6xSettings.AccentColour.G, Gk6xSettings.AccentColour.B, percent),
                _ => null,
            };
            return (main, accent);
        }

        /// <summary>Returns true only if a lighting write was made.</summary>
        private static bool SyncOnce()
        {
            Gk6xPeripheral? target;
            lock (listLock) target = connected.FirstOrDefault();
            if (target is null) return false;
            lock (ioGate) return SyncLocked(target);
        }

        private static bool SyncLocked(Gk6xPeripheral target)
        {
            var transport = Gk6xHidTransport.TryOpenPath(target.DevicePath);
            if (transport is null) { Fail("keyboard not reachable (unplugged?)"); return false; }

            using var kb = new Gk6xKeyboard(transport, Logger.WriteLine);
            Gk6xIdentity id = kb.Identify();
            if (!kb.IsSupported) { Fail("keyboard identity changed; writes disabled"); return false; }

            var (main, accent) = DesiredColours();
            var (r, g, b) = main;
            byte profile = Gk6xKeyboard.GHelperProfile;
            string keys = accent is { } a ? $"{a.r},{a.g},{a.b}" : "same";
            string stamp = $"{id.ModelId}:{id.FirmwareId:X8}:p{profile}:{r},{g},{b}:keys={keys}";
            bool wrote = false;

            if (!verifiedThisSession || AppConfig.GetString(LastWrittenKey) != stamp)
            {
                // Forget the old stamp first: if this write fails part-way the next request must rewrite.
                AppConfig.Set(LastWrittenKey, "");
                kb.SetStaticColours(profile, main, accent);
                AppConfig.Set(LastWrittenKey, stamp);
                verifiedThisSession = true;
                wrote = true;
                Logger.WriteLine($"GK6X: profile {profile} colour {r},{g},{b}, red keys {keys} written");
            }

            // Selecting the profile again after a write is what makes the keyboard reload it (the vendor app and
            // GK6X both do this). Without it a write made while Fn+W is active only shows after leaving and re-entering.
            if (selectOnNextSync || wrote && Gk6xSettings.AutoSelectProfile)
            {
                selectOnNextSync = false;
                kb.SelectProfile(profile);
            }

            SetStatus(wrote ? "Applied" : "Up to date", false);
            return wrote;
        }

        // ---------------- Status / UI ----------------

        private static bool IsConnected(string path)
        {
            lock (listLock) return connected.Any(p => p.DevicePath == path);
        }

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
            try { StatusChanged?.Invoke(); } catch { }
        }

        private static void RefreshMainWindow()
        {
            try
            {
                var f = Program.settingsForm;
                if (f is null || f.IsDisposed || !f.IsHandleCreated) return;
                f.BeginInvoke(delegate { f.VisualizePeripherals(); });
            }
            catch { }
        }
    }
}
