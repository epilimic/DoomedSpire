// Our own tests (not from upstream): replacing a monster's sprite pictures, src/Host/MonsterSprites.cs, on the real shareware game data.
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
    public class MonsterSpriteTests
    {
        /// <summary>A made-up creature picture: a filled oval of the given size standing on a point near the bottom of a transparent square (the real ones are captured from the game).</summary>
        private static CapturedFrame Blob(int halfWidth, int halfHeight, byte red)
        {
            const int size = 300, feetX = 150, feetY = 280;
            var data = new byte[size * size * 4];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    double dx = (x - feetX) / (double)halfWidth, dy = (y - (feetY - halfHeight)) / (double)halfHeight;
                    if (dx * dx + dy * dy > 1) continue;
                    var o = 4 * (y * size + x);
                    data[o] = red; data[o + 1] = 90; data[o + 2] = 60; data[o + 3] = 255;
                }
            }
            return new CapturedFrame(new Rgba(size, size, data), feetX, feetY);
        }

        [TestMethod]
        public void TheImpsFramesAreSortedIntoWalkingAttackingHurtAndDying()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                Assert.AreEqual("TROO", MonsterSprites.SpriteName(MobjType.Troop));
                var letters = MonsterSprites.Letters(wad, "TROO");
                Assert.AreEqual('A', letters[0]);
                Assert.IsTrue(letters.Count >= 20, "the imp has about 22 frames: " + letters.Count);
                var frames = MonsterSprites.Frames(MobjType.Troop, letters).ToDictionary(f => f.Letter);
                Assert.AreEqual(FrameRole.Walk, frames['A'].Role, "the first frame is the walk (and the stand)");
                Assert.AreEqual(FrameRole.Walk, frames['D'].Role);
                Assert.IsTrue(frames.Values.Count(f => f.Role == FrameRole.Attack) >= 2, "it has attack frames");
                Assert.IsTrue(frames.Values.Any(f => f.Role == FrameRole.Pain), "a hurt frame");
                var death = frames.Values.Where(f => f.Role == FrameRole.Death).OrderBy(f => f.Index).ToList();
                Assert.IsTrue(death.Count >= 4, "several dying frames: " + death.Count);
                Assert.IsTrue(death.Select(f => f.Letter).SequenceEqual(death.Select(f => f.Letter).OrderBy(l => l)), "in order");
                Assert.IsTrue(frames.Values.Count(f => f.Role == FrameRole.Gib) >= 4, "and gibbed frames");
                Assert.AreEqual(letters.Count, frames.Count);
                // a pose is asked for each frame, and dying ends on the last one
                Assert.AreEqual(1.0, MonsterSprites.Request(death.Last()).Fraction, 1e-9);
                Assert.IsTrue(MonsterSprites.Request(frames['A']).Animations.Contains("idle_loop"));
                Assert.IsTrue(MonsterSprites.OriginalHeight(wad, "TROO") > 40, "the imp is about 56 tall");
            }
        }

        [TestMethod]
        public void EveryMonsterOfTheSharewareGameHasFramesToReplace()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                foreach (var type in new[] { MobjType.Possessed, MobjType.Shotguy, MobjType.Troop, MobjType.Sergeant, MobjType.Shadows, MobjType.Bruiser })
                {
                    var sprite = MonsterSprites.SpriteName(type);
                    var letters = MonsterSprites.Letters(wad, sprite);
                    var frames = MonsterSprites.Frames(type, letters);
                    Assert.IsTrue(letters.Count > 0, sprite + " has pictures");
                    Assert.IsTrue(frames.Any(f => f.Role == FrameRole.Walk), sprite + " walks");
                    Assert.IsTrue(frames.Any(f => f.Role == FrameRole.Death), sprite + " dies");
                    Assert.IsTrue(MonsterSprites.OriginalHeight(wad, sprite) > 0, sprite + " has a height");
                }
            }
        }

        [TestMethod]
        public void OnlyTheOriginalDoom1IsReplaced()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                Assert.IsTrue(MonsterSprites.IsOriginalDoom1(wad));
            }
            // Freedoom's first game has a second episode like the full DOOM, so only its own lumps tell it apart; its second game is a DOOM II replacement
            foreach (var other in new[] { WadPath.Doom1, WadPath.Doom2 })
            {
                if (!File.Exists(other)) continue; // Freedoom not installed: nothing to check
                using (var wad = new Wad(other))
                {
                    Assert.IsFalse(MonsterSprites.IsOriginalDoom1(wad), other);
                }
            }
        }

        [TestMethod]
        public void TheFourMonstersOfTheFullGameAreSortedToo()
        {
            // The full DOOM is not in the repo; Freedoom has pictures with the same names, which is enough to check the frame sorting.
            if (!File.Exists(WadPath.Doom1)) return;
            using (var wad = new Wad(WadPath.Doom1))
            {
                foreach (var type in new[] { MobjType.Skull, MobjType.Head, MobjType.Cyborg, MobjType.Spider })
                {
                    var sprite = MonsterSprites.SpriteName(type);
                    var frames = MonsterSprites.Frames(type, MonsterSprites.Letters(wad, sprite));
                    Assert.IsTrue(frames.Any(f => f.Role == FrameRole.Walk), sprite + " walks");
                    Assert.IsTrue(frames.Any(f => f.Role == FrameRole.Attack), sprite + " attacks");
                    Assert.IsTrue(frames.Any(f => f.Role == FrameRole.Pain), sprite + " is hurt");
                    Assert.IsTrue(frames.Count(f => f.Role == FrameRole.Death) >= 5, sprite + " dies");
                    Assert.IsTrue(MonsterSprites.OriginalHeight(wad, sprite) > 30, sprite + " has a height");
                }
            }
        }

        private static string WriteAddOn(IReadOnlyList<(string Name, byte[] Data)> lumps)
        {
            var path = Path.Combine(Path.GetTempPath(), "doomedspire-enemies-" + Guid.NewGuid().ToString("N") + ".wad");
            File.WriteAllBytes(path, PwadWriter.Build(MonsterSpriteBuilder.WithMarkers(lumps)));
            return path;
        }

        private static IReadOnlyList<(string Name, byte[] Data)> BuildImp(Wad wad, Func<char, CapturedFrame?> picture)
        {
            var palette = new DoomPalette(wad.ReadLump("PLAYPAL"));
            var letters = MonsterSprites.Letters(wad, "TROO");
            var frames = new Dictionary<char, CapturedFrame>();
            foreach (var letter in letters)
            {
                var p = picture(letter);
                if (p != null) frames[letter] = p;
            }
            return MonsterSpriteBuilder.Build("TROO", letters, frames, 'A', MonsterSprites.OriginalHeight(wad, "TROO"), palette);
        }

        [TestMethod]
        public void TheBuiltPicturesAreScaledToTheMonstersSizeAndStandOnTheFloor()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var lumps = BuildImp(wad, letter => letter == 'A' ? Blob(60, 100, 200) : Blob(60 + letter, 100 - letter, 200));
                var original = MonsterSprites.OriginalHeight(wad, "TROO");
                var first = Patch.FromData("TROOA0", lumps.First(l => l.Name == "TROOA0").Data);
                Assert.AreEqual(original, first.Height, 1, "the first frame is as tall as the imp's own");
                Assert.IsTrue(first.Width > 10 && first.Width < original, "and narrower than it is tall (the blob is 60 x 100)");
                Assert.AreEqual(first.Width / 2.0, first.LeftOffset, 2, "centred on the standing point");
                Assert.AreEqual(first.Height, first.TopOffset, 2, "the bottom of the creature is on the floor");
                // the same scale for every frame: a later frame's blob is a little smaller (height 100 - letter), so its picture is shorter, not stretched back up
                var later = Patch.FromData("TROOK0", lumps.First(l => l.Name == "TROOK0").Data);
                Assert.IsTrue(later.Height < first.Height, "later frames keep the same scale");
                Assert.AreEqual(MonsterSprites.Letters(wad, "TROO").Count, lumps.Count);
                Assert.IsTrue(lumps.All(l => l.Name.EndsWith("0") && l.Name.StartsWith("TROO")));
            }
        }

        [TestMethod]
        public void AFrameWithNoPictureGetsTheFirstOnesAndNothingIsBuiltWithoutAny()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var lumps = BuildImp(wad, letter => letter == 'A' ? Blob(60, 100, 200) : null);
                Assert.AreEqual(MonsterSprites.Letters(wad, "TROO").Count, lumps.Count);
                Assert.IsTrue(lumps.All(l => l.Data.SequenceEqual(lumps[0].Data)), "all the same picture");
                Assert.AreEqual(0, BuildImp(wad, _ => null).Count, "no pictures, no lumps");
            }
        }

        [TestMethod]
        public void TheEngineShowsTheReplacementPicturesFromEverySideAndLeavesOtherMonstersAlone()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var path = WriteAddOn(BuildImp(wad, letter => Blob(40 + 3 * (letter - 'A'), 100, 200)));
                using (var withAddOn = new Wad(WadPath.Doom1Shareware, path))
                {
                    var plain = new SpriteLookup(wad);
                    var changed = new SpriteLookup(withAddOn);
                    var imp = changed[Sprite.TROO];
                    var originalImp = plain[Sprite.TROO];
                    Assert.AreEqual(originalImp.Frames.Length, imp.Frames.Length, "the same frames");
                    for (var f = 0; f < imp.Frames.Length; f++)
                    {
                        Assert.IsFalse(imp.Frames[f].Rotate, "frame " + f + " is the same picture from every side");
                        var widths = imp.Frames[f].Patches.Select(p => p.Width).Distinct().ToList();
                        Assert.AreEqual(1, widths.Count, "all eight sides of frame " + f);
                        Assert.IsTrue(imp.Frames[f].Patches.All(p => p != null && !imp.Frames[f].Flip.Any(x => x)), "not mirrored");
                    }
                    Assert.IsTrue((originalImp.Frames[0].Patches[0].Width, originalImp.Frames[0].Patches[0].Height) != (imp.Frames[0].Patches[0].Width, imp.Frames[0].Patches[0].Height), "frame A really changed");
                    // the other monsters keep the game's own pictures
                    var zombie = plain[Sprite.POSS].Frames[0].Patches[0];
                    var zombieAfter = changed[Sprite.POSS].Frames[0].Patches[0];
                    Assert.AreEqual((zombie.Width, zombie.Height), (zombieAfter.Width, zombieAfter.Height));
                }
            }
        }

        [TestMethod]
        public void AGameStartsAndRunsWithTheReplacementMonster()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var path = WriteAddOn(BuildImp(wad, letter => Blob(40, 100, 200)));
                using (var session = DoomSession.Create(WadPath.Doom1Shareware, null, new[] { path }))
                {
                    var rgba = new byte[session.Width * session.Height * 4];
                    for (var i = 0; i < 200; i++) session.Tick();
                    session.RenderFirstFrame(rgba);
                    Assert.IsTrue(rgba.Any(b => b != 0), "something is drawn");
                }
            }
        }
    }
}
