// A minimal PNG writer, only so a test can save the picture it rendered and a person can look at it. No packages.
using System;
using System.IO;
using System.IO.Compression;

namespace ManagedDoomTest
{
    public static class Png
    {
        /// <summary>Reads an 8-bit RGBA PNG without interlacing (what the game's editor and this repo's pictures are): width, height, and 4 bytes a pixel.</summary>
        public static (int Width, int Height, byte[] Rgba) Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int width = 0, height = 0, position = 8;
            var data = new MemoryStream();
            while (position < bytes.Length)
            {
                var length = (bytes[position] << 24) | (bytes[position + 1] << 16) | (bytes[position + 2] << 8) | bytes[position + 3];
                var type = System.Text.Encoding.ASCII.GetString(bytes, position + 4, 4);
                if (type == "IHDR")
                {
                    width = (bytes[position + 8] << 24) | (bytes[position + 9] << 16) | (bytes[position + 10] << 8) | bytes[position + 11];
                    height = (bytes[position + 12] << 24) | (bytes[position + 13] << 16) | (bytes[position + 14] << 8) | bytes[position + 15];
                    if (bytes[position + 16] != 8 || bytes[position + 17] != 6 || bytes[position + 20] != 0) throw new NotSupportedException("only 8-bit RGBA without interlacing");
                }
                else if (type == "IDAT") data.Write(bytes, position + 8, length);
                position += 12 + length;
            }
            data.Position = 0;
            var raw = new MemoryStream();
            using (var z = new ZLibStream(data, CompressionMode.Decompress)) z.CopyTo(raw);
            var source = raw.ToArray();
            var stride = width * 4;
            var rgba = new byte[height * stride];
            for (var y = 0; y < height; y++)
            {
                var filter = source[y * (stride + 1)];
                for (var x = 0; x < stride; x++)
                {
                    int a = x >= 4 ? rgba[y * stride + x - 4] : 0, b = y > 0 ? rgba[(y - 1) * stride + x] : 0, c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                    int value = source[y * (stride + 1) + 1 + x], add;
                    switch (filter)
                    {
                        case 1: add = a; break;
                        case 2: add = b; break;
                        case 3: add = (a + b) / 2; break;
                        case 4:
                            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                            add = pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break;
                        default: add = 0; break;
                    }
                    rgba[y * stride + x] = (byte)(value + add);
                }
            }
            return (width, height, rgba);
        }

        public static void Save(string path, int width, int height, byte[] rgba)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var raw = new MemoryStream())
            {
                for (var y = 0; y < height; y++)
                {
                    raw.WriteByte(0); // filter: none
                    raw.Write(rgba, y * width * 4, width * 4);
                }
                var compressed = new MemoryStream();
                using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                {
                    z.Write(raw.GetBuffer(), 0, (int)raw.Length);
                }

                using (var file = File.Create(path))
                {
                    file.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
                    var header = new byte[13];
                    WriteBigEndian(header, 0, width);
                    WriteBigEndian(header, 4, height);
                    header[8] = 8; // bits per channel
                    header[9] = 6; // RGBA
                    Chunk(file, "IHDR", header);
                    Chunk(file, "IDAT", compressed.ToArray());
                    Chunk(file, "IEND", new byte[0]);
                }
            }
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            s.Write(length);
            var body = new byte[4 + data.Length];
            for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
            Array.Copy(data, 0, body, 4, data.Length);
            s.Write(body);
            var crc = new byte[4];
            WriteBigEndian(crc, 0, (int)Crc32(body));
            s.Write(crc);
        }

        private static void WriteBigEndian(byte[] b, int offset, int value)
        {
            b[offset] = (byte)(value >> 24);
            b[offset + 1] = (byte)(value >> 16);
            b[offset + 2] = (byte)(value >> 8);
            b[offset + 3] = (byte)value;
        }

        private static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
