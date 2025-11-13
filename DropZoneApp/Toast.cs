using System;
using System.Drawing;
using System.Windows.Forms;

namespace DropZoneApp
{
    public static class Toast
    {
        public static void Show(string title, string text, int durationMs = 3000)
        {
            try
            {
                var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;
                var ni = new NotifyIcon
                {
                    Icon = ico,
                    Visible = true,
                    BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "DropZone" : title,
                    BalloonTipText  = string.IsNullOrWhiteSpace(text)  ? "" : text
                };
                ni.ShowBalloonTip(Math.Max(1000, durationMs));

                var timer = new Timer { Interval = Math.Max(1500, durationMs + 800) };
                timer.Tick += (s, e) =>
                {
                    try { ni.Visible = false; ni.Dispose(); } catch { }
                    try { timer.Stop(); timer.Dispose(); } catch { }
                };
                timer.Start();
            }
            catch { /* ignore */ }
        }
    }
}
