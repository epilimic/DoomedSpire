// Our own tests (not from upstream): the engine glue in src/Host, run on Freedoom with no window, no sound and no Godot.
using System;
using System.Collections.Generic;
using System.Linq;
using DoomedSpire.Host;
using ManagedDoom;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class FrameTests
    {
        [TestMethod]
        public void FirstFrameOfEpisode1Map1IsARealPicture()
        {
            using (var session = DoomSession.Create(WadPath.Doom1))
            {
                var rgba = new byte[session.Width * session.Height * 4];
                session.RenderFirstFrame(rgba);
                Png.Save("out/first-frame.png", session.Width, session.Height, rgba); // look at it: tests/Engine.Tests/out/first-frame.png

                Assert.AreEqual(640, session.Width);
                Assert.AreEqual(400, session.Height);

                var counts = new Dictionary<uint, int>();
                var opaque = true;
                for (var i = 0; i < rgba.Length; i += 4)
                {
                    var color = (uint)(rgba[i] | (rgba[i + 1] << 8) | (rgba[i + 2] << 16));
                    counts[color] = counts.TryGetValue(color, out var n) ? n + 1 : 1;
                    if (rgba[i + 3] != 255) opaque = false;
                }
                Assert.IsTrue(opaque, "every pixel must be fully opaque");
                Assert.IsTrue(counts.Count >= 30, $"a level view uses many colours, this one only {counts.Count}");
                Assert.IsTrue(counts.Values.Max() < session.Width * session.Height / 2, "more than half the picture is one colour: it looks blank");
            }
        }

        [TestMethod]
        public void TicksAdvanceTheGame()
        {
            using (var session = DoomSession.Create(WadPath.Doom1))
            {
                var a = new byte[session.Width * session.Height * 4];
                var b = new byte[a.Length];
                session.RenderFirstFrame(a);
                for (var i = 0; i < 70; i++) session.Tick();
                session.RenderTo(b);
                Assert.IsTrue(!a.SequenceEqual(b), "two seconds later the picture should have changed (animated textures, the status bar face, ...)");
            }
        }

        // --- keyboard input (piece 5) ---

        private static DoomSession Started()
        {
            var session = DoomSession.Create(WadPath.Doom1);
            session.RenderFirstFrame(new byte[session.Width * session.Height * 4]);
            return session;
        }

        private static void Tics(DoomSession session, int count)
        {
            for (var i = 0; i < count; i++) session.Tick();
        }

        private static long Distance((int X, int Y, uint Angle) a, (int X, int Y, uint Angle) b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y;
            return (long)Math.Sqrt(dx * dx + dy * dy) >> 16; // map units
        }

        [TestMethod]
        public void StandingStillWithoutKeysStaysPut()
        {
            using (var session = Started())
            {
                var before = session.PlayerState;
                Tics(session, 70);
                Assert.AreEqual(before, session.PlayerState);
            }
        }

        [TestMethod]
        public void HeldForwardMovesThePlayerAndReleasingStopsIt()
        {
            using (var session = Started())
            {
                var start = session.PlayerState;
                session.PostKey(DoomKey.Up, true);
                Tics(session, 35);
                var moving = session.PlayerState;
                Assert.IsTrue(Distance(start, moving) >= 20, $"the player should have walked at least 20 map units in a second, walked {Distance(start, moving)}");

                session.PostKey(DoomKey.Up, false);
                Tics(session, 100); // friction slows the player gradually: about 60 tics from a run
                var stopped = session.PlayerState;
                Tics(session, 35);
                Assert.AreEqual(stopped, session.PlayerState); // friction has stopped it, and nothing keeps pushing
            }
        }

        [TestMethod]
        public void WAndTheArrowAreBothForward()
        {
            long[] walked = new long[2];
            var keys = new[] { DoomKey.Up, DoomKey.W };
            for (var i = 0; i < 2; i++)
            {
                using (var session = Started())
                {
                    var start = session.PlayerState;
                    session.PostKey(keys[i], true);
                    Tics(session, 35);
                    walked[i] = Distance(start, session.PlayerState);
                }
            }
            Assert.AreEqual(walked[0], walked[1]);
            Assert.IsTrue(walked[0] > 0);
        }

        [TestMethod]
        public void TurnKeysTurnThePlayer()
        {
            using (var session = Started())
            {
                var start = session.PlayerState;
                session.PostKey(DoomKey.Left, true);
                Tics(session, 20);
                var turned = session.PlayerState;
                Assert.IsTrue(turned.Angle != start.Angle, "holding Left should turn the player");
                Assert.AreEqual((start.X, start.Y), (turned.X, turned.Y)); // turning does not walk
            }
        }

        [TestMethod]
        public void ReleaseAllKeysLetsGoOfEverything()
        {
            using (var session = Started())
            {
                session.PostKey(DoomKey.Up, true);
                session.PostKey(DoomKey.LControl, true);
                Tics(session, 5);
                session.ReleaseAllKeys();
                Tics(session, 100);
                var stopped = session.PlayerState;
                Tics(session, 35);
                Assert.AreEqual(stopped, session.PlayerState);
            }
        }

        [TestMethod]
        public void EscapeOpensDoomsMenu()
        {
            using (var session = Started())
            {
                Assert.IsFalse(session.MenuActive);
                session.PostKey(DoomKey.Escape, true);
                Tics(session, 2);
                session.PostKey(DoomKey.Escape, false);
                Tics(session, 2);
                Assert.IsTrue(session.MenuActive, "Escape should open DOOM's menu");

                // and it is drawn: the picture differs from the same moment without the menu
                var withMenu = new byte[session.Width * session.Height * 4];
                session.RenderTo(withMenu);
                using (var plain = Started())
                {
                    Tics(plain, 4);
                    var without = new byte[withMenu.Length];
                    plain.RenderTo(without);
                    Assert.IsTrue(!withMenu.SequenceEqual(without), "the menu should be visible in the picture");
                }
            }
        }

        [TestMethod]
        public void QuittingFromTheMenuCompletesTheSession()
        {
            using (var session = Started())
            {
                // F10 is DOOM's quit shortcut; it asks "are you sure", Y confirms.
                session.PostKey(DoomKey.F10, true);
                session.PostKey(DoomKey.F10, false);
                Tics(session, 3);
                session.PostKey(DoomKey.Y, true);
                session.PostKey(DoomKey.Y, false);
                var completed = false;
                for (var i = 0; i < 200 && !completed; i++) completed = session.Tick() == UpdateResult.Completed;
                Assert.IsTrue(completed, "confirming quit should make Tick report Completed, so the popup can close");
            }
        }
    }
}
