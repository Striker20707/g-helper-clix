// Lets G-Helper's own updater offer Clix rebuilds.
//
// Every Clix release sits under the official version it was built from (for example v0.291), because the
// updater compares that number. A new Clix build of the same version (a fix or feature) therefore looked
// "not newer" and was never offered. The release title carries "(Clix build N)", so for an equal version the
// build number decides. A newer official version is offered exactly as before.

using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GHelper.Peripherals.Keyboard.Gk6x
{
    public static class ClixUpdate
    {
        /// <summary>The "N" of "(Clix build N)" in a release title, or -1.</summary>
        public static int BuildFromTitle(string? title)
        {
            if (string.IsNullOrEmpty(title)) return -1;
            var m = Regex.Match(title, @"Clix build (\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : -1;
        }

        /// <summary>Same official version, newer Clix build.</summary>
        public static bool IsNewerBuild(JsonElement release, Version gitVersion, Version appVersion)
        {
            try
            {
                if (!SameVersion(gitVersion, appVersion)) return false;
                string? title = release.TryGetProperty("name", out var n) ? n.GetString() : null;
                return BuildFromTitle(title) > Gk6xBuild.Number;
            }
            catch { return false; }
        }

        /// <summary>
        /// "0.291" (the tag) and 0.291.0.0 (the exe) are the same version. Version.CompareTo says the tag is
        /// smaller, because its unset parts are -1, so compare the parts with unset ones counted as 0.
        /// </summary>
        public static bool SameVersion(Version a, Version b) =>
            a.Major == b.Major && a.Minor == b.Minor && Math.Max(a.Build, 0) == Math.Max(b.Build, 0) && Math.Max(a.Revision, 0) == Math.Max(b.Revision, 0);

        /// <summary>
        /// What the update prompt and label show, and what "skip this version" remembers: "0.291 (Clix build 9)",
        /// so skipping one Clix build doesn't hide the next one.
        /// </summary>
        public static string Describe(JsonElement release, string tag)
        {
            try
            {
                string? title = release.TryGetProperty("name", out var n) ? n.GetString() : null;
                int b = BuildFromTitle(title);
                return b > 0 ? $"{tag} (Clix build {b})" : tag;
            }
            catch { return tag; }
        }
    }
}
