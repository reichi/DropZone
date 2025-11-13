using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DropZoneApp
{
    public sealed class DockForm : Form
    {
        private readonly AppConfig _config;
        private readonly MainForm _host;
        private readonly Label _status;
        private readonly ProgressBar _progress;

        private bool _dragActive;

        private System.Windows.Forms.Timer? _pulseTimer;
        private int _pulseElapsed;

        // Proaktives Umschalten Click‑Through (damit DragEnter zuverlässig ankommt)
        private System.Windows.Forms.Timer? _hoverTimer;

        // --- P/Invoke / Styles ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int HTTRANSPARENT = -1;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private void SetClickThrough(bool enable)
        {
            if (!IsHandleCreated) return;

            void applyToHandle(IntPtr handle)
            {
                try
                {
                    int ex = GetWindowLong(handle, GWL_EXSTYLE);
                    int newEx = enable ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
                    if (newEx != ex)
                    {
                        SetWindowLong(handle, GWL_EXSTYLE, newEx);
                        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                    }
                }
                catch { }
            }

            // WICHTIG: auch auf Child‑Controls anwenden (Label, ProgressBar), sonst „schlucken“ die den Klick
            applyToHandle(this.Handle);
            foreach (Control c in Controls)
            {
                if (c.IsHandleCreated) applyToHandle(c.Handle);
            }

            _status.Enabled = !enable;
            _progress.Enabled = !enable;
        }

        private bool ShouldClickThrough()
        {
            if (!_config.DockClickThrough) return false;
            if (_dragActive) return false;                       // während Drag: eingehende Drops erlauben
            if ((ModifierKeys & Keys.Control) == Keys.Control) return false; // STRG = verschieben

            // Wenn Cursor im Fenster UND Taste gedrückt → nicht durchreichen (Drag aus Fremd-App)
            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false;

            return true;
        }

        private void UpdateClickThroughState() => SetClickThrough(ShouldClickThrough());

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    m.Result = (IntPtr)HTCAPTION; // STRG: klassisches Fensterziehen
                    return;
                }
                if (ShouldClickThrough())
                {
                    m.Result = (IntPtr)HTTRANSPARENT; // Klicks/Hit an darunterliegendes Fenster
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
                UpdateClickThroughState();
                e.Effect = HasSupportedFormats(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragLeave += (s, e) => { _dragActive = false; UpdateClickThroughState(); };
            DragDrop += async (s, e) =>
            {
                _dragActive = false; UpdateClickThroughState();
                await HandleDropAsync(e.Data!);
            };

            // STRG + LMB => sofort echtes Fensterziehen starten (unabhängig von Styles/Timer)
            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Control) == Keys.Control)
                {
                    SetClickThrough(false);
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                }
                else
                {
                    UpdateClickThroughState();
                }
            };
            MouseUp += (_, __) => UpdateClickThroughState();

            // Proaktives Umschalten (damit während gedrückter Maustaste DragEnter ankommt)
            _hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _hoverTimer.Tick += (_, __) => UpdateClickThroughState();
            _hoverTimer.Start();

            HandleCreated += (_, __) => UpdateClickThroughState();
            Shown += (_, __) => UpdateClickThroughState();
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
            UpdateClickThroughState();
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
                    triggerNotifications: false); // Notifications sind komplett entfernt

                if (_config.PulseAnimation) StartPulse();
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
                UpdateClickThroughState();
            }
        }

        private void StartPulse()
        {
            _pulseTimer?.Stop();
            _pulseTimer?.Dispose();
            _pulseElapsed = 0;
            _pulseTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _pulseTimer.Tick += (s, e) =>
            {
                _pulseElapsed += _pulseTimer!.Interval;
                if (_pulseElapsed >= _config.PulseDurationMs) { _pulseTimer.Stop(); _pulseTimer.Dispose(); _pulseTimer = null; }
                Invalidate();
            };
            _pulseTimer.Start();
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
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
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
