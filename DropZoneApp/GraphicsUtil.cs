using System;
using System.Drawing;

namespace DropZoneApp
{
    internal static class GraphicsUtil
    {
        public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            int r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int d = r * 2;
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.StartFigure();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, 90, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 180, 90);
            path.CloseFigure();
            g.DrawPath(pen, path);
        }
    }
}
