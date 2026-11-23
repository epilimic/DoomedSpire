// A minimal WAV writer, only so a test can save the sound it rendered and a person can play it. 16-bit stereo, no packages.
using System;
using System.IO;

namespace ManagedDoomTest
{
    public static class Wav
    {
        public static void Save(string path, int sampleRate, float[] left, float[] right)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var frames = Math.Min(left.Length, right.Length);
            using (var w = new BinaryWriter(File.Create(path)))
            {
                var dataBytes = frames * 4;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);          // PCM
                w.Write((short)2);          // stereo
                w.Write(sampleRate);
                w.Write(sampleRate * 4);    // bytes per second
                w.Write((short)4);          // bytes per frame
                w.Write((short)16);         // bits per sample
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);
                for (var i = 0; i < frames; i++)
                {
                    w.Write((short)Math.Round(Math.Clamp(left[i], -1f, 1f) * 32767f));
                    w.Write((short)Math.Round(Math.Clamp(right[i], -1f, 1f) * 32767f));
                }
            }
        }
    }
}
