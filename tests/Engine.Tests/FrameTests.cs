// Our own tests (not from upstream): the engine glue in src/Host, run on Freedoom with no window, no sound and no Godot.
using System.Collections.Generic;
using System.Linq;
using DoomedSpire.Host;
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
    }
}
