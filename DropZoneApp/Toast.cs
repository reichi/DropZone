using System;
using System.Drawing;
using System.Windows.Forms;

namespace DropZoneApp
{
    public static class Toast
    {
        /// <summary>
        /// Zeigt einen einfachen Tray-Ballon. Wird automatisch nach 'durationMs' geschlossen.
        /// </summary>
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
                    BalloonTipText  = text ?? string.Empty
                };
                ni.ShowBalloonTip(Math.Max(1000, durationMs));

                // WICHTIG: explizit WinForms-Timer verwenden (keine Kollision mit System.Threading.Timer)
                var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1500, durationMs + 800) };
                timer.Tick += (s, e) =>
                {
                    try { ni.Visible = false; ni.Dispose(); } catch { }
                    try { timer.Stop(); timer.Dispose(); } catch { }
                };
                timer.Start();
            }
            catch
            {
                // Quiet Hours / Policies / fehlende Shell dürfen keinen Crash auslösen
            }
        }
    }
}
