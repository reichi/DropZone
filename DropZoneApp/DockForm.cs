using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace DropZoneApp
{
    public sealed class DockForm : Form
    {
        private readonly AppConfig _config;
        private readonly MainForm _host;
        private readonly Label _status;
        private readonly ProgressBar _progress;

        private bool _dragActive;
        private bool _moving;
        private Point _moveOffset;

        private System.Windows.Forms.Timer? _pulseTimer;
        private int _pulseElapsed;

        // Polling, damit Drag von außen zuverlässig erkannt wird (ohne WS_EX_TRANSPARENT)
        private System.Windows.Forms.Timer? _clickThroughTimer;

        private bool ShouldPassThrough()
        {
            // Click-Through aktiv, wenn:
            // - Option aktiv
            // - kein Drag aktiv
            // - STRG NICHT gedrückt (STRG = verschieben)
            // - keine Maustaste gedrückt (typischer Fall: externer Drag hält LMB)
            if (!_config.DockClickThrough) return false;
            if (_dragActive) return false;
            if ((ModifierKeys & Keys.Control) == Keys.Control) return false;

            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false;

            return true;
        }

        // STRG = verschieben (HTCAPTION); sonst ggf. durchreichen (HTTRANSPARENT)
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCAPTION = 2;
            const int HTTRANSPARENT = -1;

            if (m.Msg == WM_NCHITTEST)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    m.Result = (IntPtr)HTCAPTION; // STRG: Fenster verschieben erlaubt
                    return;
                }
                if (ShouldPassThrough())
                {
                    m.Result = (IntPtr)HTTRANSPARENT; // Klick/Hit an Fenster darunter
                    return;
                }
            }
            base.WndProc(ref m);
        }

        public DockForm(AppConfig config, MainForm host)
        {
            _config = config;
            _host = host;

            Text = "DropZone Dock";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            Opacity = Math.Clamp(_config.DockOpacity, 0.1, 1.0);
            BackColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Math.Max(60, _config.DockWidth), Math.Max(80, _config.DockHeight));

            if (_config.DockLeft.HasValue && _config.DockTop.HasValue)
                Location = new Point(_config.DockLeft.Value, _config.DockTop.Value);
            else
            {
                var wa = Screen.PrimaryScreen!.WorkingArea;
                Location = new Point(wa.Right - Width - 8, wa.Top + (wa.Height - Height) / 2);
            }

            AllowDrop = true;
            Padding = new Padding(10);

            _status = new Label { Dock = DockStyle.Bottom, Height = 18, Text = "Dock bereit", TextAlign = ContentAlignment.MiddleCenter };
            _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 12, Style = ProgressBarStyle.Continuous, Visible = true };

            Controls.Add(_status);
            Controls.Add(_progress);

            Paint += DockForm_Paint;

            DragEnter += (s, e) =>
            {
                _dragActive = true;
                e.Effect = HasSupportedFormats(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragLeave += (s, e) => { _dragActive = false; };
            DragDrop += async (s, e) =>
            {
                _dragActive = false;
                await HandleDropAsync(e.Data!);
            };

            Move += (_, __) => SavePosition();
            Resize += (_, __) => SaveSize();

            DoubleClick += (_, __) => { _host.Show(); _host.Activate(); };

            // Optionales manuelles Verschieben (zusätzlich zu HTCAPTION, stört aber nicht)
            MouseDown += (s, e) =>
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control && e.Button == MouseButtons.Left)
                {
                    _moving = true;
                    _moveOffset = new Point(e.X, e.Y);
                }
            };
            MouseUp += (s, e) => { _moving = false; };
            MouseMove += (s, e) =>
            {
                if (_moving)
                {
                    var screenPos = PointToScreen(new Point(e.X, e.Y));
                    Location = new Point(screenPos.X - _moveOffset.X, screenPos.Y - _moveOffset.Y);
                }
            };

            // Poll, damit bei gedrückter Taste (externer Drag) sofort Nicht‑Durchreichen aktiv wird
            _clickThroughTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _clickThroughTimer.Tick += (_, __) => { /* nur HitTest steuern, kein WS_EX_TRANSPARENT nötig */ };
            _clickThroughTimer.Start();
        }

        private static bool HasSupportedFormats(IDataObject? data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop)
                || data.GetDataPresent("FileGroupDescriptorW")
                || data.GetDataPresent("FileGroupDescriptor")
                || data.GetDataPresent("RenPrivateMessages");
        }

        private void SavePosition()
        {
            _config.DockLeft = Left;
            _config.DockTop = Top;
            _config.Save();
        }

        private void SaveSize()
        {
            _config.DockWidth = Width;
            _config.DockHeight = Height;
            _config.Save();
        }

        public void ApplyConfig()
        {
            TopMost = true;
            Opacity = Math.Clamp(_config.DockOpacity, 0.1, 1.0);
            if (_config.DockWidth > 0 && _config.DockHeight > 0)
                Size = new Size(Math.Max(60, _config.DockWidth), Math.Max(80, _config.DockHeight));
            if (_config.DockLeft.HasValue && _config.DockTop.HasValue)
                Location = new Point(_config.DockLeft.Value, _config.DockTop.Value);
            Invalidate();
        }

        private async Task HandleDropAsync(IDataObject data)
        {
            try
            {
                _progress.Style = ProgressBarStyle.Marquee;
                _status.Text = "Verarbeite …";

                await _host.ProcessDropAsync(
                    data,
                    progressOverride: _progress,
                    statusOverride: _status,
                    triggerNotifications: false); // Benachrichtigungen vollständig entfernt
            }
            catch (Exception ex)
            {
                _status.Text = "Fehler";
                Log.Error("Dock: Fehler beim Drop", ex);
                MessageBox.Show(this, ex.Message, "Dock-Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 0;
            }
        }

        private void DockForm_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(6, 6, ClientSize.Width - 12, ClientSize.Height - 30);
            var baseColor = ColorUtil.FromHex(_config.DockBorderColorHex, Color.Gray);
            using (var penBase = new Pen(baseColor, Math.Max(1, _config.DockBorderThickness)) { DashStyle = DashStyle.Dash })
            {
                GraphicsUtil.DrawRoundedRectangle(g, penBase, r, 12);
            }

            try
            {
                using var appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIco != null)
                {
                    using var bmp = appIco.ToBitmap();
                    int cfgSize = Math.Max(16, _config.IconSizeDock);
                    int iconSize = Math.Min(cfgSize, Math.Min(r.Width, r.Height) - 20);
                    iconSize = Math.Max(16, iconSize);
                    var dest = new Rectangle(r.Left + (r.Width - iconSize) / 2, r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
                    var oldInterp = g.InterpolationMode;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, dest);
                    g.InterpolationMode = oldInterp;
                }
            }
            catch { }

            if (_pulseTimer != null && _config.PulseAnimation)
            {
                float t = Math.Max(0f, Math.Min(1f, _pulseElapsed / (float)_config.PulseDurationMs));
                float amp = 1f - Math.Abs(0.5f - t) * 2f;
                int alpha = (int)(80 * amp);
                int extra = (int)Math.Ceiling(4 * amp);
                using var penPulse = new Pen(ColorUtil.WithAlpha(baseColor, 60 + alpha), Math.Max(1, _config.DockBorderThickness + extra));
                GraphicsUtil.DrawRoundedRectangle(g, penPulse, new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), 14);
            }
        }
    }
}
