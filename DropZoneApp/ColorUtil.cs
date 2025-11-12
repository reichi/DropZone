using System;
using System.Drawing;
using System.Globalization;

namespace DropZoneApp
{
    internal static class ColorUtil
    {
        public static Color FromHex(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                hex = hex.Trim();
                if (hex.StartsWith("#")) hex = hex.Substring(1);
                if (hex.Length == 6)
                {
                    int r = int.Parse(hex.Substring(0,2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(2,2), NumberStyles.HexNumber);
                    int b = int.Parse(hex.Substring(4,2), NumberStyles.HexNumber);
                    return Color.FromArgb(255, r, g, b);
                }
                if (hex.Length == 8)
                {
                    int a = int.Parse(hex.Substring(0,2), NumberStyles.HexNumber);
                    int r = int.Parse(hex.Substring(2,2), NumberStyles.HexNumber);
                    int g = int.Parse(hex.Substring(4,2), NumberStyles.HexNumber);
                    int b = int.Parse(hex.Substring(6,2), NumberStyles.HexNumber);
                    return Color.FromArgb(a, r, g, b);
                }
            }
            catch { }
            return fallback;
        }

        public static string ToHex(Color c, bool includeAlpha = false)
        {
            return includeAlpha
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static Color WithAlpha(Color c, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c);
        }
    }
}
