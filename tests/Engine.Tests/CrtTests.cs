// Our own tests (not from upstream): where the monitor picture and the game go on screen (src/Host/CrtLayout.cs).
using System;
using System.Linq;
using System.Reflection;
using DoomedSpire.Host;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class CrtTests
    {
        private static (Box Monitor, Box Game) Default(float w = 1920, float h = 1080) => CrtLayout.Current(w, h);

        [TestMethod]
        public void TheMonitorFitsTheCanvasAndTheGameSitsOnItsGlass()
        {
            var (monitor, game) = Default();
            Assert.IsTrue(monitor.X >= 0 && monitor.Y >= 0 && monitor.X + monitor.Width <= 1920 && monitor.Y + monitor.Height <= 1080, "the whole monitor is on screen");
            Assert.IsTrue(game.X > monitor.X && game.Y > monitor.Y && game.X + game.Width < monitor.X + monitor.Width && game.Y + game.Height < monitor.Y + monitor.Height, "the game is inside the monitor");
            // centred: the monitor is in the middle of the canvas, and the glass in the middle of the monitor (it is 78 pixels from both sides of the picture)
            Assert.AreEqual(960f, monitor.X + monitor.Width / 2, 0.01f);
            Assert.AreEqual(540f, monitor.Y + monitor.Height / 2, 0.01f);
            Assert.AreEqual(monitor.X + monitor.Width / 2, game.X + game.Width / 2, 0.01f);
            // the game is as big as the glass: 692 x 535 picture pixels at the scale
            Assert.AreEqual(CrtPicture.GlassWidth * CrtPlacement.Scale, game.Width, 0.01f);
            Assert.AreEqual(CrtPicture.GlassHeight * CrtPlacement.Scale, game.Height, 0.01f);
        }

        [TestMethod]
        public void ScalingTheMonitorScalesTheGameWithIt()
        {
            var (monitor1, game1) = CrtLayout.Compute(1920, 1080, 1f, 0, 0, 78, 78, 692, 535);
            var (monitor2, game2) = CrtLayout.Compute(1920, 1080, 2f, 0, 0, 78, 78, 692, 535);
            Assert.AreEqual(monitor1.Width * 2, monitor2.Width, 0.01f);
            Assert.AreEqual(game1.Width * 2, game2.Width, 0.01f);
            // the game stays at the same place on the picture: the same fraction across it
            Assert.AreEqual((game1.X - monitor1.X) / monitor1.Width, (game2.X - monitor2.X) / monitor2.Width, 0.0001f);
            Assert.AreEqual((game1.Y - monitor1.Y) / monitor1.Height, (game2.Y - monitor2.Y) / monitor2.Height, 0.0001f);
        }

        [TestMethod]
        public void TheOffsetsMoveTheMonitorAndTheGameTogether()
        {
            var (m0, g0) = CrtLayout.Compute(1920, 1080, 1.44f, 0, 0, 78, 78, 692, 535);
            var (m1, g1) = CrtLayout.Compute(1920, 1080, 1.44f, 30, -20, 78, 78, 692, 535);
            Assert.AreEqual(m0.X + 30, m1.X, 0.01f);
            Assert.AreEqual(m0.Y - 20, m1.Y, 0.01f);
            Assert.AreEqual(g0.X + 30, g1.X, 0.01f);
            Assert.AreEqual(g0.Y - 20, g1.Y, 0.01f);
            Assert.AreEqual(m0.Width, m1.Width);
        }

        [TestMethod]
        public void MovingTheScreenRectangleMovesOnlyTheGame()
        {
            var (m0, g0) = CrtLayout.Compute(1920, 1080, 1.44f, 0, 0, 78, 78, 692, 535);
            var (m1, g1) = CrtLayout.Compute(1920, 1080, 1.44f, 0, 0, 88, 70, 700, 540);
            Assert.AreEqual(m0, m1);
            Assert.AreEqual(g0.X + 10 * 1.44f, g1.X, 0.01f);
            Assert.AreEqual(g0.Y - 8 * 1.44f, g1.Y, 0.01f);
            Assert.AreEqual(700 * 1.44f, g1.Width, 0.01f);
            Assert.AreEqual(540 * 1.44f, g1.Height, 0.01f);
        }

        [TestMethod]
        public void AWiderScreenKeepsTheMonitorCentred()
        {
            var (monitor, _) = Default(2560, 1080); // an ultrawide
            Assert.AreEqual(1280f, monitor.X + monitor.Width / 2, 0.01f);
        }

        [TestMethod]
        public void SlowMouseMovementIsNotLostAtLowSensitivity()
        {
            var input = new PlayerInput(new ManagedDoom.Config { mouse_sensitivity = 1 });
            var total = 0;
            for (var i = 0; i < 10; i++)
            {
                input.AddMouseTurn(1); // one pixel a tic: half a turning step, which rounds to nothing on its own
                var cmd = new ManagedDoom.TicCmd();
                input.BuildTicCmd(cmd);
                total += cmd.AngleTurn;
            }
            Assert.AreEqual(-5 * 8, total, "ten pixels at sensitivity 1 are five steps of 8");
        }

        [TestMethod]
        public void ResettingForgetsTheLeftOverMovement()
        {
            var input = new PlayerInput(new ManagedDoom.Config { mouse_sensitivity = 1 });
            input.AddMouseTurn(1);
            input.BuildTicCmd(new ManagedDoom.TicCmd()); // leaves half a step over
            input.ReleaseAll();
            var cmd = new ManagedDoom.TicCmd();
            input.AddMouseTurn(1);
            input.BuildTicCmd(cmd);
            Assert.AreEqual(0, (int)cmd.AngleTurn, "the half step from before is gone: this pixel is half a step again, which rounds to nothing");
        }
    }
}
