// G-Helper Clix crash safety. Two steps, so the Clix build is used whenever it works at all:
//
//  1. Clix safe mode. If G-Helper crashes and Clix code is involved, the Clix keyboard support is switched
//     off for this Clix build and G-Helper restarts itself at once. Everything else (laptop controls, fans,
//     battery limit, updater) keeps working; the keyboard keeps the colours stored in Fn+W. The next Clix
//     build turns the keyboard support back on by itself.
//  2. Official G-Helper. Only if the Clix build can't even start (it crashed during startup twice in a row,
//     the second time already in safe mode) does it start the official GHelper.exe for this session instead.
//     The official exe is found from its own (disabled) startup task, or "clix_fallback_exe" in config.json.
//     At the next sign-in the Clix build tries again (in safe mode), so a fixed update can still arrive.
//
// State lives in %AppData%\GHelper\clix-guard.txt (a few key=value lines). Deleting it resets everything.
// Nothing runs in the background: this is only a few file reads at start and a crash handler.

using GHelper.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static class ClixGuard
    {
        /// <summary>Seconds after launch before a start counts as good.</summary>
        public const int HealthySeconds = 20;
        /// <summary>Failed starts in a row (this one included) from which this launch runs in safe mode.</summary>
        public const int SafeModeAfter = 2;
        /// <summary>Failed starts in a row (this one included) from which the official G-Helper is started instead.</summary>
        public const int OfficialAfter = 3;

        private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GHelper");
        private static readonly string FilePath = Path.Combine(Dir, "clix-guard.txt");

        private static int failedStarts;
        private static bool safeForBuild;
        private static string safeReason = "";
        private static volatile bool healthy;
        private static volatile bool crashed;
        private static System.Threading.Timer? healthyTimer;

        /// <summary>True when the Clix keyboard support is off for this launch (after a crash).</summary>
        public static bool SafeMode { get; private set; }
        public static string SafeReason => safeReason;

        /// <summary>
        /// Called at the top of Program.Main (not for the "charge" task). Returns true when the official G-Helper
        /// was started instead, and this process should exit at once.
        /// </summary>
        public static bool Launch(string[] args)
        {
            try
            {
                Load();
                // Opening G-Helper while it already runs closes the running copy and starts a new one. The running
                // copy clearly started fine, even if it is closed before HealthySeconds, so that is not a failed
                // start. (Before, opening and closing quickly a few times counted up to safe mode and the fallback.)
                if (OtherInstanceRunning()) failedStarts = 0;
                failedStarts++;
                if (failedStarts >= OfficialAfter && StartOfficial())
                {
                    // Next sign-in: one more try of the Clix build (in safe mode) before falling back again.
                    failedStarts = SafeModeAfter - 1;
                    Save();
                    return true;
                }
                SafeMode = safeForBuild || failedStarts >= SafeModeAfter;
                if (SafeMode && safeReason == "") safeReason = "G-Helper Clix did not start properly last time";
                Save();

                AppDomain.CurrentDomain.UnhandledException += (_, e) => OnCrash(e.ExceptionObject as Exception);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { if (!crashed) MarkHealthy(); };
                healthyTimer = new System.Threading.Timer(_ => MarkHealthy(), null, HealthySeconds * 1000, Timeout.Infinite);

                Logger.WriteLine($"Clix build {Gk6xBuild.Number}" + (SafeMode ? $" in safe mode (Clix keyboard support off): {safeReason}" : ""));
            }
            catch (Exception e)
            {
                Logger.WriteLine("Clix guard: " + e.Message);
            }
            return false;
        }

        private static bool OtherInstanceRunning()
        {
            try
            {
                using var self = Process.GetCurrentProcess();
                var all = Process.GetProcessesByName(self.ProcessName);
                bool other = all.Any(p => p.Id != self.Id);
                foreach (var p in all) p.Dispose();
                return other;
            }
            catch { return false; }
        }

        /// <summary>Shown once G-Helper's toast exists (from the keyboard detection hook).</summary>
        public static void NotifyIfSafe()
        {
            if (!SafeMode || notified) return;
            notified = true;
            string text = safeForBuild
                ? "G-Helper Clix: keyboard lighting paused after a crash. The next Clix update turns it back on."
                : "G-Helper Clix: keyboard lighting paused because G-Helper didn't start properly last time.";
            try { Program.toast?.RunToast(text); } catch { }
        }
        private static bool notified;

        private static void MarkHealthy()
        {
            if (healthy || crashed) return;
            healthy = true;
            try { failedStarts = 0; Save(); } catch { }
        }

        private static void OnCrash(Exception? e)
        {
            if (crashed) return;
            crashed = true;
            try
            {
                string text = e?.ToString() ?? "";
                bool clix = text.Contains("Gk6x") || text.Contains("ClixGuard");
                Logger.WriteLine($"Clix guard: crash ({(clix ? "in Clix code" : "outside Clix code")}{(healthy ? "" : ", during startup")})");
                bool restart = false;
                if (clix && !safeForBuild)
                {
                    safeForBuild = true;
                    safeReason = "crash in the Clix keyboard code: " + (e?.GetType().Name ?? "unknown") + " " + (e?.Message ?? "");
                    restart = true;
                }
                // A crash during startup restarts too; the failed-start count decides what the next launch does,
                // so this can never loop: after OfficialAfter failed starts the official G-Helper takes over.
                if (!healthy) restart = true;
                Save();
                if (restart) Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath) { UseShellExecute = true });
            }
            catch { }
        }

        // ---------------- Official G-Helper ----------------

        private static bool StartOfficial()
        {
            string? exe = FindOfficial();
            if (exe is null)
            {
                Logger.WriteLine("Clix guard: Clix build failed to start repeatedly, but no official GHelper.exe was found; trying safe mode");
                return false;
            }
            try
            {
                Logger.WriteLine("Clix guard: Clix build failed to start repeatedly, starting the official G-Helper: " + exe);
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                return true;
            }
            catch (Exception e)
            {
                Logger.WriteLine("Clix guard: could not start the official G-Helper: " + e.Message);
                return false;
            }
        }

        /// <summary>"clix_fallback_exe" in config.json, else the exe in official G-Helper's own startup task.</summary>
        public static string? FindOfficial()
        {
            string self = Path.GetFullPath(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath);
            bool Usable(string? p) => !string.IsNullOrWhiteSpace(p) && File.Exists(p) &&
                                      !string.Equals(Path.GetFullPath(p), self, StringComparison.OrdinalIgnoreCase);
            try
            {
                string configured = AppConfig.GetString("clix_fallback_exe") ?? "";
                if (Usable(configured)) return configured;
            }
            catch { }
            try
            {
                using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "";
                foreach (string name in new[] { "GHelper_" + sid, "GHelper" })
                {
                    var task = ts.GetTask(name);
                    var action = task?.Definition.Actions.OfType<Microsoft.Win32.TaskScheduler.ExecAction>().FirstOrDefault();
                    string? path = action?.Path?.Trim().Trim('"');
                    if (Usable(path)) return path;
                }
            }
            catch (Exception e)
            {
                Logger.WriteLine("Clix guard: could not read the official startup task: " + e.Message);
            }
            return null;
        }

        // ---------------- State file ----------------

        private static void Load()
        {
            failedStarts = 0; safeForBuild = false; safeReason = "";
            if (!File.Exists(FilePath)) return;
            var kv = File.ReadAllLines(FilePath).Select(l => l.Split('=', 2)).Where(p => p.Length == 2)
                         .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            // A different Clix build starts with a clean slate (a new build may well fix the crash).
            if (!kv.TryGetValue("build", out var b) || b != Gk6xBuild.Number.ToString()) return;
            if (kv.TryGetValue("failed_starts", out var f)) int.TryParse(f, out failedStarts);
            safeForBuild = kv.TryGetValue("safe_mode", out var s) && s == "1";
            if (kv.TryGetValue("reason", out var r)) safeReason = r;
        }

        private static readonly object saveLock = new();

        private static void Save()
        {
            lock (saveLock)
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new[]
                {
                    "build=" + Gk6xBuild.Number,
                    "failed_starts=" + failedStarts,
                    "safe_mode=" + (safeForBuild ? 1 : 0),
                    "reason=" + safeReason.Replace('\r', ' ').Replace('\n', ' '),
                });
            }
        }
    }
}
