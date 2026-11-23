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
                Assert.AreEqual(3, frames.Values.Count(f => f.Role == FrameRole.Attack), "its attack is three frames, E F G (not the walk frames it goes back to)");
                Assert.AreEqual(1, frames.Values.Count(f => f.Role == FrameRole.Pain), "and its hurt one frame");
                // so the three attack frames spread over the whole attack animation, and the hurt one sits in the middle of it
                var attack = new[] { 'E', 'F', 'G' }.Select(l => MonsterSprites.Request(frames[l]).Fraction).ToArray();
                Assert.IsTrue(attack[0] > 0.2 && attack[0] < 0.3 && attack[2] > 0.7 && attack[2] < 0.8, "attack poses at about a quarter, a half and three quarters: " + string.Join(",", attack));
                Assert.AreEqual(0.5, MonsterSprites.Request(frames['H']).Fraction, 1e-9);
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
        public void TheRegisteredDoomIsNotTakenForTheUltimateOne()
        {
            // the engine goes by the file name; the registered DOOM and the Ultimate DOOM are both doom.wad, and only the Ultimate one has episode 4
            Func<string, string[], GameMode> modeOf = (fileName, lumps) =>
            {
                var folder = Path.Combine(Path.GetTempPath(), "doomedspire-mode-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, fileName);
                File.WriteAllBytes(path, PwadWriter.Build(lumps.Select(l => (l, new byte[] { 1 })).ToList()));
                using (var wad = new Wad(path)) return wad.GameMode;
            };
            Assert.AreEqual(GameMode.Registered, modeOf("DOOM.WAD", new[] { "E1M1", "E2M1", "E3M1" }), "no episode 4: the registered game");
            Assert.AreEqual(GameMode.Retail, modeOf("DOOM.WAD", new[] { "E1M1", "E4M1" }), "episode 4: the Ultimate DOOM");
            Assert.AreEqual(GameMode.Shareware, modeOf("DOOM1.WAD", new[] { "E1M1" }), "the shareware game is not changed");
            Assert.AreEqual(GameMode.Retail, modeOf("freedoom1.wad", new[] { "E1M1", "E4M1" }), "nor is Freedoom's first game");
        }

        [TestMethod]
        public void ACreatureWithItsOwnNamesForAnimationsStillGetsItsAttackAndHurtPoses()
        {
            // the Knowledge Demon's animations, from the game: attack_light, attack_medium and attack_heavy but no plain "attack" (which cost it two poses)
            var demon = new[] { "idle_loop", "attack_light", "attack_medium", "attack_heavy", "brain_rot", "heal", "burnt_loop", "hurt", "die", "hurt_burnt", "die_burnt" };
            Assert.AreEqual("attack_medium", MonsterSprites.ChooseAnimation(new[] { "attack", "cast" }, demon), "the medium attack");
            Assert.AreEqual("hurt", MonsterSprites.ChooseAnimation(new[] { "hurt", "hit" }, demon));
            Assert.AreEqual("idle_loop", MonsterSprites.ChooseAnimation(new[] { "idle_loop", "idle" }, demon));
            // an exact name is always preferred, and "cast" counts when there is no attack
            Assert.AreEqual("attack", MonsterSprites.ChooseAnimation(new[] { "attack", "cast" }, new[] { "attack_heavy", "attack", "cast" }));
            Assert.AreEqual("cast", MonsterSprites.ChooseAnimation(new[] { "attack", "cast" }, new[] { "idle_loop", "cast", "hurt" }));
            // with no medium one, the shortest similar name; with nothing similar, nothing
            Assert.AreEqual("attack_ram", MonsterSprites.ChooseAnimation(new[] { "attack", "cast" }, new[] { "attack_flame", "attack_ram" }));
            Assert.IsTrue(MonsterSprites.ChooseAnimation(new[] { "attack", "cast" }, new[] { "idle_loop", "hurt", "die" }) == null);
        }

        [TestMethod]
        public void MeasuringAPoseSaysHowFarItIsLiftedAndOffCentre()
        {
            var stand = Blob(60, 100, 200);                      // 120 wide, 200 tall, standing on the point
            var same = MonsterSpriteBuilder.Measure(stand, stand).Value;
            Assert.AreEqual((60, 100, 0, 0), same, "the same pose against itself: 60% wide, 100% tall, not lifted, not off centre");
            var lifted = new CapturedFrame(stand.Image, stand.OriginX, stand.OriginY + 40); // the standing point 40 pixels lower: the pose floats 40 above it
            // the oval is 201 pixels tall (its bottom row counts), so 40 pixels is 19.9%, which rounds to 20, less a pixel of the oval's own edge: 19 or 20
            Assert.IsTrue(Math.Abs(MonsterSpriteBuilder.Measure(lifted, stand).Value.Lift - 20) <= 1, "40 of 200 pixels is about 20%, and above the point is positive");
            var sunk = new CapturedFrame(stand.Image, stand.OriginX, stand.OriginY - 40);
            Assert.IsTrue(Math.Abs(MonsterSpriteBuilder.Measure(sunk, stand).Value.Lift + 20) <= 1, "below it is negative");
            Assert.IsTrue(MonsterSpriteBuilder.Measure(new CapturedFrame(new Rgba(10, 10, new byte[400]), 5, 5), stand) == null, "an empty picture has no measure");
        }

        [TestMethod]
        public void APoseMuchTallerOrOffToTheSideIsAJump()
        {
            var stand = Blob(60, 100, 200);
            Assert.IsFalse(MonsterSprites.IsJumpy(Blob(60, 100, 200), stand));
            Assert.IsFalse(MonsterSprites.IsJumpy(Blob(70, 115, 200), stand), "15% taller is an ordinary pose");
            Assert.IsTrue(MonsterSprites.IsJumpy(Blob(40, 150, 200), stand), "the clam's rear-up was 151% tall");
            Assert.IsTrue(MonsterSprites.IsJumpy(new CapturedFrame(stand.Image, stand.OriginX + 100, stand.OriginY), stand), "far to the side");
            Assert.IsTrue(MonsterSprites.IsJumpy(new CapturedFrame(stand.Image, stand.OriginX, stand.OriginY + 80), stand), "in the air");
        }

        [TestMethod]
        public void TheGameHasFourGibAnimationsToDieInto()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var sets = MonsterSprites.GibAnimations(wad);
                Assert.AreEqual(4, sets.Count, "the player's, the imp's, the zombieman's and the shotgun guy's");
                Assert.IsTrue(sets.All(s => s.Count >= 5), "each is a number of frames: " + string.Join(",", sets.Select(s => s.Count)));
                // the creature itself is not in them: the player's own gib animation has 9 frames, the first two (him with his gun, then his body in pieces) are dropped
                Assert.AreEqual(9 - MonsterSprites.FramesBeforeTheBits, sets[0].Count);
                using (var all = new Wad(WadPath.Doom1Shareware))
                {
                    Assert.IsFalse(sets[0].Any(p => p.SequenceEqual(all.ReadLump("PLAYO0"))), "not the first frame of the player's");
                }
                Assert.IsTrue(sets.All(s => s.All(p => Patch.FromData("gib", p).Width > 5)), "each frame is a picture");
            }
        }

        [TestMethod]
        public void ADyingFrameIsEightVariationsOfGibsAndTheLastOneIsTheHeap()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var sets = MonsterSprites.GibAnimations(wad);
                var letters = MonsterSprites.Letters(wad, "TROO");
                var dying = MonsterSprites.Frames(MobjType.Troop, letters).Where(f => f.Role == FrameRole.Death || f.Role == FrameRole.Gib).ToList();
                var lumps = MonsterSpriteBuilder.DeathLumps("TROO", dying, sets);
                Assert.AreEqual(dying.Count * 8, lumps.Count);
                var path = WriteAddOn(lumps);
                using (var withAddOn = new Wad(WadPath.Doom1Shareware, path))
                {
                    var imp = new SpriteLookup(withAddOn)[Sprite.TROO];
                    foreach (var frame in dying)
                    {
                        var f = imp.Frames[frame.Letter - 'A'];
                        Assert.IsTrue(f.Rotate, "frame " + frame.Letter + " has variations");
                        Assert.AreEqual(8, f.Patches.Length);
                    }
                    // the last dying frame (what stays on the floor) is the last picture of each gib animation, in turn
                    var last = dying.Where(f => f.Role == FrameRole.Death).OrderBy(f => f.Index).Last();
                    var heaps = imp.Frames[last.Letter - 'A'].Patches.Select(p => (p.Width, p.Height)).ToList();
                    var expected = Enumerable.Range(0, 8).Select(i => { var p = Patch.FromData("g", sets[i % sets.Count].Last()); return (p.Width, p.Height); }).ToList();
                    Assert.IsTrue(heaps.SequenceEqual(expected), "the heaps");
                    Assert.IsTrue(heaps.Distinct().Count() >= 3, "and they differ from each other");
                    // the living frames and other monsters are not touched
                }
            }
        }

        [TestMethod]
        public void ACorpseKeepsItsPictureAndDifferentCorpsesDiffer()
        {
            var picks = new HashSet<int>();
            for (uint i = 0; i < 200; i++)
            {
                var pick = CorpseVariant.Mix(i * 64 << 16, (i * 37 + 11) << 16); // map positions are whole numbers of units in 16.16 fixed point
                Assert.IsTrue(pick >= 0 && pick < 8);
                Assert.AreEqual(pick, CorpseVariant.Mix(i * 64 << 16, (i * 37 + 11) << 16), "the same place gives the same answer");
                picks.Add(pick);
            }
            Assert.AreEqual(8, picks.Count, "all eight variations come up");
        }

        [TestMethod]
        public void AGameStartsAndRunsWithTheGibVariations()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var sets = MonsterSprites.GibAnimations(wad);
                var all = new List<(string Name, byte[] Data)>();
                foreach (var type in new[] { MobjType.Possessed, MobjType.Shotguy, MobjType.Troop, MobjType.Sergeant, MobjType.Bruiser })
                {
                    var sprite = MonsterSprites.SpriteName(type);
                    var dying = MonsterSprites.Frames(type, MonsterSprites.Letters(wad, sprite)).Where(f => f.Role == FrameRole.Death || f.Role == FrameRole.Gib).ToList();
                    all.AddRange(MonsterSpriteBuilder.DeathLumps(sprite, dying, sets));
                }
                var path = WriteAddOn(all);
                using (var session = DoomSession.Create(WadPath.Doom1Shareware, null, new[] { path }))
                {
                    var rgba = new byte[session.Width * session.Height * 4];
                    for (var i = 0; i < 200; i++) session.Tick();
                    session.RenderFirstFrame(rgba);
                    Assert.IsTrue(rgba.Any(b => b != 0));
                }
            }
        }

        [TestMethod]
        public void DeadAndMangledBodiesAreNotReplaced()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                foreach (var type in new[] { MobjType.Possessed, MobjType.Shotguy, MobjType.Troop, MobjType.Sergeant, MobjType.Bruiser })
                {
                    var letters = MonsterSprites.Letters(wad, MonsterSprites.SpriteName(type));
                    var all = MonsterSprites.Frames(type, letters);
                    var kept = MonsterSprites.ReplacedFrames(type, letters);
                    Assert.IsTrue(kept.Count > 0 && kept.Count < all.Count, type + ": some frames are kept as DOOM's");
                    Assert.IsFalse(kept.Any(f => f.Role == FrameRole.Death || f.Role == FrameRole.Gib), type + ": no dying or gibbed frame is replaced");
                    Assert.IsTrue(kept.Any(f => f.Role == FrameRole.Walk) && kept.Any(f => f.Role == FrameRole.Attack) && kept.Any(f => f.Role == FrameRole.Pain), type + ": the living poses are");
                    Assert.AreEqual(all.Count(f => f.Role != FrameRole.Death && f.Role != FrameRole.Gib), kept.Count);
                }
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
                var first = Patch.FromData("TROOA1", lumps.First(l => l.Name == "TROOA1").Data);
                Assert.AreEqual(original, first.Height, 1, "the first frame is as tall as the imp's own");
                Assert.IsTrue(first.Width > 10 && first.Width < original, "and narrower than it is tall (the blob is 60 x 100)");
                Assert.AreEqual(first.Width / 2.0, first.LeftOffset, 2, "centred on the standing point");
                Assert.AreEqual(first.Height, first.TopOffset, 2, "the bottom of the creature is on the floor");
                // the same scale for every frame: a later frame's blob is a little smaller (height 100 - letter), so its picture is shorter, not stretched back up
                var later = Patch.FromData("TROOK1", lumps.First(l => l.Name == "TROOK1").Data);
                Assert.IsTrue(later.Height < first.Height, "later frames keep the same scale");
                Assert.AreEqual(MonsterSprites.Letters(wad, "TROO").Count * 5, lumps.Count, "five lumps a frame: front, back and the three sides (each side also mirrored)");
                Assert.IsTrue(lumps.All(l => l.Name.StartsWith("TROO")));
                Assert.IsTrue(lumps.Select(l => l.Name).Where(n => n.StartsWith("TROOA")).SequenceEqual(new[] { "TROOA1", "TROOA2A8", "TROOA3A7", "TROOA4A6", "TROOA5" }));
            }
        }

        [TestMethod]
        public void AFrameWithNoPictureGetsTheFirstOnesAndNothingIsBuiltWithoutAny()
        {
            using (var wad = new Wad(WadPath.Doom1Shareware))
            {
                var lumps = BuildImp(wad, letter => letter == 'A' ? Blob(60, 100, 200) : null);
                Assert.AreEqual(MonsterSprites.Letters(wad, "TROO").Count * 5, lumps.Count);
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
                        var frame = imp.Frames[f];
                        var widths = frame.Patches.Select(p => p.Width).Distinct().ToList();
                        Assert.AreEqual(1, widths.Count, "all eight sides of frame " + f + " are the one picture");
                        // the creature faces left: sides 2, 3, 4 (indices 1 to 3) as it is, sides 6, 7, 8 (indices 5 to 7) mirrored, front and back plain
                        var wanted = new[] { false, false, false, false, false, true, true, true };
                        Assert.IsTrue(frame.Flip.SequenceEqual(wanted), "frame " + f + " mirrors the sides where the monster faces right");
                        Assert.IsTrue(frame.Rotate, "frame " + f + " is chosen by the side it is seen from");
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
