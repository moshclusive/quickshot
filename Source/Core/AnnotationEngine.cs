using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace QuickShot.Core
{
    public enum AnnotationType
    {
        Rect,
        Circle,
        Line,
        Arrow,
        Brush,
        Highlighter,
        Number,
        Text,
        Blur,
        Spotlight,
        Zoom
    }

    public class AnnotationItem
    {
        public AnnotationType Type { get; set; }
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int Number { get; set; }
        public float Radius { get; set; } = 15f;
        public string Text { get; set; }
        public float FontSize { get; set; } = 20f;
        public float Width { get; set; } = 120f;
        public float Height { get; set; } = 28f;
        public Color Color { get; set; } = Color.FromArgb(255, 59, 48);
        public float StrokeWidth { get; set; } = 2f;
        public List<PointF> Points { get; set; } = new List<PointF>();

        // Exact document coordinates for zero-drift scrolling annotations
        public float DocX1 { get; set; }
        public float DocY1 { get; set; }
        public float DocX2 { get; set; }
        public float DocY2 { get; set; }
        public float DocX { get; set; }
        public float DocY { get; set; }
        public List<PointF> DocPoints { get; set; } = new List<PointF>();
        public bool HasDocCoords { get; set; }

        public void EnsureDocCoords(float viewX, float viewY)
        {
            if (!HasDocCoords)
            {
                DocX1 = X1 + viewX;
                DocY1 = Y1 + viewY;
                DocX2 = X2 + viewX;
                DocY2 = Y2 + viewY;
                DocX = X + viewX;
                DocY = Y + viewY;
                DocPoints = new List<PointF>();
                if (Points != null)
                {
                    foreach (var p in Points)
                        DocPoints.Add(new PointF(p.X + viewX, p.Y + viewY));
                }
                HasDocCoords = true;
            }
        }

        public void ApplyDocShift(float prependX, float prependY)
        {
            if (!HasDocCoords) return;
            DocX1 += prependX; DocX2 += prependX; DocX += prependX;
            DocY1 += prependY; DocY2 += prependY; DocY += prependY;
            if (DocPoints != null)
            {
                for (int i = 0; i < DocPoints.Count; i++)
                    DocPoints[i] = new PointF(DocPoints[i].X + prependX, DocPoints[i].Y + prependY);
            }
        }

        public void SyncToViewport(float viewX, float viewY)
        {
            if (!HasDocCoords) return;
            X1 = DocX1 - viewX;
            Y1 = DocY1 - viewY;
            X2 = DocX2 - viewX;
            Y2 = DocY2 - viewY;
            X = DocX - viewX;
            Y = DocY - viewY;
            if (DocPoints != null && DocPoints.Count > 0)
            {
                Points = new List<PointF>(DocPoints.Count);
                for (int i = 0; i < DocPoints.Count; i++)
                    Points.Add(new PointF(DocPoints[i].X - viewX, DocPoints[i].Y - viewY));
            }
        }

        public void Translate(float dx, float dy)
        {
            if (Type == AnnotationType.Number || Type == AnnotationType.Text)
            {
                X += dx;
                Y += dy;
            }
            else if (Type == AnnotationType.Brush || Type == AnnotationType.Highlighter)
            {
                for (int i = 0; i < Points.Count; i++)
                    Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
            }
            else
            {
                X1 += dx; X2 += dx;
                Y1 += dy; Y2 += dy;
            }

            if (HasDocCoords)
            {
                if (Type == AnnotationType.Number || Type == AnnotationType.Text)
                {
                    DocX += dx;
                    DocY += dy;
                }
                else if (Type == AnnotationType.Brush || Type == AnnotationType.Highlighter)
                {
                    if (DocPoints != null)
                    {
                        for (int i = 0; i < DocPoints.Count; i++)
                            DocPoints[i] = new PointF(DocPoints[i].X + dx, DocPoints[i].Y + dy);
                    }
                }
                else
                {
                    DocX1 += dx; DocX2 += dx;
                    DocY1 += dy; DocY2 += dy;
                }
            }
        }

        public AnnotationItem Clone()
        {
            return new AnnotationItem
            {
                Type = Type,
                X1 = X1,
                Y1 = Y1,
                X2 = X2,
                Y2 = Y2,
                X = X,
                Y = Y,
                Number = Number,
                Radius = Radius,
                Text = Text,
                FontSize = FontSize,
                Width = Width,
                Height = Height,
                Color = Color,
                StrokeWidth = StrokeWidth,
                Points = new List<PointF>(Points),
                DocX1 = DocX1,
                DocY1 = DocY1,
                DocX2 = DocX2,
                DocY2 = DocY2,
                DocX = DocX,
                DocY = DocY,
                DocPoints = DocPoints != null ? new List<PointF>(DocPoints) : new List<PointF>(),
                HasDocCoords = HasDocCoords
            };
        }
    }

    public static class AnnotationRenderer
    {
        public static int FindAnnotationAt(IList<AnnotationItem> list, float px, float py)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                switch (a.Type)
                {
                    case AnnotationType.Rect:
                    case AnnotationType.Circle:
                    case AnnotationType.Spotlight:
                    case AnnotationType.Blur:
                    case AnnotationType.Zoom:
                        float minX = Math.Min(a.X1, a.X2), maxX = Math.Max(a.X1, a.X2);
                        float minY = Math.Min(a.Y1, a.Y2), maxY = Math.Max(a.Y1, a.Y2);
                        if (px >= minX - 4 && px <= maxX + 4 && py >= minY - 4 && py <= maxY + 4) return i;
                        break;
                    case AnnotationType.Number:
                        if (Hypot(a.X - px, a.Y - py) <= a.Radius + 5) return i;
                        break;
                    case AnnotationType.Text:
                        if (px >= a.X - 6 && px <= a.X + Math.Max(40, a.Width) + 6 && py >= a.Y - 4 && py <= a.Y + Math.Max(20, a.Height) + 6)
                            return i;
                        break;
                    case AnnotationType.Line:
                    case AnnotationType.Arrow:
                        if (DistanceToSegment(px, py, a.X1, a.Y1, a.X2, a.Y2) <= Math.Max(8f, a.StrokeWidth + 5f))
                            return i;
                        break;
                    case AnnotationType.Brush:
                    case AnnotationType.Highlighter:
                        float tol = Math.Max(8f, a.StrokeWidth * 0.65f + 6f);
                        for (int k = 0; k < a.Points.Count; k++)
                        {
                            if (Hypot(a.Points[k].X - px, a.Points[k].Y - py) <= tol) return i;
                        }
                        break;
                }
            }
            return -1;
        }

        private static float Hypot(float dx, float dy) => (float)Math.Sqrt(dx * dx + dy * dy);

        private static float DistanceToSegment(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len2 = dx * dx + dy * dy;
            if (len2 < 0.001f) return Hypot(px - x1, py - y1);
            float t = Math.Max(0f, Math.Min(1f, ((px - x1) * dx + (py - y1) * dy) / len2));
            return Hypot(px - (x1 + t * dx), py - (y1 + t * dy));
        }

        public static void RenderAnnotations(Graphics g, IList<AnnotationItem> list, Bitmap bgSource, int selectedIndex, PointF originOffset)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Render unified Spotlight cutouts first so multiple spotlights never stack darker
            var spotlights = list.Where(it => it.Type == AnnotationType.Spotlight &&
                                              Math.Abs(it.X2 - it.X1) > 4 && Math.Abs(it.Y2 - it.Y1) > 4).ToList();
            if (spotlights.Count > 0 && bgSource != null)
            {
                DrawUnifiedSpotlights(g, spotlights, bgSource.Width, bgSource.Height, originOffset);
            }

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Type == AnnotationType.Spotlight) continue;

                DrawSingleAnnotation(g, item, bgSource, originOffset);

                if (i == selectedIndex)
                {
                    DrawSelectionOutline(g, item, originOffset);
                }
            }
        }

        private static void DrawSelectionOutline(Graphics g, AnnotationItem item, PointF offset)
        {
            RectangleF bounds = RectangleF.Empty;
            if (item.Type == AnnotationType.Number)
                bounds = new RectangleF(item.X - item.Radius - 4, item.Y - item.Radius - 4, (item.Radius + 4) * 2, (item.Radius + 4) * 2);
            else if (item.Type == AnnotationType.Text)
                bounds = new RectangleF(item.X - 4, item.Y - 4, item.Width + 8, item.Height + 8);
            else if (item.Type == AnnotationType.Brush || item.Type == AnnotationType.Highlighter)
            {
                if (item.Points.Count == 0) return;
                float minX = item.Points.Min(p => p.X), maxX = item.Points.Max(p => p.X);
                float minY = item.Points.Min(p => p.Y), maxY = item.Points.Max(p => p.Y);
                bounds = new RectangleF(minX - 6, minY - 6, maxX - minX + 12, maxY - minY + 12);
            }
            else
            {
                bounds = new RectangleF(
                    Math.Min(item.X1, item.X2) - 4, Math.Min(item.Y1, item.Y2) - 4,
                    Math.Abs(item.X2 - item.X1) + 8, Math.Abs(item.Y2 - item.Y1) + 8);
            }

            bounds.Offset(-offset.X, -offset.Y);
            using var pen = new Pen(Theme.Brand, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        private static void DrawUnifiedSpotlights(Graphics g, List<AnnotationItem> spotlights, int width, int height, PointF offset)
        {
            using var path = new GraphicsPath(FillMode.Alternate);
            path.AddRectangle(new RectangleF(-offset.X, -offset.Y, width, height));
            foreach (var sp in spotlights)
            {
                float sx = Math.Min(sp.X1, sp.X2) - offset.X;
                float sy = Math.Min(sp.Y1, sp.Y2) - offset.Y;
                float sw = Math.Abs(sp.X2 - sp.X1);
                float sh = Math.Abs(sp.Y2 - sp.Y1);
                path.AddRectangle(new RectangleF(sx, sy, sw, sh));
            }
            using (var dimBrush = new SolidBrush(Color.FromArgb(122, 0, 0, 0)))
                g.FillPath(dimBrush, path);

            foreach (var sp in spotlights)
            {
                float sx = Math.Min(sp.X1, sp.X2) - offset.X;
                float sy = Math.Min(sp.Y1, sp.Y2) - offset.Y;
                float sw = Math.Abs(sp.X2 - sp.X1);
                float sh = Math.Abs(sp.Y2 - sp.Y1);
                using var pen = new Pen(sp.Color, Math.Max(1.5f, sp.StrokeWidth));
                g.DrawRectangle(pen, sx, sy, sw, sh);
            }
        }

        private static void DrawSingleAnnotation(Graphics g, AnnotationItem item, Bitmap bgSource, PointF offset)
        {
            float ox = -offset.X, oy = -offset.Y;

            switch (item.Type)
            {
                case AnnotationType.Brush:
                {
                    if (item.Points.Count == 0) break;
                    using var pen = new Pen(item.Color, item.StrokeWidth)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };
                    if (item.Points.Count == 1)
                    {
                        float r = item.StrokeWidth / 2f;
                        using var br = new SolidBrush(item.Color);
                        g.FillEllipse(br, item.Points[0].X + ox - r, item.Points[0].Y + oy - r, r * 2, r * 2);
                    }
                    else
                    {
                        var pts = item.Points.Select(p => new PointF(p.X + ox, p.Y + oy)).ToArray();
                        g.DrawLines(pen, pts);
                    }
                    break;
                }
                case AnnotationType.Highlighter:
                {
                    DrawShareXHighlighter(g, item, bgSource, offset);
                    break;
                }
                case AnnotationType.Rect:
                {
                    float rx = Math.Min(item.X1, item.X2) + ox;
                    float ry = Math.Min(item.Y1, item.Y2) + oy;
                    float rw = Math.Abs(item.X2 - item.X1);
                    float rh = Math.Abs(item.Y2 - item.Y1);
                    if (rw > 0 && rh > 0)
                    {
                        using var pen = new Pen(item.Color, item.StrokeWidth) { LineJoin = LineJoin.Miter };
                        g.DrawRectangle(pen, rx, ry, rw, rh);
                    }
                    break;
                }
                case AnnotationType.Circle:
                {
                    float rx = Math.Min(item.X1, item.X2) + ox;
                    float ry = Math.Min(item.Y1, item.Y2) + oy;
                    float rw = Math.Abs(item.X2 - item.X1);
                    float rh = Math.Abs(item.Y2 - item.Y1);
                    if (rw > 2 && rh > 2)
                    {
                        using var pen = new Pen(item.Color, item.StrokeWidth);
                        g.DrawEllipse(pen, rx, ry, rw, rh);
                    }
                    break;
                }
                case AnnotationType.Line:
                {
                    using var pen = new Pen(item.Color, item.StrokeWidth)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round
                    };
                    g.DrawLine(pen, item.X1 + ox, item.Y1 + oy, item.X2 + ox, item.Y2 + oy);
                    break;
                }
                case AnnotationType.Arrow:
                {
                    DrawPerfectArrow(g, item.X1 + ox, item.Y1 + oy, item.X2 + ox, item.Y2 + oy, item.Color, item.StrokeWidth);
                    break;
                }
                case AnnotationType.Number:
                {
                    float r = item.Radius;
                    float cx = item.X + ox, cy = item.Y + oy;
                    using (var br = new SolidBrush(item.Color))
                        g.FillEllipse(br, cx - r, cy - r, r * 2, r * 2);
                    using (var font = Theme.Font(Math.Max(9f, r * 0.85f), FontStyle.Bold, GraphicsUnit.Point))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString(item.Number.ToString(), font, Brushes.White, new RectangleF(cx - r, cy - r + 1, r * 2, r * 2), sf);
                    }
                    break;
                }
                case AnnotationType.Text:
                {
                    if (string.IsNullOrEmpty(item.Text)) break;
                    using var font = Theme.Font(Math.Max(11f, item.FontSize * 0.75f), FontStyle.Bold, GraphicsUnit.Point);
                    using var br = new SolidBrush(item.Color);
                    g.DrawString(item.Text, font, br, item.X + ox, item.Y + oy);
                    break;
                }
                case AnnotationType.Blur:
                {
                    if (bgSource == null) break;
                    int bx = (int)Math.Round(Math.Min(item.X1, item.X2));
                    int by = (int)Math.Round(Math.Min(item.Y1, item.Y2));
                    int bw = (int)Math.Round(Math.Abs(item.X2 - item.X1));
                    int bh = (int)Math.Round(Math.Abs(item.Y2 - item.Y1));
                    var srcRect = Rectangle.Intersect(new Rectangle(0, 0, bgSource.Width, bgSource.Height), new Rectangle(bx, by, bw, bh));
                    if (srcRect.Width > 2 && srcRect.Height > 2)
                    {
                        using var blurred = CreateFastBlurredRegion(bgSource, srcRect, 10);
                        g.DrawImageUnscaled(blurred, srcRect.X + (int)ox, srcRect.Y + (int)oy);
                    }
                    break;
                }
                case AnnotationType.Zoom:
                {
                    if (bgSource == null) break;
                    float zx = Math.Min(item.X1, item.X2);
                    float zy = Math.Min(item.Y1, item.Y2);
                    float zw = Math.Abs(item.X2 - item.X1);
                    float zh = Math.Abs(item.Y2 - item.Y1);
                    if (zw > 8 && zh > 8)
                    {
                        const float zoomFactor = 1.6f;
                        float srcW = zw / zoomFactor, srcH = zh / zoomFactor;
                        float srcX = zx + (zw - srcW) / 2f, srcY = zy + (zh - srcH) / 2f;
                        var prevInterp = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bgSource,
                            new RectangleF(zx + ox, zy + oy, zw, zh),
                            new RectangleF(srcX, srcY, srcW, srcH),
                            GraphicsUnit.Pixel);
                        g.InterpolationMode = prevInterp;
                        using var pen = new Pen(item.Color, Math.Max(2f, item.StrokeWidth));
                        g.DrawRectangle(pen, zx + ox, zy + oy, zw, zh);
                    }
                    break;
                }
            }
        }

        private static void DrawPerfectArrow(Graphics g, float x1, float y1, float x2, float y2, Color color, float strokeWidth)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = Hypot(dx, dy);
            if (len < 4f) return;

            float angle = (float)Math.Atan2(dy, dx);
            float headLen = Math.Min(len * 0.6f, Math.Max(16f, strokeWidth * 3.8f));
            float headWidth = headLen * 0.44f;
            float notchDepth = headLen * 0.72f;

            float shaftEndX = x2 - (float)Math.Cos(angle) * (notchDepth * 0.95f);
            float shaftEndY = y2 - (float)Math.Sin(angle) * (notchDepth * 0.95f);

            // Subtle drop shadow
            using (var shadowPen = new Pen(Color.FromArgb(95, 0, 0, 0), strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(shadowPen, x1 + 1.5f, y1 + 1.5f, shaftEndX + 1.5f, shaftEndY + 1.5f);

            using (var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, x1, y1, shaftEndX, shaftEndY);

            var state = g.Save();
            g.TranslateTransform(x2, y2);
            g.RotateTransform((float)(angle * 180.0 / Math.PI));

            var headPts = new[]
            {
                new PointF(0, 0),
                new PointF(-headLen, -headWidth),
                new PointF(-notchDepth, 0),
                new PointF(-headLen, headWidth)
            };
            using (var shBr = new SolidBrush(Color.FromArgb(95, 0, 0, 0)))
            {
                g.TranslateTransform(1.5f, 1.5f);
                g.FillPolygon(shBr, headPts);
                g.TranslateTransform(-1.5f, -1.5f);
            }
            using (var br = new SolidBrush(color))
                g.FillPolygon(br, headPts);

            g.Restore(state);
        }

        private static void DrawShareXHighlighter(Graphics g, AnnotationItem item, Bitmap bgSource, PointF offset)
        {
            if (item.Points.Count == 0) return;
            float strokeW = Math.Max(18f, item.StrokeWidth);
            Color c = item.Color;

            if (bgSource == null)
            {
                using var pen = new Pen(Color.FromArgb(90, c), strokeW) { StartCap = LineCap.Square, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                var pts = item.Points.Select(p => new PointF(p.X - offset.X, p.Y - offset.Y)).ToArray();
                if (pts.Length >= 2) g.DrawLines(pen, pts);
                return;
            }

            float minX = item.Points.Min(p => p.X), maxX = item.Points.Max(p => p.X);
            float minY = item.Points.Min(p => p.Y), maxY = item.Points.Max(p => p.Y);
            int pad = (int)Math.Ceiling(strokeW * 0.75f + 4f);
            int bx = Math.Max(0, (int)Math.Floor(minX - pad));
            int by = Math.Max(0, (int)Math.Floor(minY - pad));
            int bx2 = Math.Min(bgSource.Width, (int)Math.Ceiling(maxX + pad));
            int by2 = Math.Min(bgSource.Height, (int)Math.Ceiling(maxY + pad));
            int bw = bx2 - bx, bh = by2 - by;
            if (bw <= 0 || bh <= 0) return;

            // Ensure highlighter color has vibrant fluorescent saturation & brightness
            int hR = c.R, hG = c.G, hB = c.B;
            int maxCh = Math.Max(hR, Math.Max(hG, hB));
            if (maxCh > 0 && maxCh < 220)
            {
                float factor = 220f / maxCh;
                hR = Math.Min(255, (int)(hR * factor));
                hG = Math.Min(255, (int)(hG * factor));
                hB = Math.Min(255, (int)(hB * factor));
            }

            // Generate anti-aliased stroke alpha mask
            using var mask = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            using (var mg = Graphics.FromImage(mask))
            {
                mg.SmoothingMode = SmoothingMode.AntiAlias;
                mg.Clear(Color.Transparent);
                using var pen = new Pen(Color.White, strokeW) { StartCap = LineCap.Square, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                var pts = item.Points.Select(p => new PointF(p.X - bx, p.Y - by)).ToArray();
                if (pts.Length == 1) mg.DrawLine(pen, pts[0], new PointF(pts[0].X + 0.5f, pts[0].Y + 0.5f));
                else mg.DrawLines(pen, pts);
            }

            using var outBmp = bgSource.Clone(new Rectangle(bx, by, bw, bh), PixelFormat.Format32bppArgb);
            var bdOut = outBmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var bdMask = mask.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(bdOut.Stride) * bh;
            byte[] pOut = new byte[bytes];
            byte[] pMask = new byte[bytes];
            Marshal.Copy(bdOut.Scan0, pOut, 0, bytes);
            Marshal.Copy(bdMask.Scan0, pMask, 0, bytes);

            // True optical Multiply blend shader:
            // Formula: Target = (Orig * Tint) / 255
            // On white paper: page becomes glowing fluorescent highlighter, text stays razor-sharp pitch black!
            // On dark mode: white text becomes glowing highlighter color, dark background stays dark! Text is 100% readable!
            float maxIntensity = 0.88f;
            for (int y = 0; y < bh; y++)
            {
                int row = y * bdOut.Stride;
                for (int x = 0; x < bw; x++)
                {
                    int k = row + x * 4;
                    byte ma = pMask[k + 3];
                    if (ma == 0) continue;
                    float alpha = (ma / 255f) * maxIntensity;

                    byte origB = pOut[k];
                    byte origG = pOut[k + 1];
                    byte origR = pOut[k + 2];

                    int targetB = (origB * hB) / 255;
                    int targetG = (origG * hG) / 255;
                    int targetR = (origR * hR) / 255;

                    pOut[k] = (byte)(origB + (targetB - origB) * alpha);
                    pOut[k + 1] = (byte)(origG + (targetG - origG) * alpha);
                    pOut[k + 2] = (byte)(origR + (targetR - origR) * alpha);
                }
            }
            Marshal.Copy(pOut, 0, bdOut.Scan0, bytes);
            outBmp.UnlockBits(bdOut);
            mask.UnlockBits(bdMask);

            g.DrawImageUnscaled(outBmp, bx - (int)offset.X, by - (int)offset.Y);
        }

        private static Bitmap CreateFastBlurredRegion(Bitmap src, Rectangle region, int blockSize)
        {
            using var sub = src.Clone(region, PixelFormat.Format32bppArgb);
            int smallW = Math.Max(1, region.Width / blockSize);
            int smallH = Math.Max(1, region.Height / blockSize);
            using var small = new Bitmap(smallW, smallH);
            using (var sg = Graphics.FromImage(small))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sg.DrawImage(sub, 0, 0, smallW, smallH);
            }
            var blurred = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var bg = Graphics.FromImage(blurred))
            {
                bg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                bg.DrawImage(small, 0, 0, region.Width, region.Height);
            }
            return blurred;
        }
    }
}

