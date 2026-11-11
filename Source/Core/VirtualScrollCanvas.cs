using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickShot.Core
{
    /// <summary>
    /// Seamless 2D Virtual Document Stitcher for live interactive scrolling:
    /// - Supports scrolling Down AND back Up (without losing/corrupting previously scrolled upper content)
    /// - Supports scrolling Right AND back Left (horizontal wide webpages / explorer folders)
    /// - Auto-detects &amp; strips fixed sticky headers/footers/sidebars cleanly on first scroll
    /// </summary>
    public sealed class VirtualScrollCanvas : IDisposable
    {
        private readonly Rectangle _workArea;
        private Bitmap _canvas;
        private Bitmap _lastRaw;
        private byte[] _lastBytes;
        private int _lastStride;

        private bool _stickyVertChecked;
        private bool _stickyHorizChecked;
        private int _stickyHeaderH;
        private int _stickyFooterH;
        private int _stickyLeftW;
        private int _stickyRightW;

        public Rectangle WorkArea => _workArea;
        public int ViewX { get; private set; }
        public int ViewY { get; private set; }
        public int CanvasWidth => _canvas.Width;
        public int CanvasHeight => _canvas.Height;

        public VirtualScrollCanvas(Rectangle workArea, Bitmap initialViewport)
        {
            _workArea = workArea;
            _lastRaw = EnsureSizeAnd32Argb(initialViewport, workArea.Width, workArea.Height);
            _lastBytes = ExtractPixels(_lastRaw, out _lastStride);

            _canvas = new Bitmap(workArea.Width, workArea.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_canvas))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImageUnscaled(_lastRaw, 0, 0);
            }
        }

        private const int MaxCanvasWidth = 24000;
        private const int MaxCanvasHeight = 32000;

        public bool ApplyViewportFrame(
            Bitmap rawScreenSlice,
            int scrollSignY,
            int scrollSignX,
            out int deltaX,
            out int deltaY,
            out int prependedLeft,
            out int prependedTop) =>
            ApplyViewportFrameWithFallback(rawScreenSlice, scrollSignY, scrollSignX, out deltaX, out deltaY, out prependedLeft, out prependedTop, 0, 0);

        public bool ApplyViewportFrameWithFallback(
            Bitmap rawScreenSlice,
            int scrollSignY,
            int scrollSignX,
            out int deltaX,
            out int deltaY,
            out int prependedLeft,
            out int prependedTop,
            int fallbackAdvanceY,
            int fallbackAdvanceX)
        {
            deltaX = 0;
            deltaY = 0;
            prependedLeft = 0;
            prependedTop = 0;

            using Bitmap cur = EnsureSizeAnd32Argb(rawScreenSlice, _workArea.Width, _workArea.Height);
            byte[] curBytes = ExtractPixels(cur, out int curStride);

            if (FramesNearlyIdentical(_lastBytes, _lastStride, curBytes, curStride, _workArea.Width, _workArea.Height))
                return false;

            int w = _workArea.Width, h = _workArea.Height;

            if (scrollSignY != 0 && !_stickyVertChecked)
            {
                DetectVerticalStickyBands(_lastBytes, _lastStride, curBytes, curStride, w, h, out _stickyHeaderH, out _stickyFooterH);
                _stickyVertChecked = true;
            }
            else if (_stickyHeaderH > 12)
            {
                // Shrink _stickyHeaderH only if a sustained multi-row band (>= 12 rows) moves inside it
                int xs = w > 60 ? (int)(w * 0.12) : 4;
                int xe = w > 60 ? (int)(w * 0.84) : Math.Max(xs + 4, w - 4);
                int streak = 0;
                for (int y = 0; y < _stickyHeaderH; y++)
                {
                    if (ComputeRowDiff(_lastBytes, _lastStride, curBytes, curStride, y, xs, xe) > 14.0)
                    {
                        if (++streak >= 12)
                        {
                            _stickyHeaderH = Math.Max(0, y - 11);
                            break;
                        }
                    }
                    else
                    {
                        streak = 0;
                    }
                }
            }
            if (scrollSignX != 0 && !_stickyHorizChecked)
            {
                DetectHorizontalStickyBands(_lastBytes, _lastStride, curBytes, curStride, w, h, out _stickyLeftW, out _stickyRightW);
                _stickyHorizChecked = true;
            }

            bool usedFallback = false;
            int candDy = 0;
            if (scrollSignY != 0)
                candDy = DetectVerticalShift(_lastBytes, _lastStride, curBytes, curStride, w, h, scrollSignY);
            else if (scrollSignX == 0)
                candDy = DetectVerticalShift(_lastBytes, _lastStride, curBytes, curStride, w, h, 0);

            if (candDy == 0 && scrollSignY != 0 && _canvas.Height > h)
                candDy = TryFindFrameInCanvasY(curBytes, curStride, w, h, scrollSignY);
            deltaY = candDy;

            if (scrollSignX != 0)
                deltaX = DetectHorizontalShift(_lastBytes, _lastStride, curBytes, curStride, w, h, scrollSignX);
            else if (deltaY == 0)
                deltaX = DetectHorizontalShift(_lastBytes, _lastStride, curBytes, curStride, w, h, 0);

            if (deltaX == 0 && deltaY == 0)
            {
                // Keep scrolling moving smoothly even over solid-color gaps or huge single-burst jumps
                if (scrollSignY != 0 && fallbackAdvanceY > 0)
                {
                    deltaY = scrollSignY > 0 ? fallbackAdvanceY : -fallbackAdvanceY;
                    usedFallback = true;
                }
                else if (scrollSignX != 0 && fallbackAdvanceX > 0)
                {
                    deltaX = scrollSignX > 0 ? fallbackAdvanceX : -fallbackAdvanceX;
                    usedFallback = true;
                }
                else
                    return false;
            }

            // Update 2D canvas vertically cleanly
            if (deltaY != 0)
            {
                int contentTop = _stickyHeaderH;
                int contentBottom = h - _stickyFooterH;
                int nextY = ViewY + deltaY;

                if (nextY < 0)
                {
                    prependedTop = -nextY;
                    int newCanvasH = Math.Min(MaxCanvasHeight, _canvas.Height + prependedTop);
                    int actualAdd = newCanvasH - _canvas.Height;
                    if (actualAdd > 0)
                    {
                        var grown = new Bitmap(_canvas.Width, newCanvasH, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(grown))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(_canvas, 0, actualAdd);
                            int drawTopH = Math.Min(contentTop + actualAdd, contentBottom);
                            if (drawTopH > 0)
                            {
                                using var stripSlice = cur.Clone(new Rectangle(0, 0, w, drawTopH), PixelFormat.Format32bppPArgb);
                                SanitizeFloatingWidgets(stripSlice, _lastBytes, _lastStride, curBytes, curStride, w, h, 0, drawTopH, deltaY);
                                g.DrawImageUnscaled(stripSlice, ViewX, 0);
                            }
                        }
                        _canvas.Dispose();
                        _canvas = grown;
                    }
                    ViewY = 0;
                    prependedTop = actualAdd;
                }
                else if (nextY + h > _canvas.Height)
                {
                    int newCanvasH = Math.Min(MaxCanvasHeight, nextY + h);
                    if (newCanvasH > _canvas.Height)
                    {
                        var grown = new Bitmap(_canvas.Width, newCanvasH, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(grown))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(_canvas, 0, 0);
                            int srcY = Math.Max(contentTop, contentBottom - deltaY);
                            int srcH = Math.Min(h - srcY, newCanvasH - (nextY + srcY));
                            if (srcH > 0)
                            {
                                using var stripSlice = cur.Clone(new Rectangle(0, srcY, w, srcH), PixelFormat.Format32bppPArgb);
                                SanitizeFloatingWidgets(stripSlice, _lastBytes, _lastStride, curBytes, curStride, w, h, srcY, srcH, deltaY);
                                g.DrawImageUnscaled(stripSlice, ViewX, nextY + srcY);
                            }
                        }
                        _canvas.Dispose();
                        _canvas = grown;
                    }
                    ViewY = Math.Min(nextY, _canvas.Height - h);
                }
                else
                {
                    ViewY = nextY;
                    if (!usedFallback && deltaX == 0 && contentBottom - contentTop >= 24)
                    {
                        int stampTop = contentTop;
                        int stampH = contentBottom - stampTop;
                        using var g = Graphics.FromImage(_canvas);
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(cur,
                            new Rectangle(ViewX, ViewY + stampTop, w, stampH),
                            new Rectangle(0, stampTop, w, stampH),
                            GraphicsUnit.Pixel);
                    }
                }
            }

            // Update 2D canvas horizontally cleanly
            if (deltaX != 0)
            {
                int contentLeft = _stickyLeftW;
                int contentRight = w - _stickyRightW;
                int nextX = ViewX + deltaX;

                if (nextX < 0)
                {
                    prependedLeft = -nextX;
                    int newCanvasW = Math.Min(MaxCanvasWidth, _canvas.Width + prependedLeft);
                    int actualAdd = newCanvasW - _canvas.Width;
                    if (actualAdd > 0)
                    {
                        var grown = new Bitmap(newCanvasW, _canvas.Height, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(grown))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(_canvas, actualAdd, 0);
                            int drawLeftW = Math.Min(contentLeft + actualAdd, contentRight);
                            g.DrawImage(cur,
                                new Rectangle(0, ViewY, drawLeftW, h),
                                new Rectangle(0, 0, drawLeftW, h),
                                GraphicsUnit.Pixel);
                        }
                        _canvas.Dispose();
                        _canvas = grown;
                    }
                    ViewX = 0;
                    prependedLeft = actualAdd;
                }
                else if (nextX + w > _canvas.Width)
                {
                    int newCanvasW = Math.Min(MaxCanvasWidth, nextX + w);
                    if (newCanvasW > _canvas.Width)
                    {
                        var grown = new Bitmap(newCanvasW, _canvas.Height, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(grown))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(_canvas, 0, 0);
                            int srcX = Math.Max(contentLeft, contentRight - deltaX);
                            int srcW = Math.Min(w - srcX, newCanvasW - (nextX + srcX));
                            if (srcW > 0)
                            {
                                g.DrawImage(cur,
                                    new Rectangle(nextX + srcX, ViewY, srcW, h),
                                    new Rectangle(srcX, 0, srcW, h),
                                    GraphicsUnit.Pixel);
                            }
                        }
                        _canvas.Dispose();
                        _canvas = grown;
                    }
                    ViewX = Math.Min(nextX, _canvas.Height > 0 ? _canvas.Width - w : 0);
                }
                else
                {
                    ViewX = nextX;
                }
            }

            _lastRaw.Dispose();
            _lastRaw = (Bitmap)cur.Clone();
            _lastBytes = curBytes;
            _lastStride = curStride;
            return true;
        }

        public void PaintViewportTo(Bitmap targetPArgb, int destX, int destY)
        {
            using var g = Graphics.FromImage(targetPArgb);
            g.CompositingMode = CompositingMode.SourceCopy;
            var srcRect = new Rectangle(
                Math.Max(0, Math.Min(_canvas.Width - _workArea.Width, ViewX)),
                Math.Max(0, Math.Min(_canvas.Height - _workArea.Height, ViewY)),
                Math.Min(_workArea.Width, _canvas.Width),
                Math.Min(_workArea.Height, _canvas.Height));
            g.DrawImage(_canvas, new Rectangle(destX, destY, srcRect.Width, srcRect.Height), srcRect, GraphicsUnit.Pixel);
        }

        public Bitmap CropDocumentRect(Rectangle docRect)
        {
            Rectangle clipped = Rectangle.Intersect(new Rectangle(0, 0, _canvas.Width, _canvas.Height), docRect);
            if (clipped.Width < 2 || clipped.Height < 2) return null;
            var outBmp = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outBmp))
            {
                g.DrawImage(_canvas, new Rectangle(0, 0, clipped.Width, clipped.Height), clipped, GraphicsUnit.Pixel);
            }
            return outBmp;
        }

        private static void ComputeActiveScrollBounds(
            byte[] prev, int pStride, byte[] cur, int cStride,
            int minX, int maxX, int minY, int maxY,
            out int actLeft, out int actRight, out int actTop, out int actBottom)
        {
            actLeft = minX; actRight = maxX;
            actTop = minY; actBottom = maxY;

            var activeRows = new List<int>();
            for (int y = minY; y < maxY; y += 4)
            {
                for (int x = minX; x < maxX; x += 12)
                {
                    int i0 = y * pStride + x * 4, i1 = y * cStride + x * 4;
                    int d = Math.Abs(prev[i0] - cur[i1]) + Math.Abs(prev[i0 + 1] - cur[i1 + 1]) + Math.Abs(prev[i0 + 2] - cur[i1 + 2]);
                    if (d > 14)
                    {
                        activeRows.Add(y);
                        break;
                    }
                }
            }

            if (activeRows.Count > 0)
            {
                // Merge runs separated by <= 52px whitespace gaps, ignoring tiny isolated UI glitches above sticky headers
                int bestStart = activeRows[0], bestEnd = activeRows[0] + 4;
                int runStart = activeRows[0], runEnd = activeRows[0] + 4;
                for (int i = 1; i < activeRows.Count; i++)
                {
                    int y = activeRows[i];
                    if (y - runEnd <= 52)
                    {
                        runEnd = y + 4;
                    }
                    else
                    {
                        if (runEnd - runStart > bestEnd - bestStart) { bestStart = runStart; bestEnd = runEnd; }
                        runStart = y;
                        runEnd = y + 4;
                    }
                }
                if (runEnd - runStart > bestEnd - bestStart) { bestStart = runStart; bestEnd = runEnd; }

                if (bestEnd - bestStart >= 48)
                {
                    actTop = Math.Max(minY, bestStart);
                    actBottom = Math.Min(maxY, bestEnd);
                }
            }

            int cLeft = int.MaxValue, cRight = -1;
            for (int y = actTop; y < actBottom; y += 6)
            {
                for (int x = minX; x < maxX; x += 12)
                {
                    int i0 = y * pStride + x * 4, i1 = y * cStride + x * 4;
                    int d = Math.Abs(prev[i0] - cur[i1]) + Math.Abs(prev[i0 + 1] - cur[i1 + 1]) + Math.Abs(prev[i0 + 2] - cur[i1 + 2]);
                    if (d > 14)
                    {
                        if (x < cLeft) cLeft = x;
                        if (x > cRight) cRight = x;
                    }
                }
            }
            if (cRight > cLeft && cRight - cLeft >= 48)
            {
                actLeft = Math.Max(minX, cLeft);
                actRight = Math.Min(maxX, cRight + 12);
            }
        }

        private int DetectVerticalShift(byte[] prev, int pStride, byte[] cur, int cStride, int w, int h, int hintSignY)
        {
            int top = _stickyHeaderH;
            int bottom = h - _stickyFooterH;
            int baseLeft = w > 80 ? Math.Max(_stickyLeftW + 4, (int)(w * 0.04)) : 4;
            int baseRight = w > 80 ? Math.Min(w - _stickyRightW - 4, (int)(w * 0.96)) : Math.Max(baseLeft + 4, w - 4);

            ComputeActiveScrollBounds(prev, pStride, cur, cStride, baseLeft, baseRight, top, bottom,
                out int xStart, out int xEnd, out int actTop, out int actBottom);

            // Identify contiguous static/sticky header & footer rows strictly at top/bottom bounds
            // (never mark matching boilerplate rows in the middle of a scrolling document as static)
            bool[] isStaticRow = new bool[h];
            int headerMovedStreak = 0;
            int maxHeaderScan = Math.Min(actBottom, actTop + Math.Max(40, (actBottom - actTop) / 3));
            for (int y = actTop; y < maxHeaderScan; y++)
            {
                double rDiff = ComputeRowDiff(prev, pStride, cur, cStride, y, xStart, xEnd);
                double rVar = ComputeHorizVariance(prev, pStride, y, 2, xStart, xEnd);
                if (rDiff < 8.0 && rVar >= 1.5)
                {
                    isStaticRow[y] = true;
                    headerMovedStreak = 0;
                }
                else if (rDiff >= 8.0 && rVar >= 1.5)
                {
                    if (++headerMovedStreak >= 6) break;
                }
            }
            int footerMovedStreak = 0;
            int minFooterScan = Math.Max(actTop, actBottom - Math.Max(40, (actBottom - actTop) / 4));
            for (int y = actBottom - 1; y >= minFooterScan; y--)
            {
                double rDiff = ComputeRowDiff(prev, pStride, cur, cStride, y, xStart, xEnd);
                double rVar = ComputeHorizVariance(prev, pStride, y, 2, xStart, xEnd);
                if (rDiff < 8.0 && rVar >= 1.5)
                {
                    isStaticRow[y] = true;
                    footerMovedStreak = 0;
                }
                else if (rDiff >= 8.0 && rVar >= 1.5)
                {
                    if (++footerMovedStreak >= 6) break;
                }
            }

            int bodyH = Math.Max(40, actBottom - actTop);
            int sampleH = Math.Min(28, Math.Max(12, bodyH / 9));
            double zeroErr = ScoreVerticalCandidate(prev, pStride, cur, cStride, actTop, actBottom, xStart, xEnd, 0, isStaticRow, out _);

            int[] anchorPcts = { 10, 22, 35, 50, 65, 78, 88, 93 };
            int bestDy = 0;
            double bestScore = double.MaxValue;

            if (hintSignY >= 0)
            {
                EvaluateVerticalCandidates(prev, pStride, cur, cStride, anchorPcts,
                    actTop, actBottom, bodyH, sampleH, xStart, xEnd, scanDown: true,
                    isStaticRow, zeroErr, ref bestDy, ref bestScore);
            }

            if (hintSignY <= 0)
            {
                EvaluateVerticalCandidates(prev, pStride, cur, cStride, anchorPcts,
                    actTop, actBottom, bodyH, sampleH, xStart, xEnd, scanDown: false,
                    isStaticRow, zeroErr, ref bestDy, ref bestScore);
            }

            // Direction-reversal fallback during smooth-scroll inertia
            if (bestDy == 0 && hintSignY != 0)
            {
                int oppDy = 0;
                double oppScore = double.MaxValue;
                EvaluateVerticalCandidates(prev, pStride, cur, cStride, anchorPcts,
                    actTop, actBottom, bodyH, sampleH, xStart, xEnd, scanDown: hintSignY < 0,
                    isStaticRow, zeroErr, ref oppDy, ref oppScore);
                if (oppDy != 0 && oppScore < 6.0 && oppScore < zeroErr * 0.45)
                    return oppDy;
            }

            return bestDy;
        }

        private static void EvaluateVerticalCandidates(
            byte[] prev, int pStride, byte[] cur, int cStride, int[] anchorPcts,
            int top, int bottom, int bodyH, int sampleH, int xStart, int xEnd, bool scanDown,
            bool[] isStaticRow, double zeroErr, ref int bestDy, ref double bestScore)
        {
            // Rank anchor bands by horizontal texture variance so active scrolling text strips lead
            var ranked = new List<(int AnchorY, double Variance)>(anchorPcts.Length);
            foreach (int pct in anchorPcts)
            {
                int anchorY = scanDown
                    ? top + Math.Max(2, (int)(bodyH * (pct / 100.0)) - sampleH)
                    : top + Math.Max(2, (int)(bodyH * (pct / 100.0)));
                if (anchorY < top + 1 || anchorY + sampleH > bottom - 1) continue;

                int staticHits = 0;
                for (int r = 0; r < sampleH; r += 2)
                {
                    if (isStaticRow[anchorY + r]) staticHits++;
                }
                if (staticHits * 2 > sampleH / 2) continue;

                double v = ComputeHorizVariance(prev, pStride, anchorY, sampleH, xStart, xEnd);
                ranked.Add((anchorY, v));
            }
            ranked.Sort((a, b) => b.Variance.CompareTo(a.Variance));

            var testedDys = new HashSet<int>();
            int checkedAnchors = 0;
            foreach (var cand in ranked)
            {
                if (bestScore < 0.35) break;
                if (cand.Variance < 1.2 && checkedAnchors > 0) break;
                checkedAnchors++;

                int minY = scanDown ? top : cand.AnchorY + 1;
                int maxY = scanDown ? cand.AnchorY - 1 : bottom - sampleH;
                if (minY > maxY) continue;

                var matches = MatchHorizontalStripCandidates(
                    prev, pStride, cur, cStride, cand.AnchorY, sampleH, minY, maxY, xStart, xEnd, scanDescending: scanDown);
                foreach (var (matchY, stripErr) in matches)
                {
                    if (stripErr > 24.0) continue;
                    int dy = scanDown ? (cand.AnchorY - matchY) : -(matchY - cand.AnchorY);
                    if (dy == 0 || !testedDys.Add(dy)) continue;

                    double fullErr = ScoreVerticalCandidate(prev, pStride, cur, cStride, top, bottom, xStart, xEnd, dy, isStaticRow, out double edgeRatio);
                    bool strongTextMatch = edgeRatio >= 0.72 && fullErr < 12.0 && fullErr < zeroErr * 0.90;
                    if (fullErr < 16.0 && (fullErr < zeroErr * 0.78 || strongTextMatch))
                    {
                        bool strictlyBetter = fullErr + 0.6 < bestScore;
                        bool nearTieCloserStep = Math.Abs(dy - bestDy) <= 3 && Math.Abs(fullErr - bestScore) <= 0.6 && Math.Abs(dy) < Math.Abs(bestDy);
                        if (strictlyBetter || nearTieCloserStep || bestDy == 0)
                        {
                            bestScore = fullErr;
                            bestDy = dy;
                        }
                    }
                }
            }
        }

        private static double ScoreVerticalCandidate(
            byte[] prev, int pStride, byte[] cur, int cStride,
            int top, int bottom, int xStart, int xEnd, int dy, bool[] isStaticRow, out double edgeRatio)
        {
            edgeRatio = 0;
            int prevStart = Math.Max(top, top + dy);
            int prevEnd = Math.Min(bottom, bottom + dy);
            int overlapH = prevEnd - prevStart;
            if (overlapH < 16) return double.MaxValue;

            double totalErr = 0;
            int totalSamples = 0;
            int edgeCount = 0;
            int edgeMatches = 0;
            int rowStep = Math.Max(3, overlapH / 32);
            int xStep = Math.Max(4, (xEnd - xStart) / 140);

            for (int yP = prevStart + 2; yP < prevEnd - 2; yP += rowStep)
            {
                int yC = yP - dy;
                if (isStaticRow != null && (isStaticRow[yP] || isStaticRow[yC])) continue;
                int prevLum = -1;
                for (int x = xStart; x < xEnd; x += xStep)
                {
                    int iP = yP * pStride + x * 4, iC = yC * cStride + x * 4;
                    int d = Math.Abs(prev[iP] - cur[iC]) + Math.Abs(prev[iP + 1] - cur[iC + 1]) + Math.Abs(prev[iP + 2] - cur[iC + 2]);
                    totalErr += d;
                    totalSamples++;

                    int lum = prev[iP] + prev[iP + 1] + prev[iP + 2];
                    if (prevLum >= 0 && Math.Abs(lum - prevLum) > 45)
                    {
                        edgeCount++;
                        int minEdgeD = d;
                        if (dy != 0 && minEdgeD > 55)
                        {
                            if (yC > 0)
                            {
                                int iUp = (yC - 1) * cStride + x * 4;
                                int dUp = Math.Abs(prev[iP] - cur[iUp]) + Math.Abs(prev[iP + 1] - cur[iUp + 1]) + Math.Abs(prev[iP + 2] - cur[iUp + 2]);
                                if (dUp < minEdgeD) minEdgeD = dUp;
                            }
                            if (yC + 1 < isStaticRow.Length)
                            {
                                int iDn = (yC + 1) * cStride + x * 4;
                                if (iDn + 2 < cur.Length)
                                {
                                    int dDn = Math.Abs(prev[iP] - cur[iDn]) + Math.Abs(prev[iP + 1] - cur[iDn + 1]) + Math.Abs(prev[iP + 2] - cur[iDn + 2]);
                                    if (dDn < minEdgeD) minEdgeD = dDn;
                                }
                            }
                        }
                        if (minEdgeD <= 58) edgeMatches++;
                    }
                    prevLum = lum;
                }
            }

            if (totalSamples == 0) return double.MaxValue;
            edgeRatio = edgeCount >= 10 ? (double)edgeMatches / edgeCount : 0;
            double avgErr = totalErr / totalSamples;
            if (dy != 0 && edgeCount >= 10 && edgeRatio < 0.56)
                return double.MaxValue;

            return avgErr;
        }

        private int TryFindFrameInCanvasY(byte[] curBytes, int curStride, int w, int h, int scrollSignY)
        {
            int top = _stickyHeaderH + 4;
            int bottom = h - _stickyFooterH - 4;
            if (bottom - top < 48) return 0;

            int minCandY = scrollSignY < 0 ? 0 : Math.Min(_canvas.Height - h, ViewY + 8);
            int maxCandY = scrollSignY < 0 ? Math.Max(0, ViewY - 8) : _canvas.Height - h;
            if (minCandY > maxCandY) return 0;

            int xStart = w > 80 ? Math.Max(_stickyLeftW + 8, (int)(w * 0.10)) : 4;
            int xEnd = w > 80 ? Math.Min(Math.Min(w - _stickyRightW - 8, (int)(w * 0.90)), _canvas.Width - ViewX) : Math.Min(Math.Max(xStart + 4, w - 4), _canvas.Width - ViewX);
            if (xEnd <= xStart) return 0;
            byte[] canvasBytes = ExtractPixels(_canvas, out int canStride);

            int yStep = Math.Max(6, (bottom - top) / 28);
            int xStep = Math.Max(6, (xEnd - xStart) / 90);
            int bestCandY = -1;
            double bestErr = 7.5;

            for (int candY = minCandY; candY <= maxCandY; candY += 2)
            {
                double err = 0;
                int samples = 0;
                bool pruned = false;
                for (int y = top; y < bottom; y += yStep)
                {
                    int cy = candY + y;
                    for (int x = xStart; x < xEnd; x += xStep)
                    {
                        int iCan = cy * canStride + (ViewX + x) * 4;
                        int iCur = y * curStride + x * 4;
                        err += Math.Abs(canvasBytes[iCan] - curBytes[iCur])
                             + Math.Abs(canvasBytes[iCan + 1] - curBytes[iCur + 1])
                             + Math.Abs(canvasBytes[iCan + 2] - curBytes[iCur + 2]);
                        samples++;
                    }
                    if ((err / Math.Max(1, samples)) > bestErr * 1.45)
                    {
                        pruned = true;
                        break;
                    }
                }
                if (pruned || samples == 0) continue;
                double avg = err / samples;
                if (avg < bestErr)
                {
                    bestErr = avg;
                    bestCandY = candY;
                }
            }

            if (bestCandY < 0) return 0;

            // Fine 1px refinement around bestCandY
            int refineStart = Math.Max(minCandY, bestCandY - 2);
            int refineEnd = Math.Min(maxCandY, bestCandY + 2);
            int finalY = bestCandY;
            for (int cyBase = refineStart; cyBase <= refineEnd; cyBase++)
            {
                double err = 0;
                int samples = 0;
                for (int y = top; y < bottom; y += Math.Max(4, yStep / 2))
                {
                    int cy = cyBase + y;
                    for (int x = xStart; x < xEnd; x += xStep)
                    {
                        int iCan = cy * canStride + (ViewX + x) * 4;
                        int iCur = y * curStride + x * 4;
                        err += Math.Abs(canvasBytes[iCan] - curBytes[iCur])
                             + Math.Abs(canvasBytes[iCan + 1] - curBytes[iCur + 1])
                             + Math.Abs(canvasBytes[iCan + 2] - curBytes[iCur + 2]);
                        samples++;
                    }
                }
                double avg = err / Math.Max(1, samples);
                if (avg < bestErr)
                {
                    bestErr = avg;
                    finalY = cyBase;
                }
            }

            int shift = finalY - ViewY;
            return Math.Abs(shift) >= 1 ? shift : 0;
        }

        public static bool HasSignificantVisualChange(Bitmap a, Bitmap b)
        {
            if (a == null || b == null || a.Size != b.Size) return true;
            var bd1 = a.LockBits(new Rectangle(0, 0, a.Width, a.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bd2 = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int w = a.Width, h = a.Height;
                int s1 = Math.Abs(bd1.Stride), s2 = Math.Abs(bd2.Stride);
                int rowBytes = Math.Min(s1, s2);
                byte[] r1 = new byte[rowBytes];
                byte[] r2 = new byte[rowBytes];
                int xEnd = Math.Max(5, w - 4);
                int yEnd = Math.Max(5, h - 4);
                int diffs = 0;

                for (int y = 4; y < yEnd; y += 6)
                {
                    Marshal.Copy(IntPtr.Add(bd1.Scan0, y * bd1.Stride), r1, 0, rowBytes);
                    Marshal.Copy(IntPtr.Add(bd2.Scan0, y * bd2.Stride), r2, 0, rowBytes);
                    for (int x = 4; x < xEnd; x += 6)
                    {
                        int idx = x * 4;
                        int d = Math.Abs(r1[idx] - r2[idx]) + Math.Abs(r1[idx + 1] - r2[idx + 1]) + Math.Abs(r1[idx + 2] - r2[idx + 2]);
                        if (d > 16 && ++diffs >= 8)
                            return true;
                    }
                }
                return false;
            }
            finally
            {
                a.UnlockBits(bd1);
                b.UnlockBits(bd2);
            }
        }

        private static double ComputeHorizVariance(byte[] buf, int stride, int y0, int sampleH, int xStart, int xEnd)
        {
            long sum = 0;
            int cnt = 0;
            for (int dy = 0; dy < sampleH; dy += 2)
            {
                int y = y0 + dy;
                for (int x = xStart; x < xEnd; x += 8)
                {
                    int idx = y * stride + x * 4;
                    sum += buf[idx] + buf[idx + 1] + buf[idx + 2];
                    cnt++;
                }
            }
            if (cnt < 4) return 0;
            double mean = (double)sum / cnt;
            double dev = 0;
            for (int dy = 0; dy < sampleH; dy += 2)
            {
                int y = y0 + dy;
                for (int x = xStart; x < xEnd; x += 8)
                {
                    int idx = y * stride + x * 4;
                    double l = buf[idx] + buf[idx + 1] + buf[idx + 2];
                    dev += Math.Abs(l - mean);
                }
            }
            return (dev / cnt) / 3.0;
        }

        private int DetectHorizontalShift(byte[] prev, int pStride, byte[] cur, int cStride, int w, int h, int hintSignX)
        {
            int left = _stickyLeftW;
            int right = w - _stickyRightW;
            int bodyW = Math.Max(40, right - left);
            int sampleW = Math.Min(36, Math.Max(14, bodyW / 7));
            int yStart = Math.Max(_stickyHeaderH + 8, (int)(h * 0.16));
            int yEnd = Math.Min(h - _stickyFooterH - 8, (int)(h * 0.84));
            if (yEnd <= yStart + 8) { yStart = 8; yEnd = Math.Max(16, h - 8); }

            if (hintSignX >= 0)
            {
                int[] anchors = { 80, 56, 36, 92 };
                foreach (int pct in anchors)
                {
                    int anchorX = pct >= 92
                        ? Math.Max(left + 2, right - sampleW - 2)
                        : left + Math.Max(2, (int)(bodyW * (pct / 100.0)) - sampleW);
                    if (anchorX + sampleW > right || anchorX - 1 < left) continue;
                    if (ComputeVertVariance(prev, pStride, anchorX, sampleW, yStart, yEnd) < 5.0 && pct != 56)
                        continue;

                    int maxX = anchorX - 4;
                    var (bestX, bestErr) = MatchVerticalStrip(prev, pStride, cur, cStride, anchorX, sampleW, left, maxX, yStart, yEnd);
                    if (bestX != -1 && bestErr < 20.0)
                    {
                        int dx = anchorX - bestX;
                        int secOff = Math.Min(sampleW + 10, bestX - left);
                        if (secOff >= 8 && EvalVertErr(prev, pStride, cur, cStride, anchorX - secOff, bestX - secOff, sampleW, yStart, yEnd) > 28.0)
                            continue;
                        return dx;
                    }
                }
            }

            if (hintSignX <= 0)
            {
                int[] anchors = { 20, 44, 66, 8 };
                foreach (int pct in anchors)
                {
                    int anchorX = left + Math.Max(2, (int)(bodyW * (pct / 100.0)));
                    if (anchorX + sampleW > right) continue;
                    if (ComputeVertVariance(prev, pStride, anchorX, sampleW, yStart, yEnd) < 5.0 && pct != 44)
                        continue;

                    int minX = anchorX + 1;
                    int maxX = right - sampleW;
                    if (minX > maxX) continue;
                    var (bestX, bestErr) = MatchVerticalStrip(prev, pStride, cur, cStride, anchorX, sampleW, minX, maxX, yStart, yEnd);
                    if (bestX != -1 && bestErr < 20.0)
                    {
                        int dxLeft = bestX - anchorX;
                        int secOff = Math.Min(sampleW + 10, (right - sampleW) - bestX);
                        if (secOff >= 8 && EvalVertErr(prev, pStride, cur, cStride, anchorX + secOff, bestX + secOff, sampleW, yStart, yEnd) > 28.0)
                            continue;
                        return -dxLeft;
                    }
                }
            }

            return 0;
        }

        private static double ComputeVertVariance(byte[] buf, int stride, int x0, int sampleW, int yStart, int yEnd)
        {
            long sum = 0;
            int cnt = 0;
            for (int dx = 0; dx < sampleW; dx += 2)
            {
                int x = x0 + dx;
                for (int y = yStart; y < yEnd; y += 8)
                {
                    int idx = y * stride + x * 4;
                    sum += buf[idx] + buf[idx + 1] + buf[idx + 2];
                    cnt++;
                }
            }
            if (cnt < 4) return 0;
            double mean = (double)sum / cnt;
            double dev = 0;
            for (int dx = 0; dx < sampleW; dx += 2)
            {
                int x = x0 + dx;
                for (int y = yStart; y < yEnd; y += 8)
                {
                    int idx = y * stride + x * 4;
                    double l = buf[idx] + buf[idx + 1] + buf[idx + 2];
                    dev += Math.Abs(l - mean);
                }
            }
            return (dev / cnt) / 3.0;
        }

        private static void DetectVerticalStickyBands(byte[] d0, int s0, byte[] d1, int s1, int w, int h, out int headerH, out int footerH)
        {
            int xStart = w > 60 ? (int)(w * 0.12) : 4;
            int xEnd = w > 60 ? (int)(w * 0.84) : Math.Max(xStart + 4, w - 4);
            headerH = 0;
            int maxHeader = (int)(h * 0.28);
            for (int y = 0; y < maxHeader; y++)
            {
                if (ComputeRowDiff(d0, s0, d1, s1, y, xStart, xEnd) < 10.0)
                {
                    headerH = y + 1;
                }
                else
                {
                    bool keepsMatching = (y + 2 < maxHeader) &&
                        (ComputeRowDiff(d0, s0, d1, s1, y + 1, xStart, xEnd) < 10.0 ||
                         ComputeRowDiff(d0, s0, d1, s1, y + 2, xStart, xEnd) < 10.0);
                    if (!keepsMatching) break;
                }
            }
            if (headerH > h * 0.28 || (h - headerH) < 160) headerH = 0;

            footerH = 0;
            int minFooter = (int)(h * 0.78);
            for (int y = h - 1; y >= minFooter; y--)
            {
                if (ComputeRowDiff(d0, s0, d1, s1, y, xStart, xEnd) < 10.0) footerH++;
                else break;
            }
            if (footerH > h * 0.20) footerH = 0;
        }

        private static void DetectHorizontalStickyBands(byte[] d0, int s0, byte[] d1, int s1, int w, int h, out int leftW, out int rightW)
        {
            int yStart = h > 60 ? (int)(h * 0.15) : 4;
            int yEnd = h > 60 ? (int)(h * 0.85) : Math.Max(yStart + 4, h - 4);
            leftW = 0;
            int maxL = (int)(w * 0.22);
            for (int x = 0; x < maxL; x++)
            {
                if (ComputeColDiff(d0, s0, d1, s1, x, yStart, yEnd) < 10.0) leftW = x + 1;
                else break;
            }
            if (leftW > w * 0.20) leftW = 0;

            rightW = 0;
            int minR = (int)(w * 0.78);
            for (int x = w - 1; x >= minR; x--)
            {
                if (ComputeColDiff(d0, s0, d1, s1, x, yStart, yEnd) < 10.0) rightW++;
                else break;
            }
            if (rightW > w * 0.20) rightW = 0;
        }

        private static void SanitizeFloatingWidgets(Bitmap stripSlice, byte[] prev, int pStride, byte[] cur, int cStride, int w, int h, int srcY, int srcH, int deltaY)
        {
            if (Math.Abs(deltaY) < 8 || prev == null || cur == null || stripSlice == null) return;

            int step = 2;
            int gridW = w / step;
            int gridH = srcH / step;
            if (gridW <= 4 || gridH <= 4) return;

            bool[,] staticGrid = new bool[gridW, gridH];
            int staticCount = 0;

            for (int gy = 0; gy < gridH; gy++)
            {
                int y = srcY + gy * step;
                if (y < 0 || y >= h) continue;

                for (int gx = 0; gx < gridW; gx++)
                {
                    int x = gx * step;
                    int curIdx = y * cStride + x * 4;
                    int prevIdx = y * pStride + x * 4;

                    int diffStationary = Math.Abs(cur[curIdx] - prev[prevIdx]) +
                                         Math.Abs(cur[curIdx + 1] - prev[prevIdx + 1]) +
                                         Math.Abs(cur[curIdx + 2] - prev[prevIdx + 2]);

                    // If pixel didn't move between frames despite scrolling
                    if (diffStationary < 15)
                    {
                        staticGrid[gx, gy] = true;
                        staticCount++;
                    }
                }
            }

            if (staticCount < 6) return;

            bool[,] visited = new bool[gridW, gridH];
            using var g = Graphics.FromImage(stripSlice);

            for (int gy = 0; gy < gridH; gy++)
            {
                for (int gx = 0; gx < gridW; gx++)
                {
                    if (staticGrid[gx, gy] && !visited[gx, gy])
                    {
                        int minGx = gx, maxGx = gx, minGy = gy, maxGy = gy;
                        var queue = new Queue<Point>();
                        queue.Enqueue(new Point(gx, gy));
                        visited[gx, gy] = true;
                        int clusterPixels = 0;

                        while (queue.Count > 0)
                        {
                            Point p = queue.Dequeue();
                            clusterPixels++;
                            if (p.X < minGx) minGx = p.X;
                            if (p.X > maxGx) maxGx = p.X;
                            if (p.Y < minGy) minGy = p.Y;
                            if (p.Y > maxGy) maxGy = p.Y;

                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    if (dx == 0 && dy == 0) continue;
                                    int nx = p.X + dx, ny = p.Y + dy;
                                    if (nx >= 0 && nx < gridW && ny >= 0 && ny < gridH &&
                                        staticGrid[nx, ny] && !visited[nx, ny])
                                    {
                                        visited[nx, ny] = true;
                                        queue.Enqueue(new Point(nx, ny));
                                    }
                                }
                            }
                        }

                        int boxW = (maxGx - minGx + 1) * step;
                        int boxH = (maxGy - minGy + 1) * step;

                        // Only sanitize localized floating widgets (not full-width headers/banners or whole screen)
                        // Floating buttons/badges are typically 12px to 240px wide and high
                        if (clusterPixels >= 8 && boxW >= 12 && boxH >= 12 && boxW < w * 0.40 && boxH < Math.Max(280, h * 0.40))
                        {
                            int boxX = Math.Max(0, minGx * step - 2);
                            int boxY = Math.Max(0, minGy * step - 2);
                            boxW = Math.Min(w - boxX, boxW + 4);
                            boxH = Math.Min(srcH - boxY, boxH + 4);

                            // Sample surrounding background color
                            int sampleX = boxX >= 10 ? boxX - 6 : Math.Min(w - 2, boxX + boxW + 6);
                            int sampleY = Math.Max(0, Math.Min(srcH - 1, boxY + boxH / 2));
                            Color bgCol = stripSlice.GetPixel(sampleX, sampleY);

                            // Check if cluster actually contains graphic content distinct from background
                            bool hasGraphicContent = false;
                            for (int cy = boxY; cy < boxY + boxH; cy += Math.Max(1, boxH / 6))
                            {
                                for (int cx = boxX; cx < boxX + boxW; cx += Math.Max(1, boxW / 6))
                                {
                                    Color c = stripSlice.GetPixel(cx, cy);
                                    if (Math.Abs(c.R - bgCol.R) + Math.Abs(c.G - bgCol.G) + Math.Abs(c.B - bgCol.B) > 28)
                                    {
                                        hasGraphicContent = true;
                                        break;
                                    }
                                }
                                if (hasGraphicContent) break;
                            }

                            if (hasGraphicContent)
                            {
                                using var br = new SolidBrush(bgCol);
                                g.FillRectangle(br, boxX, boxY, boxW, boxH);
                            }
                        }
                    }
                }
            }
        }

        private static List<(int BestY, double BestErr)> MatchHorizontalStripCandidates(
            byte[] prev, int pStride, byte[] cur, int cStride,
            int anchorY, int sampleH, int minY, int maxY, int xStart, int xEnd, bool scanDescending = false)
        {
            const int MaxCandidates = 6;
            const double CutoffErr = 24.0;
            var cands = new List<(int Y, double Err)>(MaxCandidates);
            double pruneCeiling = CutoffErr;

            int step = scanDescending ? -1 : 1;
            for (int cy = scanDescending ? maxY : minY; scanDescending ? (cy >= minY) : (cy <= maxY); cy += step)
            {
                double err = 0;
                int samples = 0;
                bool pruned = false;
                for (int dy = 0; dy < sampleH; dy += 2)
                {
                    int yP = anchorY + dy;
                    int yC = cy + dy;
                    for (int x = xStart; x < xEnd; x += 6)
                    {
                        int iP = yP * pStride + x * 4;
                        int iC = yC * cStride + x * 4;
                        err += Math.Abs(prev[iP] - cur[iC]) + Math.Abs(prev[iP + 1] - cur[iC + 1]) + Math.Abs(prev[iP + 2] - cur[iC + 2]);
                        samples++;
                    }
                    if (dy >= 4 && (err / Math.Max(1, samples)) > pruneCeiling)
                    {
                        pruned = true;
                        break;
                    }
                }
                if (pruned) continue;

                double avg = err / Math.Max(1, samples);
                if (avg > CutoffErr) continue;

                int mergeIdx = -1;
                for (int i = 0; i < cands.Count; i++)
                {
                    if (Math.Abs(cands[i].Y - cy) < 12)
                    {
                        mergeIdx = i;
                        break;
                    }
                }
                if (mergeIdx >= 0)
                {
                    if (avg < cands[mergeIdx].Err)
                        cands[mergeIdx] = (cy, avg);
                }
                else if (cands.Count < MaxCandidates)
                {
                    cands.Add((cy, avg));
                }
                else
                {
                    int worstIdx = 0;
                    for (int i = 1; i < cands.Count; i++)
                    {
                        if (cands[i].Err > cands[worstIdx].Err) worstIdx = i;
                    }
                    if (avg < cands[worstIdx].Err)
                        cands[worstIdx] = (cy, avg);
                }

                if (cands.Count >= MaxCandidates)
                {
                    double worst = 0;
                    for (int i = 0; i < cands.Count; i++)
                        if (cands[i].Err > worst) worst = cands[i].Err;
                    pruneCeiling = Math.Max(4.0, Math.Min(CutoffErr, worst + 2.0));
                }
            }

            cands.Sort((a, b) => a.Err.CompareTo(b.Err));
            return cands;
        }

        private static (int BestX, double BestErr) MatchVerticalStrip(
            byte[] prev, int pStride, byte[] cur, int cStride,
            int anchorX, int sampleW, int minX, int maxX, int yStart, int yEnd, bool scanDescending = false)
        {
            int bestX = -1;
            double bestErr = double.MaxValue;
            int step = scanDescending ? -1 : 1;
            for (int cx = scanDescending ? maxX : minX; scanDescending ? (cx >= minX) : (cx <= maxX); cx += step)
            {
                double err = 0;
                int samples = 0;
                for (int dx = 0; dx < sampleW; dx += 2)
                {
                    int xP = anchorX + dx;
                    int xC = cx + dx;
                    for (int y = yStart; y < yEnd; y += 6)
                    {
                        int iP = y * pStride + xP * 4;
                        int iC = y * cStride + xC * 4;
                        err += Math.Abs(prev[iP] - cur[iC]) + Math.Abs(prev[iP + 1] - cur[iC + 1]) + Math.Abs(prev[iP + 2] - cur[iC + 2]);
                        samples++;
                    }
                    if (err / Math.Max(1, samples) > bestErr) break;
                }
                double avg = err / Math.Max(1, samples);
                if (avg < bestErr) { bestErr = avg; bestX = cx; }
            }
            return (bestX, bestErr);
        }

        private static double EvalVertErr(byte[] prev, int pStride, byte[] cur, int cStride, int xPStart, int xCStart, int sampleW, int yStart, int yEnd)
        {
            double err = 0;
            int samples = 0;
            for (int dx = 0; dx < sampleW; dx += 2)
            {
                int xP = xPStart + dx, xC = xCStart + dx;
                for (int y = yStart; y < yEnd; y += 6)
                {
                    int iP = y * pStride + xP * 4, iC = y * cStride + xC * 4;
                    err += Math.Abs(prev[iP] - cur[iC]) + Math.Abs(prev[iP + 1] - cur[iC + 1]) + Math.Abs(prev[iP + 2] - cur[iC + 2]);
                    samples++;
                }
            }
            return err / Math.Max(1, samples);
        }

        private static double ComputeRowDiff(byte[] d0, int s0, byte[] d1, int s1, int y, int xStart, int xEnd)
        {
            double diff = 0;
            int cnt = 0;
            for (int x = xStart; x < xEnd; x += 4)
            {
                int i0 = y * s0 + x * 4, i1 = y * s1 + x * 4;
                diff += Math.Abs(d0[i0] - d1[i1]) + Math.Abs(d0[i0 + 1] - d1[i1 + 1]) + Math.Abs(d0[i0 + 2] - d1[i1 + 2]);
                cnt++;
            }
            return diff / Math.Max(1, cnt);
        }

        private static double ComputeColDiff(byte[] d0, int s0, byte[] d1, int s1, int x, int yStart, int yEnd)
        {
            double diff = 0;
            int cnt = 0;
            for (int y = yStart; y < yEnd; y += 4)
            {
                int i0 = y * s0 + x * 4, i1 = y * s1 + x * 4;
                diff += Math.Abs(d0[i0] - d1[i1]) + Math.Abs(d0[i0 + 1] - d1[i1 + 1]) + Math.Abs(d0[i0 + 2] - d1[i1 + 2]);
                cnt++;
            }
            return diff / Math.Max(1, cnt);
        }

        private static bool FramesNearlyIdentical(byte[] d1, int s1, byte[] d2, int s2, int w, int h)
        {
            int xStart = (int)(w * 0.12), xEnd = (int)(w * 0.85);
            int diffs = 0;
            for (int y = 8; y < h - 8; y += 4)
            {
                for (int x = xStart; x < xEnd; x += 5)
                {
                    int i1 = y * s1 + x * 4, i2 = y * s2 + x * 4;
                    if (Math.Abs(d1[i1] - d2[i2]) + Math.Abs(d1[i1 + 1] - d2[i2 + 1]) + Math.Abs(d1[i1 + 2] - d2[i2 + 2]) > 18)
                    {
                        if (++diffs > 18) return false;
                    }
                }
            }
            return true;
        }

        private static Bitmap EnsureSizeAnd32Argb(Bitmap src, int w, int h)
        {
            if (src.Width == w && src.Height == h && src.PixelFormat == PixelFormat.Format32bppArgb)
                return (Bitmap)src.Clone();
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(0, 0, Math.Min(w, src.Width), Math.Min(h, src.Height)), GraphicsUnit.Pixel);
            }
            return bmp;
        }

        private static byte[] ExtractPixels(Bitmap bmp, out int stride)
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(bd.Stride);
                byte[] data = new byte[stride * bmp.Height];
                Marshal.Copy(bd.Scan0, data, 0, data.Length);
                return data;
            }
            finally { bmp.UnlockBits(bd); }
        }

        public void Dispose()
        {
            _canvas?.Dispose();
            _lastRaw?.Dispose();
        }
    }
}

