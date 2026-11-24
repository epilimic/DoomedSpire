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
        private static IReadOnlyList<(string Name, byte[] Data)> Theme(Wad wad) => KeycardTheme.Build(wad, Symbols());

        /// <summary>Stand-ins for the game's three symbol pictures: 64 x 48 with a transparent margin, a plus sign (white, so it is tinted) in the first, the same in the melody's own colour in the others.</summary>
        private static Dictionary<string, Rgba> Symbols((byte R, byte G, byte B)? colour = null)
        {
            var result = new Dictionary<string, Rgba>();
            var paths = KeycardTheme.SymbolPaths;
            for (var n = 0; n < paths.Count; n++)
            {
                var data = new byte[64 * 48 * 4];
                var c = n == 0 ? (R: (byte)255, G: (byte)255, B: (byte)255) : n == 1 ? (R: (byte)255, G: (byte)40, B: (byte)40) : (R: (byte)40, G: (byte)255, B: (byte)40);
                for (var y = 8; y < 40; y++)
                {
                    for (var x = 16; x < 48; x++)
                    {
                        var bar = (x >= 29 && x < 35) || (y >= 21 && y < 27);
                        if (n == 2) bar = (x + y) % 16 < 6;
                        if (!bar) continue;
                        var o = 4 * (y * 64 + x);
                        data[o] = c.R; data[o + 1] = c.G; data[o + 2] = c.B; data[o + 3] = 255;
                    }
                }
                result[paths[n]] = new Rgba(64, 48, data);
            }
            return result;
        }

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
        public void TheCardsAreReplacedByTheGamesSymbolPicturesMadeSmall()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var lumps = Theme(wad).ToDictionary(l => l.Name, l => l.Data);
                foreach (var letter in "BYR")
                {
                    var original = DoomPatch.Decode(wad.ReadLump(letter + "KEYA0"));
                    var frames = new[] { "A", "B" }.Select(f => DoomPatch.Decode(lumps[letter + "KEY" + f + "0"])).ToArray();
                    foreach (var themed in frames)
                    {
                        Assert.IsTrue(Math.Max(themed.Width, themed.Height) == KeycardTheme.SymbolSize, $"{letter}: the longer side is {KeycardTheme.SymbolSize} pixels ({themed.Width} x {themed.Height})");
                        Assert.IsTrue(themed.Width < themed.Height || letter == 'R' || themed.Width == themed.Height, $"{letter}: the picture's proportions are kept");
                        Assert.AreEqual(themed.Height + 3, themed.Top, "hung 3 pixels above the floor like the card was");
                        Assert.AreEqual(themed.Width / 2, themed.Left, "centred");
                        Assert.IsTrue(themed.Pixels.Count(p => p >= 0) >= 30, $"{letter}: the symbol is drawn");
                        Assert.IsTrue(themed.Pixels.Count(p => p < 0) > 0, $"{letter}: and is not a square");
                        Assert.AreEqual(-1, themed.Pixels[0], "the transparent margin of the picture is cropped away, but its corners stay clear");
                    }
                    Assert.IsFalse(frames[0].Pixels.SequenceEqual(frames[1].Pixels), $"{letter}: the second frame is lighter so it flashes");
                    int Brightest(int[] px) => px.Where(p => p >= 0).Select(p => palette.Colour(p)).Max(c => c.R + c.G + c.B);
                    Assert.IsTrue(Brightest(frames[1].Pixels) > Brightest(frames[0].Pixels), $"{letter}: frame B is lighter");
                }
                // a white picture takes the melody's colour (Cho: a light blue, not a green), a coloured one keeps its own
                var cho = DoomPatch.Decode(lumps["BKEYA0"]).Pixels.Where(p => p >= 0).Select(p => palette.Colour(p)).ToList();
                Assert.IsTrue(cho.All(c => c.B > c.G + 20 && c.B > c.R + 20), "Cho's symbol is blue");
                Assert.IsTrue(DoomPatch.Decode(lumps["YKEYA0"]).Pixels.Where(p => p >= 0).Select(p => palette.Colour(p)).All(c => c.R > 150 && c.G < 120), "a red picture stays red");
                Assert.IsFalse(lumps["BKEYA0"].SequenceEqual(lumps["RKEYA0"]) || lumps["BKEYA0"].SequenceEqual(lumps["YKEYA0"]), "the three symbols are different");
                Assert.IsTrue(lumps.ContainsKey(KeycardTheme.Marker));
            }
        }

        [TestMethod]
        public void ACardWhoseSymbolPictureIsMissingOrEmptyKeepsTheGamesOwnCard()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var symbols = Symbols();
                symbols.Remove(KeycardTheme.SymbolPaths[1]);
                symbols[KeycardTheme.SymbolPaths[2]] = new Rgba(8, 8, new byte[8 * 8 * 4]);
                var warnings = new List<string>();
                var lumps = KeycardTheme.Build(wad, symbols, warnings.Add).ToDictionary(l => l.Name, l => l.Data);
                Assert.IsTrue(lumps.ContainsKey("BKEYA0") && lumps.ContainsKey("BKEYB0"));
                Assert.IsFalse(lumps.ContainsKey("YKEYA0") || lumps.ContainsKey("YKEYB0"), "no picture: the game's card stays");
                Assert.IsFalse(lumps.ContainsKey("RKEYA0") || lumps.ContainsKey("RKEYB0"), "an empty picture: the game's card stays");
                Assert.IsTrue(warnings.Any(w => w.Contains("symbol_wah.png")), "and an empty one is reported");
                Assert.IsTrue(KeycardTheme.Build(wad).All(l => !l.Name.EndsWith("KEYA0")), "with no pictures at all no card is changed");
            }
        }

        [TestMethod]
        public void TheStatusBarIconsAreTheSymbolsMadeSevenBySevenLikeTheSkullKeys()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var lumps = Theme(wad).ToDictionary(l => l.Name, l => l.Data);
                var skull = DoomPatch.Decode(wad.ReadLump("STKEYS3"));
                for (var i = 0; i < 3; i++)
                {
                    var icon = DoomPatch.Decode(lumps["STKEYS" + i]);
                    Assert.AreEqual((skull.Width, skull.Height, skull.Left, skull.Top), (icon.Width, icon.Height, icon.Left, icon.Top), $"STKEYS{i} is the size and place of a skull key's icon (7 x 7)");
                    Assert.IsTrue(icon.Pixels.Count(p => p >= 0) >= 8, $"STKEYS{i} shows the symbol");
                    Assert.IsTrue(icon.Pixels.Any(p => p < 0), $"STKEYS{i} is not a filled square");
                }
                // Cho's is a blue (the palette has no cyan, and its nearest colour is a green), Ki's is yellow
                Assert.IsTrue(DoomPatch.Decode(lumps["STKEYS0"]).Pixels.Where(p => p >= 0).Select(p => palette.Colour(p)).All(c => c.B > c.G + 20 && c.B > c.R + 20), "Cho's icon is blue");
                Assert.IsTrue(DoomPatch.Decode(lumps["STKEYS1"]).Pixels.Where(p => p >= 0).Select(p => palette.Colour(p)).All(c => c.R > 150 && c.G < 120), "a red picture stays red");
                Assert.IsFalse(lumps["STKEYS0"].SequenceEqual(lumps["STKEYS2"]), "the icons differ");
                // the skull keys' icons are not touched, and a card with no symbol picture keeps the game's icon
                Assert.IsFalse(lumps.ContainsKey("STKEYS3") || lumps.ContainsKey("STKEYS4") || lumps.ContainsKey("STKEYS5"));
                var symbols = Symbols();
                symbols.Remove(KeycardTheme.SymbolPaths[1]);
                var some = KeycardTheme.Build(wad, symbols).ToDictionary(l => l.Name, l => l.Data);
                Assert.IsTrue(some.ContainsKey("STKEYS0") && !some.ContainsKey("STKEYS1") && some.ContainsKey("STKEYS2"));
                Assert.IsFalse(KeycardTheme.Build(wad).Any(l => l.Name.StartsWith("STKEYS")), "with no pictures at all no icon is changed");
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
                    Assert.AreEqual("Picked up a Blue Cho Melody.", DoomInfo.Strings.GOTBLUECARD.ToString());
                    Assert.AreEqual("Picked up a Yellow Ki Melody.", DoomInfo.Strings.GOTYELWCARD.ToString());
                    Assert.AreEqual("Picked up a Red Wah Melody.", DoomInfo.Strings.GOTREDCARD.ToString());
                    Assert.AreEqual("You need a Blue Cho Melody to open this", DoomInfo.Strings.PD_BLUEK.ToString());
                    Assert.AreEqual("You need a Red Wah Melody to activate this", DoomInfo.Strings.PD_REDO.ToString());
                    Assert.AreEqual(skull, DoomInfo.Strings.GOTBLUESKUL.ToString(), "the skull keys keep their words");
                    // every message fits on the 320 pixel wide screen (the game does not wrap them, it cuts them off)
                    var font = new ManagedDoom.Video.DrawScreen(wad, 320, 200);
                    foreach (var text in new[] { DoomInfo.Strings.GOTBLUECARD, DoomInfo.Strings.GOTYELWCARD, DoomInfo.Strings.GOTREDCARD, DoomInfo.Strings.PD_BLUEK, DoomInfo.Strings.PD_YELLOWK, DoomInfo.Strings.PD_REDK, DoomInfo.Strings.PD_BLUEO, DoomInfo.Strings.PD_YELLOWO, DoomInfo.Strings.PD_REDO })
                    {
                        Assert.IsTrue(font.MeasureText(text.ToString(), 1) <= 320, $"\"{text}\" is {font.MeasureText(text.ToString(), 1)} pixels wide");
                    }
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
