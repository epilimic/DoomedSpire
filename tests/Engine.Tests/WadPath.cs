// Replaces upstream's WadPath.cs. The upstream tests name the commercial WADs (DOOM.WAD, DOOM2.WAD, ...); here Freedoom stands in for them (except DOOM1.WAD, the shareware game, which is in the repo), because it is
// built as a drop-in replacement for DOOM2.WAD (freedoom2.wad) and DOOM.WAD (freedoom1.wad). Any WAD that is not available is a path to a file that does not
// exist, so the test using it is reported as SKIPPED, not as failed.
using System;
using System.IO;

namespace ManagedDoomTest
{
    public static class WadPath
    {
        // Where to look: the environment variable FREEDOOM_DIR, then tests/Engine.Tests/wads, then the folders the Linux packages use.
        private static readonly string[] Folders =
        {
            Environment.GetEnvironmentVariable("FREEDOOM_DIR") ?? "",
            "wads",
            "/usr/share/games/doom", // Debian / Ubuntu package "freedoom"
            "/usr/share/doom",       // Arch-style packages (freedoom1, freedoom2); not checked by me
        };

        private static string Find(string fileName)
        {
            foreach (var folder in Folders)
            {
                if (folder.Length == 0) continue;
                var path = Path.Combine(folder, fileName);
                if (File.Exists(path)) return path;
            }
            return "MISSING-" + fileName;
        }

        // The one original DOOM we have: the shareware game that the mod ships, in the repo (assets/doom/doom1.wad).
        public static readonly string Doom1Shareware = File.Exists("../../assets/doom/doom1.wad") ? Path.GetFullPath("../../assets/doom/doom1.wad") : "MISSING-DOOM1.WAD";
        public static readonly string Doom1 = Find("freedoom1.wad");
        public static readonly string Doom2 = Find("freedoom2.wad");
        public static readonly string Tnt = "MISSING-TNT.WAD";
        public static readonly string Plutonia = "MISSING-PLUTONIA.WAD";

        public static readonly string Requiem = "MISSING-REQUIEM.WAD";
        public static readonly string TntBlood = "MISSING-TNTBLOOD.WAD";
        public static readonly string MementoMori = "MISSING-MM.WAD";
    }
}
