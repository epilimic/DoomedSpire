// Our own tests (not from upstream): finding the games the player saved locally, src/Host/WadLibrary.cs. Tiny fake files stand in for WADs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class WadLibraryTests
    {
        private static string NewFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "doomedspire-wads-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Write(string folder, string name, string magic, int extra = 100) =>
            File.WriteAllBytes(Path.Combine(folder, name), System.Text.Encoding.ASCII.GetBytes(magic).Concat(new byte[extra]).ToArray());

        [TestMethod]
        public void AFolderThatDoesNotExistHasNoGames()
        {
            Assert.AreEqual(0, WadLibrary.FindGames(Path.Combine(Path.GetTempPath(), "doomedspire-nothing-" + Guid.NewGuid().ToString("N"))).Count);
        }

        [TestMethod]
        public void FindsWholeGamesAndLeavesTheRestOut()
        {
            var folder = NewFolder();
            Write(folder, "doom.wad", "IWAD");
            Write(folder, "MYGAME.WAD", "IWAD", 500);          // capital letters are fine
            Write(folder, "addon.wad", "PWAD");                  // needs a base game
            Write(folder, "notes.txt", "IWAD");                  // not a .wad
            File.WriteAllBytes(Path.Combine(folder, "tiny.wad"), new byte[] { 1, 2 }); // shorter than the header
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            Write(Path.Combine(folder, "sub"), "deeper.wad", "IWAD"); // subfolders are not searched

            var reports = new List<string>();
            var games = WadLibrary.FindGames(folder, reports.Add);

            Assert.IsTrue(new[] { "doom.wad", "MYGAME.WAD" }.SequenceEqual(games.Select(g => g.Name)), string.Join(", ", games.Select(g => g.Name)));
            Assert.AreEqual(Path.Combine(folder, "doom.wad"), games[0].Path);
            Assert.AreEqual(104L, games[0].Size);
            Assert.AreEqual(504L, games[1].Size);
            Assert.AreEqual(1, reports.Count(r => r.Contains("addon.wad")), "the add-on is reported");
            Assert.IsFalse(reports.Any(r => r.Contains("notes.txt")), "a non-WAD file is ignored quietly");
        }

        [TestMethod]
        public void AUnreadableFileIsReportedAndDoesNotStopTheRest()
        {
            var folder = NewFolder();
            Write(folder, "a.wad", "IWAD");
            Write(folder, "b.wad", "IWAD");
            var games = WadLibrary.FindGames(folder);
            Assert.AreEqual(2, games.Count);
        }

        [TestMethod]
        public void TheGamesAreInAlphabeticalOrderWhateverTheCase()
        {
            var folder = NewFolder();
            foreach (var name in new[] { "zeta.wad", "Alpha.wad", "beta.WAD" }) Write(folder, name, "IWAD");
            Assert.IsTrue(new[] { "Alpha.wad", "beta.WAD", "zeta.wad" }.SequenceEqual(WadLibrary.FindGames(folder).Select(g => g.Name)));
        }

        [TestMethod]
        public void ThePickerListsFileNamesAndLeavesOutTheSharewareGameOnlyWhenDoomWadIsThere()
        {
            WadFile F(string name) => new WadFile(name, "/x/" + name, 12345678);
            var plain = WadLibrary.PickerChoices(new[] { F("freedoom1.wad"), F("doom2.wad") }, "DOOM shareware");
            Assert.IsTrue(new[] { "DOOM shareware", "freedoom1.wad", "doom2.wad" }.SequenceEqual(plain.Select(c => c.Text)), "the shareware game first, then the games by file name, with no size");
            Assert.IsTrue(plain[0].Wad == null && plain[1].Wad != null);
            foreach (var name in new[] { "doom.wad", "DOOM.WAD", "Doom.wad" })
            {
                var full = WadLibrary.PickerChoices(new[] { F("freedoom1.wad"), F(name) }, "DOOM shareware");
                Assert.IsTrue(new[] { "freedoom1.wad", name }.SequenceEqual(full.Select(c => c.Text)), "with " + name + " the shareware game is not listed");
            }
            Assert.IsTrue(WadLibrary.PickerChoices(new[] { F("doom2.wad"), F("doomu.wad") }, "DOOM shareware").Any(c => c.Wad == null), "other games do not hide it");
            Assert.AreEqual(1, WadLibrary.PickerChoices(new WadFile[0], "DOOM shareware").Count);
        }
    }
}
