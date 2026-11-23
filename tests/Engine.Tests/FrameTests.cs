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
            using (var session = DoomSession.Create(WadPath.Doom1Shareware))
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
            using (var session = DoomSession.Create(WadPath.Doom1Shareware))
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
            var session = DoomSession.Create(WadPath.Doom1Shareware);
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
        public void ReleaseAllInputLetsGoOfEverything()
        {
            using (var session = Started())
            {
                session.PostKey(DoomKey.Up, true);
                session.PostKey(DoomKey.LControl, true);
                Tics(session, 5);
                session.ReleaseAllInput();
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

        // --- mouse (piece 5, revised) ---

        private static TicCmd Build(PlayerInput input)
        {
            var cmd = new TicCmd();
            input.BuildTicCmd(cmd);
            return cmd;
        }

        [TestMethod]
        public void MouseMovesOnlyTurnThePlayer()
        {
            var input = new PlayerInput(new Config());
            input.AddMouseTurn(30);
            var cmd = Build(input);
            Assert.IsTrue(cmd.AngleTurn < 0, "moving the mouse right should turn the player right (a negative angle in DOOM)");
            Assert.AreEqual(0, (int)cmd.ForwardMove);
            Assert.AreEqual(0, (int)cmd.SideMove);

            // the movement is used up by one tic
            Assert.AreEqual(0, (int)Build(input).AngleTurn);

            input.AddMouseTurn(-30);
            Assert.IsTrue(Build(input).AngleTurn > 0, "moving the mouse left should turn the player left");
        }

        [TestMethod]
        public void MouseNeverWalksEvenWithTheStrafeKeyHeld()
        {
            var input = new PlayerInput(new Config());
            input.SetKey(DoomKey.LAlt, true); // the original frontend strafed with the mouse while this was held
            input.AddMouseTurn(50);
            var cmd = Build(input);
            Assert.AreEqual(0, (int)cmd.SideMove);
            Assert.AreEqual(0, (int)cmd.ForwardMove);
            Assert.IsTrue(cmd.AngleTurn < 0, "the mouse still turns");
        }

        [TestMethod]
        public void WasdWalksAndStrafesAndTheMouseAddsOnlyTurning()
        {
            var input = new PlayerInput(new Config());
            input.SetKey(DoomKey.W, true);
            input.SetKey(DoomKey.D, true);
            input.AddMouseTurn(10);
            var cmd = Build(input);
            Assert.IsTrue(cmd.ForwardMove > 0, "W is forward");
            Assert.IsTrue(cmd.SideMove > 0, "D is strafe right");
            Assert.IsTrue(cmd.AngleTurn < 0);

            input.ReleaseAll();
            input.SetKey(DoomKey.S, true);
            input.SetKey(DoomKey.A, true);
            cmd = Build(input);
            Assert.IsTrue(cmd.ForwardMove < 0, "S is back");
            Assert.IsTrue(cmd.SideMove < 0, "A is strafe left");
        }

        [TestMethod]
        public void LeftClickFiresRightClickUsesAndSpaceAlsoUses()
        {
            var input = new PlayerInput(new Config());
            input.SetMouseButton(DoomMouseButton.Mouse1, true);
            Assert.IsTrue((Build(input).Buttons & TicCmdButtons.Attack) != 0, "left click fires");
            Assert.IsTrue((Build(input).Buttons & TicCmdButtons.Use) == 0);
            input.SetMouseButton(DoomMouseButton.Mouse1, false);

            input.SetMouseButton(DoomMouseButton.Mouse2, true);
            Assert.IsTrue((Build(input).Buttons & TicCmdButtons.Use) != 0, "right click uses");
            Assert.IsTrue((Build(input).Buttons & TicCmdButtons.Attack) == 0);
            input.SetMouseButton(DoomMouseButton.Mouse2, false);

            input.SetKey(DoomKey.Space, true);
            Assert.IsTrue((Build(input).Buttons & TicCmdButtons.Use) != 0, "Space still uses");
        }

        [TestMethod]
        public void ReleaseAllLetsGoOfKeysButtonsAndPendingMouseMovement()
        {
            var input = new PlayerInput(new Config());
            input.SetKey(DoomKey.W, true);
            input.SetMouseButton(DoomMouseButton.Mouse1, true);
            input.AddMouseTurn(40);
            input.ReleaseAll();
            var cmd = Build(input);
            Assert.AreEqual(0, (int)cmd.ForwardMove);
            Assert.AreEqual(0, (int)cmd.AngleTurn);
            Assert.AreEqual((byte)0, cmd.Buttons);
            Assert.IsFalse(input.IsAnythingDown());
        }

        [TestMethod]
        public void MouseTurnsThePlayerInTheGameWithoutWalking()
        {
            using (var session = Started())
            {
                var start = session.PlayerState;
                session.AddMouseTurn(40);
                Tics(session, 1);
                var turned = session.PlayerState;
                Assert.IsTrue(turned.Angle != start.Angle, "the player should have turned");
                Assert.AreEqual((start.X, start.Y), (turned.X, turned.Y));
            }
        }

        [TestMethod]
        public void LeftClickShootsInTheGame()
        {
            using (var session = Started())
            {
                var before = session.Ammo;
                session.PostMouseButton(DoomMouseButton.Mouse1, true);
                Tics(session, 40); // the pistol takes about 17 tics to land its first shot, then fires about every 14
                session.PostMouseButton(DoomMouseButton.Mouse1, false);
                Assert.IsTrue(session.Ammo <= before - 2, $"holding left click for 40 tics should fire the pistol at least twice (had {before}, has {session.Ammo})");
            }
        }

        [TestMethod]
        public void TheGameStartsWithTheLoweredMouseSensitivity()
        {
            using (var session = Started())
            {
                var start = session.PlayerState;
                session.AddMouseTurn(40);
                Tics(session, 1);
                var turned = session.PlayerState;
                // the engine's formula: round(0.5 * sensitivity * pixels) steps of 8 angle units (a short), which the player's 32-bit angle holds shifted up by 16
                var steps = (int)Math.Round(0.5 * DoomSession.MouseSensitivity * 40, MidpointRounding.ToEven);
                Assert.AreEqual((uint)(steps * 8) << 16, unchecked(start.Angle - turned.Angle));
                Assert.IsTrue(DoomSession.MouseSensitivity < 8, "lower than the engine's default of 8");
            }
        }
    }
}
