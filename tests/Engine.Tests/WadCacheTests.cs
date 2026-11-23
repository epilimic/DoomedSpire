// Our own tests (not from upstream): writing the embedded game data to disk, src/Host/WadCache.cs. No WAD needed; a few bytes stand in for it.
using System;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class WadCacheTests
    {
        private static readonly byte[] Data = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();

        private static string NewFolder() => Path.Combine(Path.GetTempPath(), "doomedspire-test-" + Guid.NewGuid().ToString("N"), "data");

        private static Func<Stream> Source(byte[] bytes) => () => new MemoryStream(bytes);

        [TestMethod]
        public void WritesTheFileAndCreatesTheFolder()
        {
            var folder = NewFolder();
            var path = WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            Assert.AreEqual(Path.Combine(folder, "game-1.wad"), path);
            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(Data));
            Assert.IsFalse(File.Exists(path + ".tmp"), "no temporary file is left behind");
        }

        [TestMethod]
        public void LeavesAnExistingFileOfTheRightLengthAlone()
        {
            var folder = NewFolder();
            var path = WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            var marker = Data.Select(b => (byte)(b ^ 0xFF)).ToArray(); // same length, different bytes: proves nothing was written again
            File.WriteAllBytes(path, marker);
            WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(marker));
        }

        [TestMethod]
        public void WritesAgainWhenTheFileIsTheWrongLength()
        {
            var folder = NewFolder();
            var path = WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            File.WriteAllBytes(path, Data.Take(1234).ToArray()); // a short file, as a crash half way through an older write might have left
            WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(Data));
        }

        [TestMethod]
        public void ALeftoverTemporaryFileDoesNotGetInTheWay()
        {
            var folder = NewFolder();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "game-1.wad.tmp"), new byte[9000]); // longer than the real data
            var path = WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            Assert.IsTrue(File.ReadAllBytes(path).SequenceEqual(Data));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }

        [TestMethod]
        public void ANewVersionNameGetsItsOwnFile()
        {
            var folder = NewFolder();
            var a = WadCache.EnsureOnDisk(folder, "game-1.wad", Source(Data));
            var newer = Data.Concat(new byte[] { 1, 2, 3 }).ToArray();
            var b = WadCache.EnsureOnDisk(folder, "game-2.wad", Source(newer));
            Assert.IsTrue(a != b);
            Assert.IsTrue(File.ReadAllBytes(a).SequenceEqual(Data), "the old file is untouched");
            Assert.IsTrue(File.ReadAllBytes(b).SequenceEqual(newer));
        }

        [TestMethod]
        public void AMissingSourceThrowsAndWritesNothing()
        {
            var folder = NewFolder();
            var threw = false;
            try
            {
                WadCache.EnsureOnDisk(folder, "game-1.wad", () => throw new FileNotFoundException("not in the dll"));
            }
            catch (FileNotFoundException)
            {
                threw = true;
            }
            Assert.IsTrue(threw);
            Assert.IsFalse(File.Exists(Path.Combine(folder, "game-1.wad")));
        }

        [TestMethod]
        public void TheBuiltInGameInTheAssetsFolderIsTheShareware19()
        {
            // The file that gets embedded: check it is the original shareware DOOM 1.9 the mod expects, and that the engine can start a game from a copy made the way the mod makes it.
            var asset = Path.GetFullPath("../../assets/doom/doom1.wad");
            Assert.IsTrue(File.Exists(asset), "assets/doom/doom1.wad is missing from the repo");
            Assert.AreEqual(4196020L, new FileInfo(asset).Length);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var file = File.OpenRead(asset))
            {
                Assert.AreEqual("f0cefca49926d00903cf57551d901abe", BitConverter.ToString(md5.ComputeHash(file)).Replace("-", "").ToLowerInvariant());
            }
            Assert.IsTrue(WadLibrary.IsGame(asset), "it is a whole game (IWAD)");

            var path = WadCache.EnsureOnDisk(NewFolder(), "doom1-shareware-1.9.wad", () => File.OpenRead(asset));
            using (var session = DoomSession.Create(path))
            {
                var rgba = new byte[session.Width * session.Height * 4];
                session.RenderFirstFrame(rgba);
                Assert.IsTrue(rgba.Any(b => b != 0), "the first frame should not be empty");
            }
        }
    }
}
