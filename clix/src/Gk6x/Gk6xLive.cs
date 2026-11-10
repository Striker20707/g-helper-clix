// Live lighting for the Matrix Clix while G-Helper runs ("driver mode", like the vendor app's online profile).
//
//  * One background thread keeps the keyboard in driver mode: it sends a frame whenever the colours change
//    (instantly, no flash, nothing written to flash) and about 20 frames a second while an effect animates,
//    plus a ping at least once a second. Frames are drawn by Gk6xEffects.
//  * The stored layers stay the fallback: a few seconds after the last change the colours are also written to
//    Fn+W (and Fn+R is kept black), so whatever happens to G-Helper the keyboard shows the right thing.
//  * Before the lights should go off, sleep, shutdown or exit, G-Helper leaves driver mode itself by selecting
//    Fn+R (off) or Fn+W (on). Only if G-Helper is killed do the pings simply stop.
//  * Any failure drops back to the stored layers; after three failures in a row live mode is off for the session.
// All USB work happens under Gk6xKeyboards.ioGate on one open handle, so replies can never interleave.

using System;
using System.Threading;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static partial class Gk6xKeyboards
    {
        public const int PingIntervalMs = 900;
        /// <summary>Minimum time between animation frames (a frame takes about 54 ms on the Clix anyway).</summary>
        public const int FrameIntervalMs = 50;
        /// <summary>Quiet time before a live change is also stored in the keyboard (saves flash writes while dragging).</summary>
        public const int LiveStoreDelayMs = 3000;
        public const int MaxLiveFailures = 3;

        private static readonly object effectLock = new();
        private static Gk6xEffect? effect;
        private static long effectStartMs;
        private static volatile bool frameDirty = true;

        /// <summary>Open handle while in driver mode (guarded by ioGate).</summary>
        private static Gk6xKeyboard? liveKb;
        private static volatile bool inLive;
        private static volatile bool exiting;
        private static volatile bool liveUnavailable;
        private static int liveFailures;
        private static bool storedSyncFailedInLive;
        private static bool storedCheckedThisSession;
        private static bool exitHooked;

        private static readonly AutoResetEvent liveWake = new(false);
        private static Thread? liveThread;
        private static readonly System.Threading.Timer storeTimer = new(_ => { try { WriteOnce(); } catch (Exception e) { Logger.WriteLine("GK6X: store failed: " + e.Message); } });

        /// <summary>True while the keyboard is showing G-Helper's live frames.</summary>
        public static bool IsLive => inLive;

        /// <summary>Live mode is used when enabled, it has not failed this session, and the lights should be on now.</summary>
        private static bool LiveWanted()
        {
            if (!Gk6xSettings.Live || liveUnavailable || exiting || shuttingDown || asleep) return false;
            lock (stateLock) if (current is null) return false;
            return ShouldBeLit();
        }

        /// <summary>
        /// New lighting from the keyboard window, profile switch or laptop sync. restart: start the animation from its
        /// beginning (the laptop restarts its effect whenever it is applied, so synced effects line up).
        /// </summary>
        internal static void LightingChanged(Gk6xEffect e, bool restart)
        {
            SetEffect(e, restart);
            RequestWrite();
        }

        /// <summary>A colour streamed by a laptop mode (Ambient, Heatmap, Audio...): shown live only, never stored.</summary>
        internal static void ShowTransient(Gk6xEffect e)
        {
            if (!inLive) return;
            SetEffect(e, false);
        }

        /// <summary>Hook in PeripheralsProvider.StreamKeyboardColor (only reached with "Sync with laptop" on).</summary>
        public static void StreamColor(System.Drawing.Color color)
        {
            if (!inLive) return;
            Gk6xClix? clix;
            lock (stateLock) clix = current;
            clix?.StreamColour(color);
        }

        private static void SetEffect(Gk6xEffect e, bool restart)
        {
            lock (effectLock)
            {
                if (restart || effect is null || effect.Kind != e.Kind) effectStartMs = Environment.TickCount64;
                effect = e;
            }
            frameDirty = true;
            if (Gk6xSettings.Live && !liveUnavailable) { EnsureLiveThread(); liveWake.Set(); }
        }

        private static void EnsureLiveThread()
        {
            HookExit();
            lock (effectLock)
            {
                if (liveThread is not null) return;
                liveThread = new Thread(LiveLoop) { IsBackground = true, Name = "Clix live lighting" };
                liveThread.Start();
            }
        }

        private static void HookExit()
        {
            if (exitHooked) return;
            exitHooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => OnExit();
            try { System.Windows.Forms.Application.ApplicationExit += (_, _) => OnExit(); } catch { }
        }

        /// <summary>G-Helper is closing: leave driver mode on the stored layer, so the keyboard keeps its colours.</summary>
        public static void OnExit()
        {
            if (exiting) return;
            exiting = true;
            ApplyPowerNow();
        }

        private static void LiveLoop()
        {
            uint[] frame = new uint[Gk6xProtocol.StaticSlots];
            long lastPing = 0, lastFrame = 0;
            while (true)
            {
                int wait = Timeout.Infinite;
                try
                {
                    Gk6xClix? target;
                    lock (stateLock) target = current;
                    bool haveEffect;
                    lock (effectLock) haveEffect = effect is not null;
                    // Nothing to show yet (the window or laptop sync hasn't applied anything): stay on the stored layer.
                    if (target is null || !haveEffect || !LiveWanted())
                    {
                        if (inLive && target is not null) lock (ioGate) if (inLive && !LiveWanted()) LeaveLiveLocked(target);
                        liveWake.WaitOne();
                        continue;
                    }

                    if (!inLive)
                    {
                        lock (ioGate)
                        {
                            if (inLive || !LiveWanted()) continue;
                            // Once per launch, before going live: make sure the stored fallback is right.
                            if (!storedCheckedThisSession) { WriteLocked(target); storedCheckedThisSession = true; }
                            EnterLiveLocked(target);
                        }
                        frameDirty = true;
                        lastPing = Environment.TickCount64;
                    }

                    Gk6xEffect? e;
                    long start;
                    lock (effectLock) { e = effect; start = effectStartMs; }
                    long now = Environment.TickCount64;
                    if (e is not null && (frameDirty || (e.Animated && now - lastFrame >= FrameIntervalMs)))
                    {
                        frameDirty = false;
                        Gk6xEffects.Render(e, (now - start) / 1000.0, frame);
                        lock (ioGate)
                        {
                            if (!inLive) continue;
                            liveKb!.SendLiveFrame(frame);
                        }
                        lastFrame = now;
                        liveFailures = 0;
                    }

                    if (Environment.TickCount64 - lastPing >= PingIntervalMs)
                    {
                        lock (ioGate)
                        {
                            if (!inLive) continue;
                            liveKb!.Ping();
                        }
                        lastPing = Environment.TickCount64;
                    }

                    now = Environment.TickCount64;
                    long untilPing = PingIntervalMs - (now - lastPing);
                    long untilFrame = e?.Animated == true ? FrameIntervalMs - (now - lastFrame) : long.MaxValue;
                    wait = (int)Math.Clamp(Math.Min(untilPing, untilFrame), 1, PingIntervalMs);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("GK6X: live lighting stopped: " + ex.Message);
                    lock (ioGate) DropLiveLocked();
                    if (++liveFailures >= MaxLiveFailures)
                    {
                        liveUnavailable = true;
                        Logger.WriteLine("GK6X: live lighting off for this session, using the stored layers");
                        SetStatus("Stored lighting (live mode unavailable)", false);
                        coalescer.Request();
                        continue;
                    }
                    wait = 1000 * liveFailures;
                }
                liveWake.WaitOne(wait);
            }
        }

        private static void EnterLiveLocked(Gk6xClix target)
        {
            var transport = Gk6xHidTransport.TryOpenPath(target.DevicePath) ?? throw new Gk6xException("keyboard not reachable (unplugged?)");
            var kb = new Gk6xKeyboard(transport, Logger.WriteLine);
            try
            {
                kb.AssumeIdentity(target.Identity, target.Model);
                long t0 = Environment.TickCount64;
                kb.EnterLiveMode();
                liveKb = kb;
                inLive = true;
                lastSelected = Gk6xProtocol.LayerDriver;
                Logger.WriteLine($"GK6X: live lighting on in {Environment.TickCount64 - t0} ms");
                SetStatus("Live", false);
            }
            catch
            {
                kb.Dispose();
                throw;
            }
        }

        /// <summary>Closes the handle without sending anything (after an error; the keyboard times out by itself).</summary>
        private static void DropLiveLocked()
        {
            // Left without a layer switch: the next stored write selects the colour layer again.
            if (inLive) selectOnNextWrite = true;
            inLive = false;
            if (lastSelected == Gk6xProtocol.LayerDriver) lastSelected = 0;
            try { liveKb?.Dispose(); } catch { }
            liveKb = null;
        }

        /// <summary>
        /// Leaves driver mode on purpose: selects Fn+R when the lights should be off (if it is ready), else the
        /// stored colour layer, then lets the stored path finish (write any change made in the last few seconds).
        /// </summary>
        private static void LeaveLiveLocked(Gk6xClix target)
        {
            if (!inLive) return;
            var id = target.Identity;
            bool darkReady = UseDarkLayer && AppConfig.GetString(StampKey(Gk6xSettings.DarkProfile)) == SlotsStamp(id, new uint[Gk6xProtocol.StaticSlots])
                             && AppConfig.GetString(KeysKey(Gk6xSettings.DarkProfile)) == KeysStamp(id);
            byte layer = !ShouldBeLit() && darkReady ? Gk6xSettings.DarkProfile : ActiveProfile;
            try
            {
                liveKb!.SelectProfile(layer);
                lastSelected = layer;
                inLive = false; // left on purpose: no re-select needed
                Logger.WriteLine($"GK6X: live lighting off, profile {layer} selected");
            }
            catch (Exception e)
            {
                Logger.WriteLine("GK6X: could not leave live mode cleanly: " + e.Message);
                lastSelected = 0;
            }
            DropLiveLocked();
            WriteLocked(target);
        }

        /// <summary>
        /// While live: store the current colours in the shown-on-fallback layer (no layer switch, nothing visible),
        /// and keep Fn+R ready. If the keyboard refuses stored writes in driver mode, they wait until live mode ends.
        /// </summary>
        private static void StoreWhileLiveLocked(Gk6xClix target)
        {
            if (storedSyncFailedInLive || liveKb is null) return;
            var id = target.Identity;
            uint[] slots = target.SnapshotSlots();
            string stamp = SlotsStamp(id, slots);
            string blackStamp = SlotsStamp(id, new uint[Gk6xProtocol.StaticSlots]);
            byte p = ActiveProfile;
            bool colours = !verifiedThisSession || AppConfig.GetString(StampKey(p)) != stamp;
            bool dark = UseDarkLayer && (AppConfig.GetString(StampKey(Gk6xSettings.DarkProfile)) != blackStamp
                                         || AppConfig.GetString(KeysKey(Gk6xSettings.DarkProfile)) != KeysStamp(id));
            if (!colours && !dark) return;
            long t0 = Environment.TickCount64;
            try
            {
                if (colours)
                {
                    if (!EnsureNormalKeys(liveKb, id, p)) throw new Gk6xException($"profile {p} key table write refused");
                    AppConfig.Set(StampKey(p), "");
                    liveKb.SetLighting(p, slots, true, Gk6xSettings.ChunkDelayMs);
                    AppConfig.Set(StampKey(p), stamp);
                    verifiedThisSession = true;
                }
                if (dark && !PrepareDarkLayer(liveKb, id, blackStamp)) darkLayerFailed = true;
                Logger.WriteLine($"GK6X: live colours stored in profile {p} in {Environment.TickCount64 - t0} ms");
            }
            catch (Exception e)
            {
                storedSyncFailedInLive = true;
                Logger.WriteLine("GK6X: stored write refused in live mode, storing when live mode ends: " + e.Message);
                DropLiveLocked();
            }
            // Re-send the frame: whatever the write did to the display, the live colours come straight back.
            frameDirty = true;
            liveWake.Set();
        }
    }
}
