using System;
using System.Drawing;
using System.Windows.Forms;

namespace DropZoneApp
{
    public static class Toast
    {
        /// <summary>
        /// Zeigt einen einfachen Tray-Balloon an. Wird automatisch nach 'durationMs' wieder geschlossen.
        /// Läuft ohne bestehendes NotifyIcon, da ein eigenes temporäres Icon erzeugt und wieder entsorgt wird.
        /// </summary>
        public static void Show(string title, string text, int durationMs = 3000)
        {
            try
            {
                var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;

                // eigenes NotifyIcon anlegen
                var ni = new NotifyIcon
                {
                    Icon = ico,
                    Visible = true,
                    BalloonTipTitle = string.IsNullOrWhiteSpace(title) ? "DropZone" : title,
                    BalloonTipText  = text ?? string.Empty
                };

                ni.ShowBalloonTip(Math.Max(1000, durationMs));

                // **Fix**: explizit System.Windows.Forms.Timer verwenden (keine Ambiguität mit System.Threading.Timer)
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
                // absichtlich schlucken – fehlende Shell-Rechte / Quiet Hours etc. sollen keinen Crash verursachen
            }
        }
    }
}
