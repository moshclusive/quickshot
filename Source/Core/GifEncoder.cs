using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickShot.Core
{
    /// <summary>
    /// Pure-C# High-Quality GIF89a Encoder:
    /// - 16-bit rgb565 adaptive 256-color median-cut palette per frame
    /// - Clamped Floyd-Steinberg error-diffusion dithering using O(1) 65,536-color LUT
    /// - Automatic consecutive duplicate frame coalescing for smaller GIF size
    /// - Fast LZW compressor with Netscape 2.0 infinite looping
    /// </summary>
    public static class GifEncoder
    {
        public class GifFrame
        {
            public byte[] Bgra { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int DelayCs { get; set; } = 10;
        }

        public static GifFrame CaptureFrameFromBitmap(Bitmap bmp, int delayCs = 10)
        {
            int w = bmp.Width, h = bmp.Height;
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int byteLen = w * h * 4;
                byte[] buf = new byte[byteLen];
                if (bd.Stride == w * 4)
                {
                    Marshal.Copy(bd.Scan0, buf, 0, byteLen);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(IntPtr.Add(bd.Scan0, y * bd.Stride), buf, y * w * 4, w * 4);
                }
                return new GifFrame { Bgra = buf, Width = w, Height = h, DelayCs = delayCs };
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        public static byte[] Encode(IList<GifFrame> rawFrames)
        {
            if (rawFrames == null || rawFrames.Count == 0)
                throw new ArgumentException("No GIF frames to encode");

            // Coalesce consecutive nearly-identical frames to keep GIF compact and timing accurate
            var frames = CoalesceFrames(rawFrames);
            int width = frames[0].Width;
            int height = frames[0].Height;

            using var ms = new MemoryStream(Math.Max(65536, width * height));
            using var bw = new BinaryWriter(ms);

            byte[] firstPaletteRgb = BuildRgb565MedianCutPalette(frames[0].Bgra, width * height);

            // Header: GIF89a
            bw.Write(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 });
            // Logical Screen Descriptor with 256-entry Global Color Table (0xF7) for full viewer compatibility
            bw.Write((ushort)width);
            bw.Write((ushort)height);
            bw.Write((byte)0xF7);
            bw.Write((byte)0x00); // Background Color Index
            bw.Write((byte)0x00); // Pixel Aspect Ratio
            bw.Write(firstPaletteRgb);

            // Netscape 2.0 Application Extension (Infinite Loop)
            bw.Write(new byte[] { 0x21, 0xFF, 0x0B });
            bw.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            bw.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });

            for (int f = 0; f < frames.Count; f++)
            {
                var frame = frames[f];
                byte[] paletteRgb = f == 0
                    ? firstPaletteRgb
                    : BuildRgb565MedianCutPalette(frame.Bgra, width * height);
                byte[] indexed = QuantizeAndDitherBgra(frame.Bgra, width, height, paletteRgb);

                // Graphic Control Extension
                ushort delay = (ushort)Math.Max(2, frame.DelayCs);
                bw.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 }); // Dispose: do not dispose
                bw.Write(delay);
                bw.Write((byte)0x00); // Transparent color index
                bw.Write((byte)0x00); // Block terminator

                // Image Descriptor (Frame 0 uses Global Color Table 0x00; Frames 1..N use adaptive Local Color Table 0x87)
                bw.Write((byte)0x2C);
                bw.Write((ushort)0);
                bw.Write((ushort)0);
                bw.Write((ushort)width);
                bw.Write((ushort)height);
                bw.Write((byte)(f == 0 ? 0x00 : 0x87));

                if (f > 0)
                    bw.Write(paletteRgb);

                // LZW Compressed Image Data
                WriteLzw(bw, indexed, 8);
            }

            // GIF Trailer
            bw.Write((byte)0x3B);
            bw.Flush();
            return ms.ToArray();
        }

        private static List<GifFrame> CoalesceFrames(IList<GifFrame> raw)
        {
            var result = new List<GifFrame>(raw.Count);
            foreach (var f in raw)
            {
                if (result.Count > 0)
                {
                    var prev = result[result.Count - 1];
                    if (prev.DelayCs < 400 && AreFramesVeryClose(prev.Bgra, f.Bgra))
                    {
                        prev.DelayCs += f.DelayCs;
                        continue;
                    }
                }
                result.Add(new GifFrame { Bgra = f.Bgra, Width = f.Width, Height = f.Height, DelayCs = f.DelayCs });
            }
            return result;
        }

        private static bool AreFramesVeryClose(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int step = Math.Max(4, (a.Length / 4096) * 4);
            for (int i = 0; i < a.Length; i += step)
            {
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 8)
                    return false;
            }
            return true;
        }

        private struct ColorBox
        {
            public int Start;
            public int End;
            public int RMin, RMax, GMin, GMax, BMin, BMax;
        }

        private static byte[] BuildRgb565MedianCutPalette(byte[] bgra, int pixelCount)
        {
            int[] hist = new int[65536];
            int step = pixelCount > 250000 ? 2 : 1;
            for (int i = 0; i < pixelCount; i += step)
            {
                int p = i * 4;
                int b = bgra[p] >> 3;
                int g = bgra[p + 1] >> 2;
                int r = bgra[p + 2] >> 3;
                hist[(r << 11) | (g << 5) | b]++;
            }

            var colors = new List<int>(4096);
            for (int k = 0; k < 65536; k++)
            {
                if (hist[k] > 0) colors.Add(k);
            }

            byte[] pal = new byte[256 * 3];
            if (colors.Count <= 256)
            {
                for (int i = 0; i < colors.Count; i++)
                {
                    int k = colors[i];
                    pal[i * 3] = (byte)(((k >> 11) & 31) * 255 / 31);
                    pal[i * 3 + 1] = (byte)(((k >> 5) & 63) * 255 / 63);
                    pal[i * 3 + 2] = (byte)((k & 31) * 255 / 31);
                }
                return pal;
            }

            int[] colorArr = colors.ToArray();
            var boxes = new List<ColorBox>(256) { MakeBox(colorArr, 0, colorArr.Length) };

            while (boxes.Count < 256)
            {
                int splitIdx = -1;
                int bestRange = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var bx = boxes[i];
                    if (bx.End - bx.Start <= 1) continue;
                    int range = Math.Max((bx.RMax - bx.RMin) * 8, Math.Max((bx.GMax - bx.GMin) * 4, (bx.BMax - bx.BMin) * 8));
                    if (range > bestRange)
                    {
                        bestRange = range;
                        splitIdx = i;
                    }
                }
                if (splitIdx == -1) break;

                var box = boxes[splitIdx];
                int rSpan = (box.RMax - box.RMin) * 8;
                int gSpan = (box.GMax - box.GMin) * 4;
                int bSpan = (box.BMax - box.BMin) * 8;

                int channel = (gSpan >= rSpan && gSpan >= bSpan) ? 1 : (rSpan >= bSpan ? 0 : 2);
                Array.Sort(colorArr, box.Start, box.End - box.Start, Comparer<int>.Create((a, b) =>
                {
                    int va = channel == 0 ? ((a >> 11) & 31) : (channel == 1 ? ((a >> 5) & 63) : (a & 31));
                    int vb = channel == 0 ? ((b >> 11) & 31) : (channel == 1 ? ((b >> 5) & 63) : (b & 31));
                    return va.CompareTo(vb);
                }));

                long totalWeight = 0;
                for (int i = box.Start; i < box.End; i++) totalWeight += hist[colorArr[i]];
                long half = totalWeight / 2;
                long acc = 0;
                int mid = box.Start + 1;
                for (int i = box.Start; i < box.End - 1; i++)
                {
                    acc += hist[colorArr[i]];
                    mid = i + 1;
                    if (acc >= half) break;
                }

                boxes[splitIdx] = MakeBox(colorArr, box.Start, mid);
                boxes.Add(MakeBox(colorArr, mid, box.End));
            }

            for (int i = 0; i < boxes.Count && i < 256; i++)
            {
                var bx = boxes[i];
                long rSum = 0, gSum = 0, bSum = 0, wSum = 0;
                for (int j = bx.Start; j < bx.End; j++)
                {
                    int k = colorArr[j];
                    int w = hist[k];
                    rSum += (((k >> 11) & 31) * 255 / 31) * w;
                    gSum += (((k >> 5) & 63) * 255 / 63) * w;
                    bSum += ((k & 31) * 255 / 31) * w;
                    wSum += w;
                }
                if (wSum > 0)
                {
                    pal[i * 3] = (byte)(rSum / wSum);
                    pal[i * 3 + 1] = (byte)(gSum / wSum);
                    pal[i * 3 + 2] = (byte)(bSum / wSum);
                }
            }
            return pal;
        }

        private static ColorBox MakeBox(int[] arr, int start, int end)
        {
            int rMin = 31, rMax = 0, gMin = 63, gMax = 0, bMin = 31, bMax = 0;
            for (int i = start; i < end; i++)
            {
                int k = arr[i];
                int r = (k >> 11) & 31, g = (k >> 5) & 63, b = k & 31;
                if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                if (b < bMin) bMin = b; if (b > bMax) bMax = b;
            }
            return new ColorBox { Start = start, End = end, RMin = rMin, RMax = rMax, GMin = gMin, GMax = gMax, BMin = bMin, BMax = bMax };
        }

        private static byte[] QuantizeAndDitherBgra(byte[] bgra, int width, int height, byte[] palette)
        {
            int len = width * height;
            byte[] indexed = new byte[len];
            short[] lut = new short[65536];
            for (int i = 0; i < 65536; i++) lut[i] = -1;

            short[] err = new short[len * 3];
            for (int i = 0, p = 0; i < len; i++, p += 4)
            {
                int k = i * 3;
                err[k] = bgra[p + 2];     // R
                err[k + 1] = bgra[p + 1]; // G
                err[k + 2] = bgra[p];     // B
            }

            for (int y = 0; y < height; y++)
            {
                int rowOff = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = rowOff + x;
                    int k = i * 3;
                    int r = err[k];
                    int g = err[k + 1];
                    int b = err[k + 2];

                    int cr = r < 0 ? 0 : (r > 255 ? 255 : r);
                    int cg = g < 0 ? 0 : (g > 255 ? 255 : g);
                    int cb = b < 0 ? 0 : (b > 255 ? 255 : b);

                    int key = ((cr >> 3) << 11) | ((cg >> 2) << 5) | (cb >> 3);
                    short idx = lut[key];
                    if (idx == -1)
                    {
                        idx = (short)FindNearestPaletteIndex(palette, cr, cg, cb);
                        lut[key] = idx;
                    }
                    indexed[i] = (byte)idx;

                    int p3 = idx * 3;
                    int er = cr - palette[p3];
                    int eg = cg - palette[p3 + 1];
                    int eb = cb - palette[p3 + 2];
                    if (er > 48) er = 48; else if (er < -48) er = -48;
                    if (eg > 48) eg = 48; else if (eg < -48) eg = -48;
                    if (eb > 48) eb = 48; else if (eb < -48) eb = -48;

                    if (er == 0 && eg == 0 && eb == 0) continue;

                    if (x + 1 < width)
                    {
                        int kr = k + 3;
                        err[kr] += (short)((er * 7) >> 4);
                        err[kr + 1] += (short)((eg * 7) >> 4);
                        err[kr + 2] += (short)((eb * 7) >> 4);
                    }
                    if (y + 1 < height)
                    {
                        int nextRow = (rowOff + width + x) * 3;
                        if (x > 0)
                        {
                            int kbl = nextRow - 3;
                            err[kbl] += (short)((er * 3) >> 4);
                            err[kbl + 1] += (short)((eg * 3) >> 4);
                            err[kbl + 2] += (short)((eb * 3) >> 4);
                        }
                        err[nextRow] += (short)((er * 5) >> 4);
                        err[nextRow + 1] += (short)((eg * 5) >> 4);
                        err[nextRow + 2] += (short)((eb * 5) >> 4);
                        if (x + 1 < width)
                        {
                            int kbr = nextRow + 3;
                            err[kbr] += (short)(er >> 4);
                            err[kbr + 1] += (short)(eg >> 4);
                            err[kbr + 2] += (short)(eb >> 4);
                        }
                    }
                }
            }
            return indexed;
        }

        private static int FindNearestPaletteIndex(byte[] palette, int r, int g, int b)
        {
            int bestIdx = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                int p = i * 3;
                int dr = r - palette[p];
                int dg = g - palette[p + 1];
                int db = b - palette[p + 2];
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                    if (dist == 0) break;
                }
            }
            return bestIdx;
        }

        private static void WriteLzw(BinaryWriter bw, byte[] pixels, int minCodeSize)
        {
            bw.Write((byte)minCodeSize);
            int clearCode = 1 << minCodeSize;
            int eoiCode = clearCode + 1;

            byte[] block = new byte[256];
            int blockLen = 0;
            int curAccum = 0;
            int curBits = 0;

            void FlushBlock()
            {
                if (blockLen > 0)
                {
                    bw.Write((byte)blockLen);
                    bw.Write(block, 0, blockLen);
                    blockLen = 0;
                }
            }

            void Emit(int code, int codeSize)
            {
                curAccum |= (code << curBits);
                curBits += codeSize;
                while (curBits >= 8)
                {
                    block[blockLen++] = (byte)(curAccum & 0xFF);
                    if (blockLen == 255) FlushBlock();
                    curAccum >>= 8;
                    curBits -= 8;
                }
            }

            const int HashSize = 5003;
            int[] htab = new int[HashSize];
            int[] codetab = new int[HashSize];

            void ResetHash()
            {
                for (int i = 0; i < HashSize; i++) htab[i] = -1;
            }

            int nBits = minCodeSize + 1;
            int maxCode = (1 << nBits) - 1;
            int freeEnt = clearCode + 2;

            ResetHash();
            Emit(clearCode, nBits);

            int ent = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int c = pixels[i];
                int fcode = (c << 12) + ent;
                int h = (c << 4) ^ ent;
                if (htab[h] == fcode)
                {
                    ent = codetab[h];
                    continue;
                }
                if (htab[h] >= 0)
                {
                    int disp = h == 0 ? 1 : HashSize - h;
                    bool found = false;
                    while (true)
                    {
                        h -= disp;
                        if (h < 0) h += HashSize;
                        if (htab[h] == fcode)
                        {
                            ent = codetab[h];
                            found = true;
                            break;
                        }
                        if (htab[h] < 0) break;
                    }
                    if (found) continue;
                }

                Emit(ent, nBits);
                if (freeEnt > maxCode && nBits < 12)
                {
                    nBits++;
                    maxCode = (1 << nBits) - 1;
                }
                ent = c;
                if (freeEnt < 4096)
                {
                    codetab[h] = freeEnt++;
                    htab[h] = fcode;
                }
                else
                {
                    Emit(clearCode, nBits);
                    ResetHash();
                    freeEnt = clearCode + 2;
                    nBits = minCodeSize + 1;
                    maxCode = (1 << nBits) - 1;
                }
            }

            Emit(ent, nBits);
            if (freeEnt > maxCode && nBits < 12)
            {
                nBits++;
                maxCode = (1 << nBits) - 1;
            }
            Emit(eoiCode, nBits);
            while (curBits > 0)
            {
                block[blockLen++] = (byte)(curAccum & 0xFF);
                if (blockLen == 255) FlushBlock();
                curAccum >>= 8;
                curBits -= 8;
            }
            FlushBlock();
            bw.Write((byte)0x00); // Block Terminator
        }
    }
}

