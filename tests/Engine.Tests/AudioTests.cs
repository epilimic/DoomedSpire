// Our own tests (not from upstream): DOOM's sound effects and music, src/Host/EngineSound.cs, EngineMusic.cs, DoomAudio.cs, run on Freedoom with no game and no speakers.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DoomedSpire.Host;
using ManagedDoom;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ManagedDoomTest
{
    [TestClass]
    public class AudioTests
    {
        private const int FramesPerTic = EngineSound.OutputRate / 35; // 1260: exactly one game tic of audio

        private static readonly string SoundFont = "../../assets/soundfont/TimGM6mb.sf2";

        private sealed class Capture
        {
            public readonly List<float> Left = new List<float>();
            public readonly List<float> Right = new List<float>();
            public readonly List<string> Reports = new List<string>();
        }

        private static DoomSession Start(bool music, Capture capture)
        {
            var options = new AudioOptions
            {
                Music = music,
                OpenSoundFont = () => File.OpenRead(SoundFont),
                Report = capture.Reports.Add,
            };
            return DoomSession.Create(WadPath.Doom1, options);
        }

        /// <summary>Plays the game for <paramref name="tics"/> tics and keeps all the audio it made.</summary>
        private static void Run(DoomSession session, Capture capture, int tics)
        {
            var left = new float[FramesPerTic];
            var right = new float[FramesPerTic];
            for (var i = 0; i < tics; i++)
            {
                session.Tick();
                session.RenderAudio(left, right, FramesPerTic);
                capture.Left.AddRange(left);
                capture.Right.AddRange(right);
            }
        }

        private static float Peak(IEnumerable<float> samples) => samples.Select(Math.Abs).DefaultIfEmpty(0f).Max();
        private static double Rms(IEnumerable<float> samples) => Math.Sqrt(samples.Select(s => (double)s * s).DefaultIfEmpty(0).Average());

        [TestMethod]
        public void TheSoundEffectsAreLoaded()
        {
            var capture = new Capture();
            using (var session = Start(false, capture))
            {
                Assert.IsTrue(session.LoadedSoundCount >= 60, $"Freedoom Phase 1 has 69 sound effects, only {session.LoadedSoundCount} loaded");
                Assert.IsFalse(session.HasMusic, "music was switched off");
            }
        }

        [TestMethod]
        public void NothingMakesASoundBeforeAnythingHappens()
        {
            var capture = new Capture();
            using (var session = Start(false, capture))
            {
                Run(session, capture, 30);
                Assert.AreEqual(0f, Peak(capture.Left));
                Assert.AreEqual(0f, Peak(capture.Right));
            }
        }

        [TestMethod]
        public void FiringTheGunMakesASoundInTheMiddle()
        {
            var capture = new Capture();
            using (var session = Start(false, capture))
            {
                Run(session, capture, 30);
                var quiet = capture.Left.Count;
                session.PostMouseButton(DoomMouseButton.Mouse1, true);
                Run(session, capture, 40);

                var left = capture.Left.Skip(quiet).ToArray();
                var right = capture.Right.Skip(quiet).ToArray();
                Assert.IsTrue(Peak(left) > 0.05f, $"the pistol should be clearly audible, peak was {Peak(left)}");
                Assert.IsTrue(Peak(left) <= 1f && Peak(right) <= 1f);
                // The shot comes from the player itself, so it is centred: both ears hear the same.
                Assert.AreEqual(Rms(left), Rms(right), Rms(left) * 0.01);
                Assert.AreEqual(0, capture.Reports.Count, string.Join("; ", capture.Reports));
            }
        }

        [TestMethod]
        public void TheMusicPlaysAndLooksLikeMusic()
        {
            var capture = new Capture();
            using (var session = Start(true, capture))
            {
                Assert.IsTrue(session.HasMusic);
                Run(session, capture, 3 * 35);
                session.PostMouseButton(DoomMouseButton.Mouse1, true); // a few shots over the music, for the sample file
                Run(session, capture, 3 * 35);
                session.PostMouseButton(DoomMouseButton.Mouse1, false);
                Run(session, capture, 2 * 35);

                var all = capture.Left.Concat(capture.Right).ToArray();
                Assert.AreEqual(0, capture.Reports.Count, string.Join("; ", capture.Reports));
                Assert.IsTrue(all.All(s => !float.IsNaN(s) && !float.IsInfinity(s)), "no broken samples");
                Assert.IsTrue(Peak(all) > 0.02f, $"the music is nearly silent, peak {Peak(all)}");
                Assert.IsTrue(Peak(all) <= 1f);
                var rms = Rms(all);
                Assert.IsTrue(rms > 0.003 && rms < 0.4, $"the overall level should be a sensible one, rms {rms}");
                Assert.IsTrue(Rms(capture.Left) > 0.001 && Rms(capture.Right) > 0.001, "both channels carry sound");
                var clipped = all.Count(s => Math.Abs(s) >= 1f) / (double)all.Length;
                Assert.IsTrue(clipped < 0.001, $"{clipped:P2} of the samples are at full scale: too loud");

                Wav.Save("out/audio-sample.wav", EngineSound.OutputRate, capture.Left.ToArray(), capture.Right.ToArray()); // play it: tests/Engine.Tests/out/audio-sample.wav
            }
        }

        [TestMethod]
        public void NeverLouderThanFullScaleEvenWithTheGainsFarUp()
        {
            var capture = new Capture();
            using (var session = Start(true, capture))
            {
                session.SetAudioGains(50f, 50f);
                session.PostMouseButton(DoomMouseButton.Mouse1, true);
                Run(session, capture, 60);
                Assert.IsTrue(Peak(capture.Left) <= 1f && Peak(capture.Right) <= 1f);
                Assert.IsTrue(Peak(capture.Left) >= 0.99f, "turned up this far it should reach full scale");
            }
        }

        [TestMethod]
        public void TheGainsScaleTheLevel()
        {
            var loud = new Capture();
            var soft = new Capture();
            using (var a = Start(true, loud)) { a.SetAudioGains(1f, 1f); Run(a, loud, 60); }
            using (var b = Start(true, soft)) { b.SetAudioGains(0.25f, 0.25f); Run(b, soft, 60); }
            Assert.AreEqual(Rms(loud.Left) * 0.25, Rms(soft.Left), Rms(loud.Left) * 0.01);
        }

        [TestMethod]
        public void PanningKeepsTheSamePowerAndPutsSoundWhereItIs()
        {
            foreach (var pan in new[] { -1f, -0.5f, 0f, 0.3f, 1f })
            {
                EngineSound.PanGains(pan, 0.8f, out var l, out var r);
                Assert.AreEqual(0.8, Math.Sqrt(l * l + r * r), 0.0001);
            }
            EngineSound.PanGains(-1f, 1f, out var left1, out var right1);
            Assert.IsTrue(left1 > 0.999f && right1 < 0.001f, "full left is all in the left ear");
            EngineSound.PanGains(1f, 1f, out var left2, out var right2);
            Assert.IsTrue(left2 < 0.001f && right2 > 0.999f, "full right is all in the right ear");
        }

        [TestMethod]
        public void MixingIsCheapEnoughToDoEveryFrame()
        {
            var capture = new Capture();
            using (var session = Start(true, capture))
            {
                session.PostMouseButton(DoomMouseButton.Mouse1, true); // sound effects as well as music
                Run(session, capture, 10);

                var left = new float[735]; // one frame at 60 fps
                var right = new float[735];
                var timer = Stopwatch.StartNew();
                for (var i = 0; i < 120; i++) session.RenderAudio(left, right, left.Length); // two seconds of audio
                var millisecondsPerFrame = timer.Elapsed.TotalMilliseconds / 120;

                Directory.CreateDirectory("out");
                File.WriteAllText("out/audio-timing.txt", $"{millisecondsPerFrame:0.000} ms to render the audio for one 60 fps frame (music and sound effects)\n");
                Assert.IsTrue(millisecondsPerFrame < 4, $"rendering one frame of audio took {millisecondsPerFrame:0.00} ms; a 16 ms frame cannot afford much");
            }
        }
    }
}
