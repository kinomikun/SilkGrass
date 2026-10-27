using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Silk.NET.OpenGL;

namespace SilkGrass.Graphics
{
    /// <summary>
    /// Reads back the default framebuffer and writes it as a PNG. The encoder
    /// is the minimal one PNG allows (RGBA8, no filtering, one zlib stream),
    /// which keeps the project free of image libraries.
    /// </summary>
    public static class Screenshot
    {
        public static unsafe void Capture(GL gl, int width, int height, string path)
        {
            var pixels = new byte[width * height * 4];
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            gl.ReadBuffer(ReadBufferMode.Back);
            gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            fixed (byte* p = pixels)
                gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);

            // GL rows run bottom-up; PNG rows top-down. Alpha is forced opaque.
            var rows = new byte[height * (width * 4 + 1)];
            for (int y = 0; y < height; y++)
            {
                int dst = y * (width * 4 + 1);
                rows[dst] = 0; // filter type: none
                System.Buffer.BlockCopy(pixels, (height - 1 - y) * width * 4, rows, dst + 1, width * 4);
                for (int x = 0; x < width; x++) rows[dst + 1 + x * 4 + 3] = 255;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var file = File.Create(path);
            WritePng(file, width, height, rows);
            Log.Info($"[Screenshot] Saved {Path.GetFullPath(path)}");
        }

        private static void WritePng(Stream output, int width, int height, byte[] filteredRows)
        {
            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8;  // bit depth
            header[9] = 6;  // color type: RGBA
            WriteChunk(output, "IHDR", header);

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                    zlib.Write(filteredRows, 0, filteredRows.Length);
                WriteChunk(output, "IDAT", compressed.ToArray());
            }

            WriteChunk(output, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            output.Write(length);

            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(typeBytes);
            output.Write(data);

            uint crc = Crc32(typeBytes, 0xFFFFFFFFu);
            crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            output.Write(crcBytes);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data, uint crc)
        {
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
