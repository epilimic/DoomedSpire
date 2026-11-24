// Our own tests (not from upstream): DOOM's keycards as the Fool's melody cards (src/Host/KeycardTheme.cs), on the real shareware game data.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using ManagedDoom;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class KeycardTests
    {
        private static IReadOnlyList<(string Name, byte[] Data)> Theme(Wad wad, IReadOnlyDictionary<string, Rgba> art = null) => KeycardTheme.Build(wad, art);

        private static (double R, double G, double B) Average(int[] pixels, DoomPalette palette, Func<int, bool> include = null)
        {
            var colours = pixels.Select((c, i) => (c, i)).Where(p => p.c >= 0 && (include == null || include(p.i))).Select(p => palette.Colour(p.c)).ToList();
            return (colours.Average(c => (double)c.R), colours.Average(c => (double)c.G), colours.Average(c => (double)c.B));
        }

        [TestMethod]
        public void APatchReadsBackIntoItsPixels()
        {
            var pixels = new[] { 1, 2, -1, 3, 4, 5 };
            var back = DoomPatch.Decode(DoomPatch.Encode(3, 2, -4, 7, pixels));
            Assert.AreEqual((3, 2, -4, 7), (back.Width, back.Height, back.Left, back.Top));
            Assert.IsTrue(back.Pixels.SequenceEqual(pixels));
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var card = DoomPatch.Decode(wad.ReadLump("BKEYA0"));
                Assert.AreEqual((14, 16), (card.Width, card.Height));
                Assert.IsTrue(card.Pixels.Count(p => p >= 0) > 150);
            }
        }

        [TestMethod]
        public void TheThreeCardsKeepTheirShapeSizeAndLightsAndGetASymbolOnTheirFace()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var lumps = Theme(wad).ToDictionary(l => l.Name, l => l.Data);
                var symbolPixels = new Dictionary<char, int> { ['B'] = 15, ['Y'] = 14, ['R'] = 14 }; // the pixels in the symbols of Cho (rings), Ki (cross) and Wah (zigzag)
                foreach (var letter in "BYR")
                {
                    var a = DoomPatch.Decode(wad.ReadLump(letter + "KEYA0"));
                    var b = DoomPatch.Decode(wad.ReadLump(letter + "KEYB0"));
                    var lights = Enumerable.Range(0, a.Pixels.Length).Where(i => a.Pixels[i] != b.Pixels[i]).ToList();
                    Assert.IsTrue(lights.Count >= 3, "the lights on the card's edge are found");
                    foreach (var (frame, original) in new[] { ("A", a), ("B", b) })
                    {
                        var name = letter + "KEY" + frame + "0";
                        var themed = DoomPatch.Decode(lumps[name]);
                        Assert.AreEqual((original.Width, original.Height, original.Left, original.Top), (themed.Width, themed.Height, themed.Left, themed.Top), name + " keeps its size and place");
                        Assert.IsTrue(original.Pixels.Select(p => p >= 0).SequenceEqual(themed.Pixels.Select(p => p >= 0)), name + " keeps its outline");
                        foreach (var i in lights) Assert.AreEqual(original.Pixels[i], themed.Pixels[i], name + ": the lights are not touched");
                        var changed = Enumerable.Range(0, original.Pixels.Length).Count(i => original.Pixels[i] != themed.Pixels[i]);
                        Assert.IsTrue(changed >= symbolPixels[letter], $"{name}: the symbol (and its edge) is drawn on the face ({changed} pixels changed)");
                        // the symbol's own pixels are all one light colour
                        var ink = themed.Pixels.Where((p, i) => original.Pixels[i] != p).GroupBy(p => p).OrderByDescending(g => g.Count()).First();
                        Assert.IsTrue(ink.Count() >= symbolPixels[letter] - 2, $"{name}: the symbol is in one colour ({ink.Count()} pixels)");
                    }
                }
                Assert.IsFalse(lumps["BKEYA0"].SequenceEqual(lumps["RKEYA0"]) || lumps["BKEYA0"].SequenceEqual(lumps["YKEYA0"]), "the three cards are different");
                // the status bar keeps the game's own icons until the owner paints them
                Assert.IsFalse(lumps.ContainsKey("STKEYS0") || lumps.ContainsKey("STKEYS1") || lumps.ContainsKey("STKEYS2"));
                Assert.IsTrue(lumps.ContainsKey(KeycardTheme.Marker));
            }
        }

        [TestMethod]
        public void TheOwnersStatusBarIconsAreUsedWhenTheyAreTheRightSizeAndReportedAndLeftOutIfNot()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var data = new byte[7 * 5 * 4];
                for (var i = 0; i < 35; i++) { data[4 * i] = 255; data[4 * i + 1] = 0; data[4 * i + 2] = 255; data[4 * i + 3] = i % 2 == 0 ? (byte)255 : (byte)0; }
                var warnings = new List<string>();
                var art = new Dictionary<string, Rgba> { ["small_cho.png"] = new Rgba(7, 5, data), ["small_ki.png"] = new Rgba(8, 5, new byte[8 * 5 * 4]) };
                var lumps = KeycardTheme.Build(wad, art, warnings.Add).ToDictionary(l => l.Name, l => l.Data);
                var cho = DoomPatch.Decode(lumps["STKEYS0"]);
                Assert.IsTrue(cho.Pixels.Where((p, i) => i % 2 == 1).All(p => p == -1), "the owner's transparent pixels are transparent");
                Assert.IsTrue(cho.Pixels.Where((p, i) => i % 2 == 0).All(p => p >= 0 && palette.Colour(p).R > 150 && palette.Colour(p).B > 100), "and its pixels are the owner's");
                Assert.AreEqual((0, -1), (cho.Left, cho.Top), "in the place of the game's own icon");
                Assert.IsTrue(warnings.Any(w => w.Contains("small_ki.png") && w.Contains("7 x 5")), "a picture of the wrong size is reported");
                Assert.IsFalse(lumps.ContainsKey("STKEYS1"), "and left out, so the game's own icon stays");
                Assert.IsFalse(lumps.ContainsKey("STKEYS2"), "as is the one with no picture at all");
            }
        }

        [TestMethod]
        public void ACyanStatusBarIconIsShownAsLightBlueNotGreen()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var data = new byte[7 * 5 * 4];
                var shades = new[] { (0, 240, 247), (0, 180, 185), (0, 120, 124), (0, 60, 62) };
                for (var i = 0; i < 35; i++) { var s = shades[i % shades.Length]; data[4 * i] = (byte)s.Item1; data[4 * i + 1] = (byte)s.Item2; data[4 * i + 2] = (byte)s.Item3; data[4 * i + 3] = 255; }
                var lumps = KeycardTheme.Build(wad, new Dictionary<string, Rgba> { ["small_cho.png"] = new Rgba(7, 5, data) }).ToDictionary(l => l.Name, l => l.Data);
                foreach (var p in DoomPatch.Decode(lumps["STKEYS0"]).Pixels)
                {
                    var c = palette.Colour(p);
                    Assert.IsTrue(c.B > c.G + 20 && c.B > c.R + 20, $"a cyan pixel is a blue ({c.R},{c.G},{c.B}), not a green or a grey");
                }
                // pink and yellow are simply the nearest the palette has
                var yellow = new byte[7 * 5 * 4];
                for (var i = 0; i < 35; i++) { yellow[4 * i] = 255; yellow[4 * i + 1] = 255; yellow[4 * i + 2] = 0; yellow[4 * i + 3] = 255; }
                var ki = DoomPatch.Decode(KeycardTheme.Build(wad, new Dictionary<string, Rgba> { ["small_ki.png"] = new Rgba(7, 5, yellow) }).ToDictionary(l => l.Name, l => l.Data)["STKEYS1"]);
                Assert.IsTrue(ki.Pixels.All(p => palette.Colour(p) == (255, 255, 0)));
            }
        }

        [TestMethod]
        public void TheCardsAppearInTheGameAndTheMessagesNameThemButNotTheSkullKeys()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var path = Path.Combine(Path.GetTempPath(), "doomedspire-keys-" + Guid.NewGuid().ToString("N") + ".wad");
                File.WriteAllBytes(path, PwadWriter.Build(Theme(wad)));
                var skull = DoomInfo.Strings.GOTBLUESKUL.ToString();
                using (var session = DoomSession.Create(WadPath.Doom1Shareware, null, new[] { path }))
                {
                    Assert.AreEqual("Picked up a Blue Cho Melody Card.", DoomInfo.Strings.GOTBLUECARD.ToString());
                    Assert.AreEqual("Picked up a Yellow Ki Melody Card.", DoomInfo.Strings.GOTYELWCARD.ToString());
                    Assert.AreEqual("Picked up a Red Wah Melody Card.", DoomInfo.Strings.GOTREDCARD.ToString());
                    Assert.AreEqual("You need a Blue Cho Melody Card to open this door", DoomInfo.Strings.PD_BLUEK.ToString());
                    Assert.AreEqual("You need a Red Wah Melody Card to activate this object", DoomInfo.Strings.PD_REDO.ToString());
                    Assert.AreEqual(skull, DoomInfo.Strings.GOTBLUESKUL.ToString(), "the skull keys keep their words");
                    for (var i = 0; i < 100; i++) session.Tick();
                }
                using (var withAddOn = new Wad(WadPath.Doom1Shareware, path))
                {
                    var key = new SpriteLookup(withAddOn)[Sprite.BKEY];
                    var original = new SpriteLookup(wad)[Sprite.BKEY];
                    Assert.AreEqual(original.Frames.Length, key.Frames.Length);
                    Assert.IsFalse(ReferenceEquals(key.Frames[0].Patches[0], original.Frames[0].Patches[0]));
                }
                // the next game, without the melody cards, has the game's own words again
                using (var plain = DoomSession.Create(WadPath.Doom1Shareware))
                {
                    Assert.AreEqual("Picked up a blue keycard.", DoomInfo.Strings.GOTBLUECARD.ToString());
                    Assert.AreEqual("You need a blue key to open this door", DoomInfo.Strings.PD_BLUEK.ToString());
                }
            }
        }
    }
}
