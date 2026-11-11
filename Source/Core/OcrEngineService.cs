using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickShot.Core
{
    public class OcrExtractionResult
    {
        public bool Success { get; set; }
        public string Text { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Universal Any-Language OCR Service:
    /// - Zero language dropdown, zero translation, zero grammar rewriting
    /// - Combines Windows Native WinRT OCR + Tesseract 5.2 (bundled tessdata_best ben + eng + osd)
    /// - Smart multi-variant pipeline for Bengali, English, mixed Bengali+English, dark UI, and colorful posters/banners
    /// - Automatically detects &amp; caches additional script models via OSD on first encounter.
    /// </summary>
    public static class OcrEngineService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        private static readonly object TessDirLock = new object();

        private enum VariantMode
        {
            NormalContrast,
            InvertedContrast,
            BrightPosterMask
        }

        private struct ImageStats
        {
            public double MeanLum;
            public bool IsDarkBg;
            public bool HasDarkAndLightMix;
            public bool HasColoredBg;
        }

        private class ScoredCandidate
        {
            public string Text;
            public float Conf;
            public int BenLetters;
            public int BenWords;
            public int LatinLetters;
            public int Score;
            public bool FromBrightMask;
        }

        private static readonly Dictionary<string, string> ScriptToTessLang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arabic"] = "ara",
            ["Bengali"] = "ben",
            ["Cyrillic"] = "rus",
            ["Devanagari"] = "hin",
            ["Greek"] = "ell",
            ["Gujarati"] = "guj",
            ["Gurmukhi"] = "pan",
            ["Han"] = "chi_sim",
            ["HanS"] = "chi_sim",
            ["HanT"] = "chi_tra",
            ["Hangul"] = "kor",
            ["Hebrew"] = "heb",
            ["Japanese"] = "jpn",
            ["Kannada"] = "kan",
            ["Khmer"] = "khm",
            ["Latin"] = "eng",
            ["Malayalam"] = "mal",
            ["Myanmar"] = "mya",
            ["Sinhala"] = "sin",
            ["Tamil"] = "tam",
            ["Telugu"] = "tel",
            ["Thai"] = "tha",
            ["Tibetan"] = "bod",
            ["Vietnamese"] = "vie"
        };

        public static string GetWritableTessdataDir()
        {
            lock (TessDirLock)
            {
                string appTess = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickShot", "tessdata");

                try
                {
                    if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                    if (Directory.Exists(appTess))
                    {
                        foreach (string file in Directory.GetFiles(appTess, "*.traineddata"))
                        {
                            string dest = Path.Combine(cacheDir, Path.GetFileName(file));
                            bool shouldCopy = !File.Exists(dest) || new FileInfo(file).Length != new FileInfo(dest).Length;
                            if (shouldCopy)
                            {
                                try { File.Copy(file, dest, true); } catch { }
                            }
                        }
                    }
                    return cacheDir;
                }
                catch
                {
                    return Directory.Exists(appTess) ? appTess : cacheDir;
                }
            }
        }

        public static async Task<OcrExtractionResult> ExtractAnyLanguageAsync(Bitmap sourceBitmap, Action<string> onProgress = null)
        {
            if (sourceBitmap == null)
                return new OcrExtractionResult { Success = false, Error = "No image to extract." };

            return await Task.Run(async () =>
            {
                try
                {
                    onProgress?.Invoke("Extracting text...");
                    string tessDir = GetWritableTessdataDir();
                    int scale = ComputeOcrScale(sourceBitmap.Width, sourceBitmap.Height);

                    using Bitmap scaled = ResizeClean(sourceBitmap, scale);
                    ImageStats stats = AnalyzeImageStats(scaled);

                    VariantMode primaryMode = stats.IsDarkBg ? VariantMode.InvertedContrast : VariantMode.NormalContrast;
                    using Bitmap primaryBmp = BuildPreprocessedVariant(scaled, primaryMode);
                    byte[] primaryPng = BitmapToPngBytes(primaryBmp);

                    // Run WinRT (all installed languages) & Tesseract ben+eng concurrently on Primary variant
                    var winrtTask = Task.Run(() => TryWinRtAllInstalledLanguages(primaryPng));
                    var primaryTessTask = Task.Run(() => RunTesseract(primaryPng, tessDir, "ben+eng", PageSegMode.Auto));
                    await Task.WhenAll(winrtTask, primaryTessTask).ConfigureAwait(false);

                    var winCandidates = winrtTask.Result;
                    var primaryRes = primaryTessTask.Result;
                    ScoredCandidate primaryScored = ScoreCandidate(primaryRes.Text, primaryRes.Confidence, false);

                    // Check non-English WinRT languages (Arabic, Hindi, Russian, Japanese, Chinese, etc.)
                    var nonEnWin = winCandidates
                        .Where(c => !c.LangTag.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Text))
                        .OrderByDescending(c => CountReadableLetters(c.Text))
                        .FirstOrDefault();

                    if (nonEnWin != null && CountReadableLetters(nonEnWin.Text) >= 2 && primaryScored.BenWords == 0)
                    {
                        return new OcrExtractionResult { Success = true, Text = nonEnWin.Text.Trim() };
                    }

                    var enWin = winCandidates.FirstOrDefault(c => c.LangTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
                    string enWinText = SanitizeEnglishOcr(enWin?.Text?.Trim() ?? "");

                    // Fast path: crisp high-confidence Bengali or Bengali+English directly from Primary
                    if (primaryScored.BenWords >= 1 && primaryRes.Confidence >= 0.76f && !stats.HasDarkAndLightMix && !stats.HasColoredBg)
                    {
                        return new OcrExtractionResult { Success = true, Text = CleanBengaliOcrText(primaryRes.Text) };
                    }

                    var benCandidates = new List<ScoredCandidate>();
                    if (primaryScored.BenLetters >= 2) benCandidates.Add(primaryScored);

                    // Secondary passes for dark UI themes, mixed light+dark regions, or colorful posters/thumbnails
                    bool needExtraPasses =
                        primaryScored.BenWords < 2 ||
                        primaryRes.Confidence < 0.78f ||
                        stats.HasDarkAndLightMix ||
                        stats.HasColoredBg;

                    if (needExtraPasses)
                    {
                        // 1) Inverted polarity pass when light+dark mix exists or primary wasn't already sufficient
                        VariantMode secondaryMode = stats.IsDarkBg ? VariantMode.NormalContrast : VariantMode.InvertedContrast;
                        using (Bitmap invBmp = BuildPreprocessedVariant(scaled, secondaryMode))
                        {
                            byte[] invPng = BitmapToPngBytes(invBmp);
                            var invRes = RunTesseract(invPng, tessDir, "ben+eng", PageSegMode.Auto);
                            ScoredCandidate invScored = ScoreCandidate(invRes.Text, invRes.Confidence, false);
                            if (invScored.BenWords >= 1) benCandidates.Add(invScored);
                        }

                        // 2) Bright-on-Color/Poster luminance & warm-yellow mask (handles memes, banners, red/blue/photo cards)
                        bool alreadyStrongBen = benCandidates.Any(c => c.BenWords >= 2 && c.Conf >= 0.80f);
                        if (!alreadyStrongBen || stats.HasColoredBg || stats.HasDarkAndLightMix)
                        {
                            using Bitmap posterBmp = BuildPreprocessedVariant(scaled, VariantMode.BrightPosterMask);
                            byte[] posterPng = BitmapToPngBytes(posterBmp);

                            var pAuto = RunTesseract(posterPng, tessDir, "ben+eng", PageSegMode.Auto);
                            ScoredCandidate sAuto = ScoreCandidate(pAuto.Text, pAuto.Confidence, true);
                            if (sAuto.BenWords >= 1) benCandidates.Add(sAuto);

                            // Posters & stylized headers frequently respond much better to SingleBlock when Auto misses top lines
                            if (sAuto.BenWords < 2 || pAuto.Confidence < 0.82f)
                            {
                                var pBlock = RunTesseract(posterPng, tessDir, "ben+eng", PageSegMode.SingleBlock);
                                ScoredCandidate sBlock = ScoreCandidate(pBlock.Text, pBlock.Confidence, true);
                                if (sBlock.BenWords >= 1) benCandidates.Add(sBlock);
                            }
                        }
                    }

                    // Pick highest-scoring Bengali / Mixed Bengali+English candidate
                    var bestBen = benCandidates
                        .Where(c => c.BenWords >= 1 && c.Score > 0)
                        .OrderByDescending(c => c.Score)
                        .FirstOrDefault();

                    if (bestBen != null)
                    {
                        string cleanedBen = CleanBengaliOcrText(bestBen.Text);
                        // If captured area had mixed light-UI English + colored/dark Bengali card where bright-mask dropped the English sidebar, merge cleanly!
                        if (!string.IsNullOrWhiteSpace(enWinText) && Regex.Matches(enWinText, @"[A-Za-z]{2,}").Count >= 2 &&
                            bestBen.FromBrightMask && bestBen.LatinLetters < Regex.Matches(enWinText, @"[A-Za-z]").Count / 2)
                        {
                            string merged = MergeUniqueMixedLines(cleanedBen, enWinText);
                            return new OcrExtractionResult { Success = true, Text = merged };
                        }
                        return new OcrExtractionResult { Success = true, Text = cleanedBen };
                    }

                    // If no Bengali detected & pure clean English exists
                    bool hasAsciiNoise = Regex.IsMatch(primaryRes.Text ?? "", @"[A-Za-z][/,\\|][A-Za-z]");
                    if (!hasAsciiNoise && !string.IsNullOrWhiteSpace(enWinText) &&
                        Regex.Matches(enWinText, @"[A-Za-z0-9]").Count >= 2 &&
                        (primaryRes.Confidence >= 0.72f || CountReadableLetters(enWinText) >= 4))
                    {
                        return new OcrExtractionResult { Success = true, Text = enWinText };
                    }

                    // Ambiguous or non-English/non-Bengali script: Auto-download/cache OSD & Script detector on demand
                    string osdPath = Path.Combine(tessDir, "osd.traineddata");
                    if (!File.Exists(osdPath))
                    {
                        onProgress?.Invoke("Preparing language script detector...");
                        await DownloadTrainedDataAsync("osd", osdPath).ConfigureAwait(false);
                    }
                    if (File.Exists(osdPath))
                    {
                        onProgress?.Invoke("Detecting script...");
                        string detectedScript = DetectScriptWithOsd(primaryBmp, tessDir);
                        if (!string.IsNullOrEmpty(detectedScript) &&
                            ScriptToTessLang.TryGetValue(detectedScript, out string targetTessLang) &&
                            targetTessLang != "ben" && targetTessLang != "eng")
                        {
                            string modelPath = Path.Combine(tessDir, targetTessLang + ".traineddata");
                            if (!File.Exists(modelPath))
                            {
                                onProgress?.Invoke($"Loading {detectedScript} OCR model...");
                                await DownloadTrainedDataAsync(targetTessLang, modelPath).ConfigureAwait(false);
                            }

                            if (File.Exists(modelPath))
                            {
                                onProgress?.Invoke("Extracting text...");
                                string combo = File.Exists(Path.Combine(tessDir, "eng.traineddata"))
                                    ? $"{targetTessLang}+eng"
                                    : targetTessLang;
                                var altRes = RunTesseract(primaryPng, tessDir, combo, PageSegMode.Auto);
                                if (!string.IsNullOrWhiteSpace(altRes.Text) && altRes.Confidence >= primaryRes.Confidence - 0.05f)
                                {
                                    return new OcrExtractionResult { Success = true, Text = altRes.Text.Trim() };
                                }
                            }
                        }
                    }

                    // Check already-cached additional languages in %LocalAppData%\QuickShot\tessdata
                    string bestCachedText = null;
                    float bestCachedConf = primaryRes.Confidence;
                    foreach (string tdFile in Directory.GetFiles(tessDir, "*.traineddata"))
                    {
                        string lang = Path.GetFileNameWithoutExtension(tdFile);
                        if (lang == "eng" || lang == "ben" || lang == "osd") continue;
                        var r = RunTesseract(primaryPng, tessDir, lang, PageSegMode.Auto);
                        if (!string.IsNullOrWhiteSpace(r.Text) && r.Confidence > bestCachedConf + 0.08f)
                        {
                            bestCachedConf = r.Confidence;
                            bestCachedText = r.Text.Trim();
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(bestCachedText))
                        return new OcrExtractionResult { Success = true, Text = bestCachedText };

                    // Final best fallback between WinRT English and Tesseract output
                    if (!string.IsNullOrWhiteSpace(enWinText) && CountReadableLetters(enWinText) >= CountReadableLetters(primaryRes.Text))
                        return new OcrExtractionResult { Success = true, Text = enWinText };
                    if (!string.IsNullOrWhiteSpace(primaryRes.Text))
                        return new OcrExtractionResult { Success = true, Text = SanitizeEnglishOcr(primaryRes.Text) };

                    return new OcrExtractionResult { Success = false, Error = "No clear readable text was detected in the selected area." };
                }
                catch (Exception ex)
                {
                    return new OcrExtractionResult { Success = false, Error = ex.Message };
                }
            });
        }

        private static int ComputeOcrScale(int w, int h)
        {
            if ((w < 400 || h < 240) && w * 3 <= 3200 && h * 3 <= 2400)
                return 3;
            if (w * 2 <= 3200 && h * 2 <= 2400)
                return 2;
            return 1;
        }

        private static Bitmap ResizeClean(Bitmap src, int scale)
        {
            int w = src.Width * scale;
            int h = src.Height * scale;
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return dst;
        }

        private static ImageStats AnalyzeImageStats(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = Math.Abs(bd.Stride);
                byte[] buf = new byte[stride * h];
                Marshal.Copy(bd.Scan0, buf, 0, buf.Length);

                int stepX = Math.Max(1, w / 48);
                int stepY = Math.Max(1, h / 48);
                double lumSum = 0;
                int total = 0, darkCnt = 0, lightCnt = 0, colorCnt = 0;

                for (int y = 0; y < h; y += stepY)
                {
                    for (int x = 0; x < w; x += stepX)
                    {
                        int i = y * stride + x * 4;
                        byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                        double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                        lumSum += lum;
                        if (lum < 95) darkCnt++;
                        else if (lum > 180) lightCnt++;
                        int maxC = Math.Max(r, Math.Max(g, b));
                        int minC = Math.Min(r, Math.Min(g, b));
                        if (maxC - minC > 48 && lum > 30 && lum < 220) colorCnt++;
                        total++;
                    }
                }

                double mean = total > 0 ? lumSum / total : 255;
                double darkFrac = total > 0 ? (double)darkCnt / total : 0;
                double lightFrac = total > 0 ? (double)lightCnt / total : 0;
                double colorFrac = total > 0 ? (double)colorCnt / total : 0;

                return new ImageStats
                {
                    MeanLum = mean,
                    IsDarkBg = mean < 112,
                    HasDarkAndLightMix = darkFrac >= 0.12 && lightFrac >= 0.12,
                    HasColoredBg = colorFrac >= 0.18
                };
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
        }

        private static Bitmap BuildPreprocessedVariant(Bitmap scaled, VariantMode mode)
        {
            int w = scaled.Width, h = scaled.Height, pad = 32;
            var sbd = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int ow = w + pad * 2, oh = h + pad * 2;
            var dst = new Bitmap(ow, oh, PixelFormat.Format32bppArgb);
            var obd = dst.LockBits(new Rectangle(0, 0, ow, oh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int sStride = Math.Abs(sbd.Stride);
                int oStride = Math.Abs(obd.Stride);
                byte[] src = new byte[sStride * h];
                byte[] outBytes = new byte[oStride * oh];
                Marshal.Copy(sbd.Scan0, src, 0, src.Length);
                for (int i = 0; i < outBytes.Length; i++) outBytes[i] = 255;

                if (mode != VariantMode.BrightPosterMask)
                {
                    bool invert = (mode == VariantMode.InvertedContrast);
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int si = y * sStride + x * 4;
                            double lum = 0.299 * src[si + 2] + 0.587 * src[si + 1] + 0.114 * src[si];
                            double v = invert ? (255.0 - lum) : lum;
                            // Gentle contrast curve preserving ClearType gray shades
                            double stretched = (v - 128.0) * 1.18 + 128.0;
                            byte c = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(stretched)));
                            int oi = (y + pad) * oStride + (x + pad) * 4;
                            outBytes[oi] = c;
                            outBytes[oi + 1] = c;
                            outBytes[oi + 2] = c;
                            outBytes[oi + 3] = 255;
                        }
                    }
                }
                else
                {
                    // Isolate bright white & warm yellow/cyan lettering cleanly from noisy/colored poster backgrounds
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int si = y * sStride + x * 4;
                            byte b = src[si], g = src[si + 1], r = src[si + 2];
                            double lum = 0.299 * r + 0.587 * g + 0.114 * b;
                            bool isBrightYellow = r > 188 && g > 172 && b < 145;
                            bool isBrightWhite = lum > 178 && (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 48);
                            bool isBrightCyan = g > 185 && b > 185 && r < 140;
                            byte c = (isBrightYellow || isBrightWhite || isBrightCyan) ? (byte)0 : (byte)255;
                            int oi = (y + pad) * oStride + (x + pad) * 4;
                            outBytes[oi] = c;
                            outBytes[oi + 1] = c;
                            outBytes[oi + 2] = c;
                            outBytes[oi + 3] = 255;
                        }
                    }
                }

                Marshal.Copy(outBytes, 0, obd.Scan0, outBytes.Length);
                return dst;
            }
            finally
            {
                scaled.UnlockBits(sbd);
                dst.UnlockBits(obd);
            }
        }

        private static ScoredCandidate ScoreCandidate(string rawText, float conf, bool fromBrightMask)
        {
            string t = rawText?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(t))
                return new ScoredCandidate { Text = "", Conf = conf, FromBrightMask = fromBrightMask, Score = 0 };

            int benLetters = Regex.Matches(t, @"[\u0985-\u09B9\u09DC-\u09DF]").Count;
            int latinLetters = Regex.Matches(t, @"[A-Za-z]").Count;
            int benWords = 0;
            foreach (Match m in Regex.Matches(t, @"[\u0980-\u09FF]{2,}"))
            {
                if (Regex.IsMatch(m.Value, @"[\u0985-\u09B9\u09DC-\u09DF]"))
                    benWords++;
            }

            int latinWords = 0;
            foreach (Match m in Regex.Matches(t, @"[A-Za-z]{2,}"))
            {
                latinWords++;
            }

            // Penalize OCR garbage strings (isolated 1-char slashes, pipes, random digits mixed into words)
            int noiseChars = Regex.Matches(t, @"[\\/@#$%^*~_=]").Count;
            int score = (int)(conf * 100) + benWords * 22 + benLetters * 6 + latinWords * 14 + latinLetters * 2 - noiseChars * 12;

            // Bonus for genuine multi-char Bengali words without gibberish
            if (benWords == 0 && benLetters <= 2) score -= 40;

            return new ScoredCandidate
            {
                Text = t,
                Conf = conf,
                BenLetters = benLetters,
                BenWords = benWords,
                LatinLetters = latinLetters,
                Score = score,
                FromBrightMask = fromBrightMask
            };
        }

        private static string MergeUniqueMixedLines(string benText, string enText)
        {
            var outLines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string line in benText.Replace("\r\n", "\n").Split('\n'))
            {
                string l = line.Trim();
                if (l.Length >= 2 && seen.Add(l)) outLines.Add(l);
            }
            foreach (string line in enText.Replace("\r\n", "\n").Split('\n'))
            {
                string l = line.Trim();
                if (l.Length >= 2 && !Regex.IsMatch(benText, Regex.Escape(l), RegexOptions.IgnoreCase) && seen.Add(l))
                    outLines.Add(l);
            }
            return string.Join(Environment.NewLine, outLines).Trim();
        }

        private static string DetectScriptWithOsd(Bitmap preprocessed, string tessDir)
        {
            try
            {
                using Bitmap tight = CropToNonWhiteContent(preprocessed, 10);
                // Tile into a dense grid so even cursive/connected scripts (Arabic, Devanagari) have 50+ components for OSD
                int cols = Math.Max(1, Math.Min(5, 1600 / Math.Max(60, tight.Width)));
                int rows = Math.Max(1, Math.Min(8, 1200 / Math.Max(30, tight.Height)));
                if (cols * rows < 12)
                {
                    cols = Math.Max(cols, 3);
                    rows = Math.Max(rows, 5);
                }

                using Bitmap osdBmp = (cols > 1 || rows > 1)
                    ? TileGrid(tight, cols, rows)
                    : (Bitmap)tight.Clone();

                byte[] bytes = BitmapToPngBytes(osdBmp);
                using var engine = new TesseractEngine(tessDir, "osd", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(bytes);
                using var page = engine.Process(pix, PageSegMode.OsdOnly);
                page.DetectBestOrientationAndScript(out _, out _, out string scriptName, out float scriptConf);
                if (!string.IsNullOrWhiteSpace(scriptName) && scriptConf >= 0.25f)
                    return scriptName.Trim();
            }
            catch { }
            return null;
        }

        private static Bitmap CropToNonWhiteContent(Bitmap src, int pad)
        {
            int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < src.Height; y += 2)
            {
                for (int x = 0; x < src.Width; x += 2)
                {
                    Color c = src.GetPixel(x, y);
                    if (c.R < 235 || c.G < 235 || c.B < 235)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX <= minX || maxY <= minY) return (Bitmap)src.Clone();
            int x0 = Math.Max(0, minX - pad), y0 = Math.Max(0, minY - pad);
            int x1 = Math.Min(src.Width, maxX + pad + 2), y1 = Math.Min(src.Height, maxY + pad + 2);
            return src.Clone(new Rectangle(x0, y0, x1 - x0, y1 - y0), PixelFormat.Format32bppArgb);
        }

        private static Bitmap TileGrid(Bitmap src, int cols, int rows)
        {
            int gap = 16;
            int w = src.Width * cols + gap * (cols - 1);
            int h = src.Height * rows + gap * (rows - 1);
            var tiled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(tiled))
            {
                g.Clear(Color.White);
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        g.DrawImageUnscaled(src, c * (src.Width + gap), r * (src.Height + gap));
            }
            return tiled;
        }

        private static async Task DownloadTrainedDataAsync(string lang, string destPath)
        {
            try
            {
                string url = $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata";
                byte[] data = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                if (data != null && data.Length > 10000)
                {
                    string tmp = destPath + ".tmp";
                    File.WriteAllBytes(tmp, data);
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(tmp, destPath);
                }
            }
            catch { }
        }

        private static (string Text, float Confidence) RunTesseract(byte[] pngBytes, string tessDir, string langSpec, PageSegMode psm)
        {
            try
            {
                using var engine = new TesseractEngine(tessDir, langSpec, EngineMode.LstmOnly);
                using var pix = Pix.LoadFromMemory(pngBytes);
                using var page = engine.Process(pix, psm);
                return (page.GetText() ?? "", page.GetMeanConfidence());
            }
            catch
            {
                try
                {
                    using var engine = new TesseractEngine(tessDir, langSpec, EngineMode.Default);
                    using var pix = Pix.LoadFromMemory(pngBytes);
                    using var page = engine.Process(pix, psm);
                    return (page.GetText() ?? "", page.GetMeanConfidence());
                }
                catch
                {
                    return ("", 0f);
                }
            }
        }

        private class WinRtCandidate
        {
            public string LangTag;
            public string Text;
        }

        private static List<WinRtCandidate> TryWinRtAllInstalledLanguages(byte[] pngBytes)
        {
            var list = new List<WinRtCandidate>();
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(pngBytes);
                    Await(writer.StoreAsync());
                    Await(writer.FlushAsync());
                    writer.DetachStream();
                }
                stream.Seek(0);
                var decoder = Await(BitmapDecoder.CreateAsync(stream));
                using var softBmp = Await(decoder.GetSoftwareBitmapAsync());

                foreach (var lang in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages)
                {
                    try
                    {
                        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(lang);
                        if (engine == null) continue;
                        var res = Await(engine.RecognizeAsync(softBmp));
                        IEnumerable<string> lines;
                        if (lang.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                            lang.LanguageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                        {
                            lines = res.Lines.Select(l => string.Concat(l.Words.Select(w => w.Text)));
                        }
                        else if (lang.LayoutDirection == Windows.Globalization.LanguageLayoutDirection.Rtl)
                        {
                            lines = res.Lines.Select(l => string.Join(" ", l.Words.Reverse().Select(w => w.Text)));
                        }
                        else
                        {
                            lines = res.Lines.Select(l => l.Text);
                        }
                        string text = string.Join(Environment.NewLine, lines).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                            list.Add(new WinRtCandidate { LangTag = lang.LanguageTag, Text = text });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static T Await<T>(IAsyncOperation<T> op)
        {
            using var ev = new ManualResetEventSlim(false);
            op.Completed = (info, status) => ev.Set();
            ev.Wait();
            return op.GetResults();
        }

        private static byte[] BitmapToPngBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private static int CountReadableLetters(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int cnt = 0;
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) cnt++;
            }
            return cnt;
        }

        public static string CleanBengaliOcrText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string s = text.Normalize(NormalizationForm.FormC);
            s = s.Replace('ৰ', 'র').Replace('ৱ', 'ব').Replace("ঽ", "");
            s = Regex.Replace(s, @"ে\s*া", "ো");
            s = Regex.Replace(s, @"ে\s*ৗ", "ৌ");
            s = Regex.Replace(s, @"([ক-হড়-ঢ়য়])\s+([া-ৌ্])", "$1$2");
            s = Regex.Replace(s, @"র্\s+([ক-হড়-ঢ়য়])", "র্$1");
            s = Regex.Replace(s, @"া{2,}", "া");
            s = Regex.Replace(s, @"ি{2,}", "ি");
            s = Regex.Replace(s, @"ী{2,}", "ী");
            s = Regex.Replace(s, @"ু{2,}", "ু");
            s = Regex.Replace(s, @"ূ{2,}", "ূ");
            s = Regex.Replace(s, @"ে{2,}", "ে");
            s = Regex.Replace(s, @"ৈ{2,}", "ৈ");
            s = Regex.Replace(s, @"ো{2,}", "ো");
            s = Regex.Replace(s, @"ৌ{2,}", "ৌ");
            // Only convert pipe '|' right adjacent to Bengali words into Dari '।'
            s = Regex.Replace(s, @"([\u0980-\u09FF])\s*[|]\s*", "$1 । ");
            // Drop orphan leading/trailing edge-border brackets/pipes produced around poster borders
            s = Regex.Replace(s, @"^[\|\[\]]+\s*", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"[ \t]+", " ");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        private static string SanitizeEnglishOcr(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string s = text.Trim();
            int latinCnt = Regex.Matches(s, @"[A-Za-z]").Count;
            int benCnt = Regex.Matches(s, @"[\u0985-\u09B9\u09DC-\u09DF]").Count;
            int hiCnt = Regex.Matches(s, @"[\u0900-\u0963\u0966-\u097F]").Count;
            if (latinCnt >= 3 && benCnt < 2 && hiCnt < 2)
            {
                s = Regex.Replace(s, @"(^|\n|\s)[|।](?=\s+[A-Za-z])", "$1I");
                s = Regex.Replace(s, @"[।|]", "");
            }
            s = Regex.Replace(s, @"[ \t]+", " ");
            return s.Trim();
        }
    }
}
