// Our own tests (not from upstream): DOOM's own font as pictures of text (src/Host/DoomFont.cs), on the real shareware game data.
using System;
using System.Linq;
using DoomedSpire.Host;
using ManagedDoom;
using ManagedDoom.Video;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class DoomFontTests
    {
        private static int Opaque(Rgba p) => Enumerable.Range(0, p.Width * p.Height).Count(i => p.Data[4 * i + 3] == 255);

        [TestMethod]
        public void ATextIsAsWideAsTheEnginesOwnMeasureAndHasPixels()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var font = DoomFont.Load(wad);
                Assert.IsTrue(font != null);
                var engine = new DrawScreen(wad, 320, 200);
                foreach (var text in new[] { "CHOOSE A GAME", "DOOM shareware (built in)", "FREEDOOM1.WAD   (27.0 MB)", "Save more DOOM games (.wad files) in:", "~/.local/share" })
                {
                    var picture = font.Render(text);
                    Assert.AreEqual(Math.Max(1, engine.MeasureText(text, 1)), picture.Width, text);
                    Assert.AreEqual(font.Measure(text), engine.MeasureText(text, 1), text);
                    Assert.IsTrue(Opaque(picture) > text.Count(char.IsLetterOrDigit) * 12, text + ": the letters are drawn");
                    Assert.IsTrue(picture.Height >= 8 && picture.Height <= 9, "one line is 8 or 9 pixels tall: " + picture.Height);
                }
                Assert.IsTrue(font.Render("abc xyz").Data.SequenceEqual(font.Render("ABC XYZ").Data), "a small letter is drawn as its capital, as the engine does");
                Assert.AreEqual(20, font.Skull(0).Width);
                Assert.AreEqual(19, font.Skull(1).Height);
                Assert.IsTrue(font.Skull(0).Data.SequenceEqual(font.Skull(1).Data) == false, "the two skull pictures differ");
            }
        }

        [TestMethod]
        public void NoLetterIsCutOffAndTheTextIsRedLikeTheGamesOwn()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var font = DoomFont.Load(wad);
                var all = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:;!?()-_+=/[]'\"#$%&*<>@";
                // the picture holds every pixel of every letter: the same count as the patches themselves
                var expected = 0;
                foreach (var c in all)
                {
                    var lump = wad.GetLumpNumber("STCFN" + ((int)c).ToString("000"));
                    if (lump != -1) expected += DoomPatch.Decode(wad.ReadLump(lump)).Pixels.Count(p => p >= 0);
                }
                var picture = font.Render(all);
                Assert.AreEqual(expected, Opaque(picture), "no pixel of any letter is lost (a letter that sits lower, like the comma, is not cut off)");
                var reds = Enumerable.Range(0, picture.Width * picture.Height).Where(i => picture.Data[4 * i + 3] == 255).Count(i => picture.Data[4 * i] > picture.Data[4 * i + 1] + 40);
                Assert.IsTrue(reds > Opaque(picture) / 2, "the font is the game's red one");
            }
        }

        [TestMethod]
        public void ALongNameIsCutWithDotsToFitOneLineAndAShortOneIsLeftAlone()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var font = DoomFont.Load(wad);
                Assert.AreEqual("doom.wad", font.Truncate("doom.wad", 198));
                var cut = font.Truncate("my very long custom megawad name here.wad", 198);
                Assert.IsTrue(cut.EndsWith("...") && font.Measure(cut) <= 198 && cut.Length < 41, cut);
                Assert.IsTrue(font.Measure(font.Truncate("my very long custom megawad name here.wad", 198).Replace("...", "") + "W") > 198 - 12 || cut.Length > 20, "it is cut where the line is full, not far short of it");
                Assert.AreEqual("a b", font.Truncate("a\nb", 198));
                var up = font.Arrow(true);
                var down = font.Arrow(false);
                Assert.IsTrue(up != null && down != null && up.Width == down.Width && up.Height == down.Height);
                Assert.IsFalse(up.Data.SequenceEqual(down.Data), "down is up turned over");
                Assert.IsTrue(Opaque(up) == Opaque(down) && Opaque(up) > 5);
            }
        }

        [TestMethod]
        public void LongTextIsWrappedAtSpacesAndLongWordsAreBrokenToFit()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var font = DoomFont.Load(wad);
                var text = "Choose one of your saved games to play with the new look and the same old demons";
                var lines = font.Wrap(text, 120);
                Assert.IsTrue(lines.Count > 3);
                Assert.IsTrue(lines.All(l => font.Measure(l) <= 120), "every line fits");
                Assert.AreEqual(text, string.Join(" ", lines), "nothing is lost or added when cutting at spaces");
                var path = "/home/someone/.local/share/SlayTheSpire2/DoomedSpire/wads";
                var broken = font.Wrap(path, 100);
                Assert.IsTrue(broken.Count > 1 && broken.All(l => font.Measure(l) <= 100), "a word wider than a line is broken");
                Assert.AreEqual(path, string.Concat(broken), "and nothing is lost");
                Assert.AreEqual(2, font.Wrap("a\nb", 100).Count, "a new-line starts a line");
                var picture = font.Render(text, 120);
                Assert.IsTrue(picture.Width <= 120);
                Assert.AreEqual((lines.Count - 1) * DoomFont.LineHeight + 8, picture.Height);
                // centred: a short line sits in the middle of the picture
                var two = font.Render("WIDE LINE HERE\nI", 0, centred: true);
                int FirstOpaque(Rgba p, int row) => Enumerable.Range(0, p.Width).First(x => p.Data[4 * ((row) * p.Width + x) + 3] == 255);
                Assert.IsTrue(FirstOpaque(two, DoomFont.LineHeight + 1) > 20, "the short line starts well in from the left");
                Assert.AreEqual(1, font.Render("").Width, "no text is a 1 pixel picture");
            }
        }
    }
}
