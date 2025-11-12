using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DropZoneApp
{
    internal static class GraphicsUtil
    {
        public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            int r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int d = r * 2;
            using var path = new GraphicsPath();
            path.StartFigure();
            // top-left
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            // top-right
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            // bottom-right (fix: provide width & height, correct start angle 0)
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            // bottom-left
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }
    }
}
