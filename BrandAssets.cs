using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FarmMacro
{
    public static class BrandAssets
    {
        public const string AppName = "Flow Macro";
        public const string Version = "1.5.0";

        internal const string SvgSource = @"<svg xmlns=""http://www.w3.org/2000/svg"" width=""256"" height=""256"" viewBox=""0 0 256 256"" role=""img"" aria-label=""Flow Macro"">
  <rect x=""1"" y=""1"" width=""254"" height=""254"" rx=""48"" fill=""#0e1422""/>
  <path d=""M204 166 C225 125 207 75 163 55 C117 34 66 55 47 99 C27 145 48 195 94 212 C128 225 165 217 190 192"" fill=""none"" stroke=""#8991ff"" stroke-width=""12"" stroke-linecap=""round"" stroke-linejoin=""round""/>
  <path d=""M171 191 L194 188 L190 211"" fill=""none"" stroke=""#8991ff"" stroke-width=""12"" stroke-linecap=""round"" stroke-linejoin=""round""/>
  <path d=""M88 70 L96 171 L117 149 L137 184 L156 173 L137 141 L166 137 Z"" fill=""#5ddeba""/>
</svg>
";

        // The caller owns the returned Icon; the resource stream is never kept open.
        public static Icon LoadIcon()
        {
            using (Stream stream = typeof(BrandAssets).Assembly.GetManifestResourceStream("FarmMacro.AppIcon"))
            {
                if (stream != null)
                {
                    using (Icon resource = new Icon(stream)) return (Icon)resource.Clone();
                }
            }
            using (Bitmap mark = (Bitmap)CreateMark(64))
            {
                IntPtr handle = mark.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        // Original code-native artwork. The caller owns the returned Image.
        public static Image CreateMark(int size)
        {
            if (size < 1 || size > 2048) throw new ArgumentOutOfRangeException("size", "Use a size from 1 to 2048 pixels.");
            int renderSize = Math.Min(2048, size * 4);
            using (Bitmap source = new Bitmap(renderSize, renderSize, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(source))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.ScaleTransform(renderSize / 256F, renderSize / 256F);
                    DrawMark(graphics);
                }
                Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(result))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, renderSize, renderSize, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
        }

        static void DrawMark(Graphics graphics)
        {
            using (GraphicsPath square = new GraphicsPath())
            using (SolidBrush navy = new SolidBrush(Color.FromArgb(14, 20, 34)))
            {
                square.AddArc(1, 1, 96, 96, 180, 90);
                square.AddArc(159, 1, 96, 96, 270, 90);
                square.AddArc(159, 159, 96, 96, 0, 90);
                square.AddArc(1, 159, 96, 96, 90, 90);
                square.CloseFigure();
                graphics.FillPath(navy, square);
            }
            using (GraphicsPath orbit = new GraphicsPath())
            using (GraphicsPath arrow = new GraphicsPath())
            using (Pen indigo = new Pen(Color.FromArgb(137, 145, 255), 12))
            {
                indigo.StartCap = LineCap.Round;
                indigo.EndCap = LineCap.Round;
                indigo.LineJoin = LineJoin.Round;
                orbit.AddBezier(204, 166, 225, 125, 207, 75, 163, 55);
                orbit.AddBezier(163, 55, 117, 34, 66, 55, 47, 99);
                orbit.AddBezier(47, 99, 27, 145, 48, 195, 94, 212);
                orbit.AddBezier(94, 212, 128, 225, 165, 217, 190, 192);
                graphics.DrawPath(indigo, orbit);
                arrow.AddLines(new PointF[] { new PointF(171, 191), new PointF(194, 188), new PointF(190, 211) });
                graphics.DrawPath(indigo, arrow);
            }
            using (SolidBrush mint = new SolidBrush(Color.FromArgb(93, 222, 186)))
                graphics.FillPolygon(mint, new PointF[] {
                    new PointF(88, 70), new PointF(96, 171), new PointF(117, 149), new PointF(137, 184),
                    new PointF(156, 173), new PointF(137, 141), new PointF(166, 137)
                });
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    }
}
