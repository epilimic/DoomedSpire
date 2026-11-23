// Our own tests (not from upstream): replacing the status-bar face, src/Host/DoomPatch.cs and FaceFactory.cs, on the real shareware game data.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DoomedSpire.Host;
using ManagedDoom;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class FaceTests
    {
        /// <summary>A made-up portrait to stand in for a character's picture (the real ones are the game's art and are not in the repo): a round face with hair, eyes and a mouth, an outline, and transparent corners.</summary>
        private static Rgba Portrait(int size = 96)
        {
            var data = new byte[size * size * 4];
            double c = (size - 1) / 2.0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var o = 4 * (y * size + x);
                    double dx = x - c, dy = y - c, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > size * 0.46) continue;                      // transparent outside the circle
                    byte r = 226, g = 176, b = 138;                     // skin
                    if (d > size * 0.42) { r = 40; g = 60; b = 120; }   // outline
                    else if (y < size * 0.30) { r = 70; g = 45; b = 30; } // hair
                    else if ((Math.Abs(dx + size * 0.17) < size * 0.07 || Math.Abs(dx - size * 0.17) < size * 0.07) && Math.Abs(dy + size * 0.06) < size * 0.05)
                    { r = 250; g = 250; b = 250; }                       // eyes
                    else if (Math.Abs(dy - size * 0.22) < size * 0.035 && Math.Abs(dx) < size * 0.14) { r = 170; g = 40; b = 50; } // mouth
                    var shade = 1.0 - 0.25 * (dx / size);               // light from the left
                    data[o] = (byte)(r * shade); data[o + 1] = (byte)(g * shade); data[o + 2] = (byte)(b * shade); data[o + 3] = 255;
                }
            }
            return new Rgba(size, size, data);
        }

        private static IReadOnlyList<(string Name, byte[] Data)> Faces(FaceSettings settings, Rgba portrait = null)
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                return FaceFactory.Build(portrait ?? Portrait(), name =>
                {
                    var p = Patch.FromWad(wad, name);
                    return new PatchSpec(p.Width, p.Height, p.LeftOffset, p.TopOffset);
                }, palette, settings);
            }
        }

        /// <summary>The picture's palette indices, row after row, -1 where transparent, read with the engine's own decoder.</summary>
        private static int[] Decode(byte[] lump)
        {
            var patch = Patch.FromData("test", lump);
            var pixels = Enumerable.Repeat(-1, patch.Width * patch.Height).ToArray();
            for (var x = 0; x < patch.Width; x++)
            {
                foreach (var post in patch.Columns[x])
                {
                    for (var k = 0; k < post.Length; k++) pixels[(post.TopDelta + k) * patch.Width + x] = post.Data[post.Offset + k];
                }
            }
            return pixels;
        }

        private static (double Red, double Brightness, double Saturation, double Gold) Look(byte[] lump, DoomPalette palette)
        {
            double red = 0, bright = 0, sat = 0, gold = 0; var n = 0;
            foreach (var i in Decode(lump).Where(i => i >= 0))
            {
                var (r, g, b) = palette.Colour(i);
                red += r - (g + b) / 2.0; bright += (r + g + b) / 3.0; sat += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)); gold += (r + g) / 2.0 - b; n++;
            }
            return (red / n, bright / n, sat / n, gold / n);
        }

        private static byte[] Lump(IReadOnlyList<(string Name, byte[] Data)> faces, string name) => faces.First(f => f.Name == name).Data;

        [TestMethod]
        public void AnEncodedPatchIsReadBackByTheEngineExactly()
        {
            var random = new Random(7);
            const int w = 17, h = 23;
            var pixels = new int[w * h];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = random.Next(5) == 0 ? -1 : random.Next(256);
            for (var y = 0; y < h; y++) pixels[y * w + 3] = -1;   // a column with nothing in it
            for (var y = 0; y < h; y++) pixels[y * w + 4] = y;    // a column with no gaps
            var data = DoomPatch.Encode(w, h, 8, 25, pixels);
            var patch = Patch.FromData("t", data);
            Assert.AreEqual(w, patch.Width); Assert.AreEqual(h, patch.Height);
            Assert.AreEqual(8, patch.LeftOffset); Assert.AreEqual(25, patch.TopOffset);
            Assert.IsTrue(Decode(data).SequenceEqual(pixels), "every pixel comes back as written");
        }

        [TestMethod]
        public void ThePaletteMatchFindsTheColourItself()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                for (var i = 0; i < 256; i++)
                {
                    var (r, g, b) = palette.Colour(i);
                    Assert.AreEqual((r, g, b), palette.Colour(palette.Nearest(r, g, b)), "colour " + i);
                }
                var red = palette.Colour(palette.Nearest(255, 0, 0));
                Assert.IsTrue(red.R > 150 && red.G < 60 && red.B < 60, "pure red matches a red");
            }
        }

        [TestMethod]
        public void TheEngineReadsAnAddOnWadAndPrefersItsLumps()
        {
            var path = Path.Combine(Path.GetTempPath(), "doomedspire-pwad-" + Guid.NewGuid().ToString("N") + ".wad");
            var replacement = DoomPatch.Encode(3, 3, 0, 0, new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            File.WriteAllBytes(path, PwadWriter.Build(new List<(string, byte[])> { ("STFST00", replacement), ("MYLUMP", new byte[] { 9, 8, 7 }) }));
            using (var wad = new Wad(WadPath.Doom1Shareware, path))
            {
                Assert.IsTrue(wad.ReadLump("STFST00").SequenceEqual(replacement), "the add-on's face wins over the game's");
                Assert.IsTrue(wad.ReadLump("MYLUMP").SequenceEqual(new byte[] { 9, 8, 7 }), "a lump only the add-on has is found");
                Assert.IsTrue(wad.GetLumpSize(wad.GetLumpNumber("STFST01")) > 100, "the game's other faces are untouched");
            }
        }

        [TestMethod]
        public void ThereIsAReplacementForEveryFaceTheGameHasAtItsOriginalSize()
        {
            var faces = Faces(FaceSettings.Default);
            Assert.AreEqual(42, faces.Count);
            Assert.AreEqual(42, faces.Select(f => f.Name).Distinct().Count());
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                foreach (var (name, data) in faces)
                {
                    Assert.IsTrue(wad.GetLumpNumber(name) != -1, name + " is not a lump the game has: the list of names is wrong");
                    var original = Patch.FromWad(wad, name);
                    var ours = Patch.FromData(name, data);
                    Assert.AreEqual((original.Width, original.Height, original.LeftOffset, original.TopOffset), (ours.Width, ours.Height, ours.LeftOffset, ours.TopOffset), name);
                }
                // and the other way: every face the game draws (the engine's own list, StatusBarRenderer.cs:461-475) is one of ours
                var drawn = new List<string>();
                for (var i = 0; i < 5; i++)
                {
                    for (var j = 0; j < 3; j++) drawn.Add("STFST" + i + j);
                    drawn.Add("STFTR" + i + "0"); drawn.Add("STFTL" + i + "0"); drawn.Add("STFOUCH" + i); drawn.Add("STFEVL" + i); drawn.Add("STFKILL" + i);
                }
                drawn.Add("STFGOD0"); drawn.Add("STFDEAD0");
                Assert.IsTrue(drawn.OrderBy(n => n).SequenceEqual(faces.Select(f => f.Name).OrderBy(n => n)));

                Directory.CreateDirectory("out");
                var sheet = FaceFactory.Sheet(faces, new DoomPalette(wad.ReadLump("PLAYPAL")), 8);
                Png.Save("out/face-sheet.png", sheet.Width, sheet.Height, sheet.Data); // look at it: tests/Engine.Tests/out/face-sheet.png
            }
        }

        [TestMethod]
        public void TheFacesAreTheSameEveryTime()
        {
            var a = Faces(FaceSettings.Default);
            var b = Faces(FaceSettings.Default);
            Assert.IsTrue(a.Zip(b, (x, y) => x.Name == y.Name && x.Data.SequenceEqual(y.Data)).All(same => same));
        }

        [TestMethod]
        public void LowerHealthLooksPalerRedderAndDarker()
        {
            var faces = Faces(FaceSettings.Default);
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var looks = Enumerable.Range(0, 5).Select(i => Look(Lump(faces, "STFST" + i + "0"), palette)).ToArray();
                for (var i = 1; i < 5; i++)
                {
                    Assert.IsTrue(looks[i].Red > looks[i - 1].Red - 0.5, $"level {i} is at least as red as level {i - 1}");
                    Assert.IsTrue(looks[i].Brightness < looks[i - 1].Brightness + 0.5, $"level {i} is at least as dark as level {i - 1}");
                }
                Assert.IsTrue(looks[4].Red > looks[0].Red + 15, $"the worst level is clearly redder ({looks[0].Red:0} to {looks[4].Red:0})");
                Assert.IsTrue(looks[4].Brightness < looks[0].Brightness - 8, "and clearly darker");
                Assert.IsFalse(Lump(faces, "STFST00").SequenceEqual(Lump(faces, "STFST40")));
            }
        }

        /// <summary>Where each pixel of a face is on screen, relative to where faces are drawn (so faces of different sizes can be compared): position to palette index.</summary>
        private static Dictionary<(int U, int V), int> OnScreen(byte[] lump)
        {
            var patch = Patch.FromData("t", lump);
            var pixels = Decode(lump);
            var map = new Dictionary<(int, int), int>();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] >= 0) map[(i % patch.Width - patch.LeftOffset, i / patch.Width - patch.TopOffset)] = pixels[i];
            }
            return map;
        }

        [TestMethod]
        public void WithTheDamageSettingAtZeroEveryHealthLevelLooksTheSame()
        {
            var faces = Faces(FaceSettings.Default with { Damage = 0f });
            var healthy = OnScreen(Lump(faces, "STFST00"));
            for (var i = 1; i < 5; i++)
            {
                var other = OnScreen(Lump(faces, "STFST" + i + "0"));
                Assert.IsTrue(healthy.Count == other.Count && healthy.All(kv => other.TryGetValue(kv.Key, out var v) && v == kv.Value), "level " + i + " looks like level 0 on screen");
            }
        }

        [TestMethod]
        public void TheFaceKeepsItsSizeAndPlaceAcrossHealthLevelsEvenThoughTheOriginalPicturesDifferInSize()
        {
            // The original face pictures are 24 x 29, 24 x 30 or 24 x 31; a replacement that fills each one separately would grow and shrink as health changes.
            var faces = Faces(FaceSettings.Default);
            var shape = OnScreen(Lump(faces, "STFST00")).Keys.ToHashSet();
            for (var i = 1; i < 5; i++)
            {
                Assert.IsTrue(shape.SetEquals(OnScreen(Lump(faces, "STFST" + i + "0")).Keys), $"level {i} covers the same pixels of the screen as level 0");
            }
            Assert.IsTrue(shape.SetEquals(OnScreen(Lump(faces, "STFEVL0")).Keys), "and so does the grin");
            Assert.IsTrue(shape.Count > 300, "the face really fills its place");
        }

        /// <summary>The average column of the white pixels (the eyes of the test portrait) on screen, or NaN if there are none.</summary>
        private static double EyeColumn(byte[] lump, DoomPalette palette)
        {
            var columns = OnScreen(lump).Where(kv => { var (r, g, b) = palette.Colour(kv.Value); return Math.Min(r, Math.Min(g, b)) > 190; }).Select(kv => (double)kv.Key.U).ToList();
            return columns.Count == 0 ? double.NaN : columns.Average();
        }

        [TestMethod]
        public void TurningFacesTurnTheHeadTowardTheirSideAndGlancesCutNothingOff()
        {
            var (faces, palette, _, wad) = Setup(FaceSettings.Default with { Damage = 0f, Blood = 0f });
            using (wad)
            {
                for (var level = 0; level < 5; level++)
                {
                    var straight = Lump(faces, "STFST" + level + "0");
                    var whole = OnScreen(straight).Count;
                    var right = Lump(faces, "STFTR" + level + "0");
                    var left = Lump(faces, "STFTL" + level + "0");
                    // the head is turned, not cut off: it still fills about what the straight one does, with the outline where it was...
                    foreach (var turn in new[] { right, left })
                    {
                        var shown = OnScreen(turn).Count;
                        Assert.IsTrue(shown > whole * 0.9 && shown < whole * 1.1, $"level {level}: a turned head covers {shown} pixels, a straight one {whole}");
                    }
                    // ...and the features have slid sideways: the turn-left picture is the one drawn (features slid right), the turn-right one is its mirror image
                    var eyes = EyeColumn(straight, palette);
                    Assert.IsFalse(double.IsNaN(eyes), "the test portrait's eyes are found");
                    Assert.IsTrue(EyeColumn(left, palette) > eyes + 1.5, $"level {level}: the turn-left picture has the eyes slid right ({eyes:0.0} to {EyeColumn(left, palette):0.0})");
                    Assert.IsTrue(EyeColumn(right, palette) < eyes - 1.5, $"level {level}: and the turn-right picture, its mirror, has them slid left ({eyes:0.0} to {EyeColumn(right, palette):0.0})");
                    // the glances do not cut anything off: the same columns of the screen are covered as by the straight face, and the eyes have moved a little
                    var straightColumns = OnScreen(straight).Keys.Select(k => k.U).Distinct().OrderBy(u => u).ToList();
                    foreach (var (look, direction) in new[] { ("STFST" + level + "1", -1), ("STFST" + level + "2", 1) })
                    {
                        var glance = Lump(faces, look);
                        var shown = OnScreen(glance).Count;
                        Assert.IsTrue(Math.Abs(whole - shown) <= 6, $"{look} covers {shown} pixels, the straight face {whole}");
                        Assert.IsTrue(OnScreen(glance).Keys.Select(k => k.U).Distinct().OrderBy(u => u).SequenceEqual(straightColumns), $"{look} keeps the head's whole width: nothing cut off on either side");
                        var moved = EyeColumn(glance, palette) - eyes;
                        Assert.IsTrue(direction * moved > 0.3 && direction * moved < 2.5, $"{look}: the eyes move about a pixel the way it looks ({moved:0.0})");
                    }
                }
            }
        }

        [TestMethod]
        public void TheTurnLeftFaceIsTheTurnRightFaceMirroredAtEveryHealthLevel()
        {
            var (faces, palette, _, wad) = Setup(FaceSettings.Default);
            using (wad)
            {
                for (var level = 0; level < 5; level++)
                {
                    var right = OnScreen(Lump(faces, "STFTR" + level + "0"));
                    var left = OnScreen(Lump(faces, "STFTL" + level + "0"));
                    Assert.IsTrue(right.Count > 300, "there is a face");
                    // the straight face is 24 wide and drawn from column 5, so its middle is between columns 16 and 17: a column u goes to 33 - u
                    Assert.AreEqual(right.Count, left.Count, "level " + level + ": same number of pixels");
                    foreach (var (at, colour) in right)
                    {
                        Assert.IsTrue(left.TryGetValue((33 - at.U, at.V), out var mirrored) && mirrored == colour, $"level {level}: turn right at {at} is the same colour as turn left mirrored");
                    }
                }
            }
        }

        /// <summary>The average colour of a face's pixels.</summary>
        private static (double R, double G, double B) Average(byte[] lump, DoomPalette palette)
        {
            var colours = OnScreen(lump).Values.Select(palette.Colour).ToList();
            return (colours.Average(c => (double)c.R), colours.Average(c => (double)c.G), colours.Average(c => (double)c.B));
        }

        [TestMethod]
        public void TheNeutralSettingGivesEveryFaceTheSameColourAndNoBlood()
        {
            var (faces, palette, _, wad) = Setup(FaceSettings.Default with { Neutral = true });
            using (wad)
            {
                var plain = Average(Lump(faces, "STFST00"), palette);
                foreach (var (name, _, _) in FaceFactory.Faces)
                {
                    var (r, g, b) = Average(Lump(faces, name), palette);
                    Assert.IsTrue(Math.Abs(r - plain.R) < 6 && Math.Abs(g - plain.G) < 6 && Math.Abs(b - plain.B) < 6, $"{name} is the same colour overall as the plain face: ({r:0},{g:0},{b:0}) against ({plain.R:0},{plain.G:0},{plain.B:0})");
                }
                // the same pixel of the same face, whatever the health: identical (no paleness, redness or marks)
                var healthy = OnScreen(Lump(faces, "STFST00"));
                for (var i = 1; i < 5; i++)
                {
                    var other = OnScreen(Lump(faces, "STFST" + i + "0"));
                    Assert.IsTrue(healthy.Count == other.Count && healthy.All(kv => other.TryGetValue(kv.Key, out var v) && v == kv.Value), "level " + i + " is the same picture as level 0");
                }
                // and the grin, rampage and god faces are the straight face exactly
                foreach (var name in new[] { "STFEVL4", "STFKILL4", "STFGOD0" })
                {
                    var same = OnScreen(Lump(faces, name));
                    Assert.IsTrue(healthy.Count == same.Count && healthy.All(kv => same.TryGetValue(kv.Key, out var v) && v == kv.Value), name + " is the same picture as the straight face");
                }
            }
        }

        [TestMethod]
        public void TheLooksMoveTheFaceAndTheOtherExpressionsAreDifferent()
        {
            var faces = Faces(FaceSettings.Default);
            var names = new[] { "STFST00", "STFST01", "STFST02", "STFTR00", "STFTL00", "STFOUCH0", "STFEVL0", "STFKILL0", "STFGOD0", "STFDEAD0" };
            for (var i = 0; i < names.Length; i++)
            {
                for (var j = i + 1; j < names.Length; j++)
                {
                    Assert.IsFalse(Lump(faces, names[i]).SequenceEqual(Lump(faces, names[j])), $"{names[i]} and {names[j]} should not be the same picture");
                }
            }
        }

        [TestMethod]
        public void DeadIsGreyAndDarkGodIsGolden()
        {
            var faces = Faces(FaceSettings.Default);
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var normal = Look(Lump(faces, "STFST00"), palette);
                var dead = Look(Lump(faces, "STFDEAD0"), palette);
                var god = Look(Lump(faces, "STFGOD0"), palette);
                Assert.IsTrue(dead.Brightness < normal.Brightness - 20, "dead is much darker");
                Assert.IsTrue(dead.Saturation < normal.Saturation, "and less colourful");
                Assert.IsTrue(god.Gold > normal.Gold + 10, "god mode is golden");
            }
        }

        [TestMethod]
        public void ACropOfThePictureChangesTheFace()
        {
            var whole = Faces(FaceSettings.Default);
            var zoomed = Faces(FaceSettings.Default with { CropLeft = 0.25f, CropTop = 0.25f, CropWidth = 0.5f, CropHeight = 0.5f });
            Assert.IsFalse(Lump(whole, "STFST00").SequenceEqual(Lump(zoomed, "STFST00")));
        }

        [TestMethod]
        public void ANarrowPictureStillGivesAFullFace()
        {
            var wide = new Rgba(40, 10, Enumerable.Range(0, 40 * 10 * 4).Select(i => (byte)(i % 4 == 3 ? 255 : 120)).ToArray());
            var faces = Faces(FaceSettings.Default, wide);
            Assert.IsTrue(Decode(Lump(faces, "STFST00")).All(i => i >= 0), "an opaque picture fills the whole face");
        }

        [TestMethod]
        public void TheFaceShowsInTheGameAndOnlyThere()
        {
            var path = Path.Combine(Path.GetTempPath(), "doomedspire-faces-" + Guid.NewGuid().ToString("N") + ".wad");
            File.WriteAllBytes(path, PwadWriter.Build(Faces(FaceSettings.Default)));

            byte[] plain, custom; int width;
            using (var session = DoomSession.Create(WadPath.Doom1Shareware))
            {
                width = session.Width; plain = new byte[width * session.Height * 4]; session.RenderFirstFrame(plain);
            }
            using (var session = DoomSession.Create(WadPath.Doom1Shareware, null, new[] { path }))
            {
                custom = new byte[plain.Length]; session.RenderFirstFrame(custom);
            }

            int changedInside = 0, changedOutside = 0;
            for (var i = 0; i < plain.Length; i += 4)
            {
                if (plain[i] == custom[i] && plain[i + 1] == custom[i + 1] && plain[i + 2] == custom[i + 2]) continue;
                int p = i / 4, x = p % width, y = p / width;
                if (x >= 270 && x < 350 && y >= 320 && y < 400) changedInside++; else changedOutside++;
            }
            Assert.IsTrue(changedInside > 200, $"the face area should look different, {changedInside} pixels did");
            Assert.AreEqual(0, changedOutside, "nothing else on the screen changes");
        }

        [TestMethod]
        public void TheTunerCanFindTheFaceNumbers()
        {
            var fields = typeof(FaceTuning).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.AreEqual(9, fields.Length);
            foreach (var field in fields)
            {
                Assert.IsFalse(field.IsInitOnly || field.IsLiteral, field.Name);
                Assert.IsTrue(field.FieldType == typeof(float) || field.FieldType == typeof(int) || field.FieldType == typeof(bool), field.Name);
                var attribute = field.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().Name == "TunableAttribute");
                Assert.IsTrue(attribute != null, field.Name + " needs the Tunable attribute");
                Assert.AreEqual("Doomed Spire face", attribute.GetType().GetProperty("Group").GetValue(attribute) as string);
                Assert.IsFalse(string.IsNullOrEmpty(attribute.GetType().GetProperty("Tip").GetValue(attribute) as string), field.Name + " has a tip");
            }
            Assert.AreEqual(FaceSettings.Default, FaceSettings.FromTuning(), "the defaults are the plain settings");
        }

        // --- the blood knob, and painting on a sheet (piece: faces, second round) ---

        /// <summary>The colour of each pixel of a face, null where transparent.</summary>
        private static (int R, int G, int B)?[] Colours(byte[] lump, DoomPalette palette) =>
            Decode(lump).Select(i => i < 0 ? ((int, int, int)?)null : palette.Colour(i)).ToArray();

        private static (IReadOnlyList<(string Name, byte[] Data)> Faces, DoomPalette Palette, Func<string, PatchSpec> Spec, Wad Wad) Setup(FaceSettings settings)
        {
            var wad = new Wad(WadPath.Doom1Shareware);
            var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
            Func<string, PatchSpec> spec = name => { var p = Patch.FromWad(wad, name); return new PatchSpec(p.Width, p.Height, p.LeftOffset, p.TopOffset); };
            return (FaceFactory.Build(Portrait(), spec, palette, settings), palette, spec, wad);
        }

        /// <summary>How many pixels of a face differ between two builds: with only the blood setting changed, those are the blood marks.</summary>
        private static int Differences(byte[] a, byte[] b, DoomPalette palette) =>
            Colours(a, palette).Zip(Colours(b, palette), (x, y) => x != y).Count(d => d);

        [TestMethod]
        public void TheBloodSettingControlsTheMarksAndZeroMeansNone()
        {
            var (none, palette, _, wad1) = Setup(FaceSettings.Default with { Blood = 0f });
            var (normal, _, _, wad2) = Setup(FaceSettings.Default);
            var (lots, _, _, wad3) = Setup(FaceSettings.Default with { Blood = 2f });
            using (wad1) using (wad2) using (wad3)
            {
                // at full health there are no marks to switch off, so the faces are the same
                Assert.AreEqual(0, Differences(Lump(none, "STFST00"), Lump(normal, "STFST00"), palette), "no blood at full health either way");
                // at every worse level the marks are exactly what differs
                var marks = Enumerable.Range(1, 4).Select(i => Differences(Lump(none, "STFST" + i + "0"), Lump(normal, "STFST" + i + "0"), palette)).ToArray();
                Assert.IsTrue(marks.All(m => m > 0), "every worse level gets some marks: " + string.Join(",", marks));
                Assert.IsTrue(marks[3] > marks[0], "and the worst level has more than the first: " + string.Join(",", marks));
                Assert.IsTrue(marks[3] < 60, "a handful of pixels, not a wash: " + marks[3]);
                var twice = Differences(Lump(none, "STFST40"), Lump(lots, "STFST40"), palette);
                Assert.IsTrue(twice > marks[3], $"twice the setting gives more marks ({marks[3]} then {twice})");
                Assert.IsTrue(Differences(Lump(none, "STFDEAD0"), Lump(normal, "STFDEAD0"), palette) > 5, "the dead face has marks that switch off too");
            }
        }

        [TestMethod]
        public void AFaceSheetReadsBackIntoTheSameFacesAtAnyWholeNumberScale()
        {
            var (faces, palette, spec, wad) = Setup(FaceSettings.Default);
            using (wad)
            {
                foreach (var scale in new[] { 1, 3 })
                {
                    var sheet = FaceFactory.Sheet(faces, palette, scale, transparentBackground: true);
                    Assert.AreEqual(34 * 8 * scale + 8, sheet.Width);
                    var back = FaceSheetImport.Read(sheet, spec, palette);
                    Assert.AreEqual(42, back.Count);
                    foreach (var (name, data) in faces)
                    {
                        var a = Colours(data, palette);
                        var b = Colours(back.First(l => l.Name == name).Data, palette);
                        Assert.IsTrue(a.SequenceEqual(b), $"{name} comes back the same at scale {scale}");
                    }
                }
            }
        }

        [TestMethod]
        public void PaintingOnASheetChangesOnlyThatFace()
        {
            var (faces, palette, spec, wad) = Setup(FaceSettings.Default with { Blood = 0f });
            using (wad)
            {
                var sheet = FaceFactory.Sheet(faces, palette, 1, transparentBackground: true);
                // paint one pixel in the middle of the worst-health straight face (row 4, column 0): a bright blue no face has
                var s = spec("STFST40");
                int px = s.Width / 2, py = s.Height / 2;
                int u = px - s.Left, v = py - s.Top;
                var o = 4 * ((FaceFactory.SheetMargin + 4 * FaceFactory.SheetCellHeight + v) * sheet.Width + FaceFactory.SheetMargin + 0 * FaceFactory.SheetCellWidth + u);
                var painted = (byte[])sheet.Data.Clone();
                painted[o] = 0; painted[o + 1] = 0; painted[o + 2] = 255; painted[o + 3] = 255;

                var back = FaceSheetImport.Read(new Rgba(sheet.Width, sheet.Height, painted), spec, palette);
                var original = Colours(Lump(faces, "STFST40"), palette);
                var edited = Colours(Lump(back, "STFST40"), palette);
                var changed = Enumerable.Range(0, original.Length).Where(i => original[i] != edited[i]).ToArray();
                Assert.AreEqual(1, changed.Length, "exactly one pixel changed");
                Assert.AreEqual(py * s.Width + px, changed[0]);
                Assert.IsTrue(edited[changed[0]] is { } c && c.B > 150 && c.R < 80, "and it is blue");
                foreach (var (name, data) in faces.Where(f => f.Name != "STFST40"))
                {
                    Assert.IsTrue(Colours(data, palette).SequenceEqual(Colours(Lump(back, name), palette)), name + " is untouched");
                }

                // rubbing a pixel out makes it transparent
                var erased = (byte[])sheet.Data.Clone();
                erased[o + 3] = 0;
                var gone = Colours(Lump(FaceSheetImport.Read(new Rgba(sheet.Width, sheet.Height, erased), spec, palette), "STFST40"), palette);
                Assert.IsTrue(gone[py * s.Width + px] == null, "a transparent pixel on the sheet is transparent in the face");
            }
        }

        [TestMethod]
        public void ASheetOfTheWrongSizeIsRefusedWithAClearMessage()
        {
            var (faces, palette, spec, wad) = Setup(FaceSettings.Default);
            using (wad)
            {
                var sheet = FaceFactory.Sheet(faces, palette, 1, transparentBackground: true);
                var cropped = new Rgba(sheet.Width - 5, sheet.Height, new byte[(sheet.Width - 5) * sheet.Height * 4]);
                var message = "";
                try { FaceSheetImport.Read(cropped, spec, palette); } catch (FormatException e) { message = e.Message; }
                Assert.IsTrue(message.Contains("wide") && message.Contains(sheet.Width.ToString()) || message.Contains("must be"), "the message says what size it should be: " + message);
            }
        }

        [TestMethod]
        public void WritesTheOriginalDoomguySheetsToLookAt()
        {
            // The game's own 42 faces, in the same layout as the sheet the mod writes for painting, so the two can be compared. Written to out/, which is git-ignored:
            // they are id Software's art, so they are made when asked for and not kept in the repo.
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
                var original = FaceFactory.Faces.Select(f => (f.Name, wad.ReadLump(f.Name))).ToList();
                Directory.CreateDirectory("out");
                var big = FaceFactory.Sheet(original, palette, 6);
                Png.Save("out/doomguy-faces-preview.png", big.Width, big.Height, big.Data);
                var plain = FaceFactory.Sheet(original, palette, 1, transparentBackground: true);
                Png.Save("out/doomguy-faces-1x.png", plain.Width, plain.Height, plain.Data);
                Assert.AreEqual(34 * 8 + 8, plain.Width);
            }
        }
    }
}
