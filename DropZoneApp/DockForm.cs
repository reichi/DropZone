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
        private bool _moving;
        private Point _moveOffset;

        private System.Windows.Forms.Timer? _pulseTimer;
        private int _pulseElapsed;

        // NEU: Polling-Timer, damit Drag von außen zuverlässig erkannt wird
        private System.Windows.Forms.Timer? _clickThroughTimer;

        // --- P/Invoke für Extended Window Styles (Click-Through) ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private static int GetExStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8) return (int)GetWindowLongPtr64(hWnd, GWL_EXSTYLE);
            return GetWindowLong(hWnd, GWL_EXSTYLE);
        }

        private static void SetExStyle(IntPtr hWnd, int exStyle)
        {
            if (hWnd == IntPtr.Zero) return;
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
            else
                SetWindowLong32(hWnd, GWL_EXSTYLE, exStyle);

            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        private void SetClickThrough(bool enable)
        {
            if (!IsHandleCreated) return;

            void apply(IntPtr handle)
            {
                int ex = GetExStyle(handle);
                int newEx = enable ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
                if (newEx != ex) SetExStyle(handle, newEx);
            }

            apply(Handle);
            foreach (Control c in Controls)
                if (c.IsHandleCreated) apply(c.Handle);

            _status.Enabled = !enable;
            _progress.Enabled = !enable;
        }

        private bool ShouldClickThrough()
        {
            // Click-Through aktiv nur, wenn
            // - Option aktiv
            // - kein Drag aktiv
            // - keine STRG-Taste (Verschieben)
            // - keine Maustaste gedrückt (typ. Drag aus anderem Fenster)
            if (!_config.DockClickThrough) return false;
            if (_dragActive) return false;
            if ((ModifierKeys & Keys.Control) == Keys.Control) return false;
            if (Control.MouseButtons != MouseButtons.None) return false;
            return true;
        }

        private void UpdateClickThroughState() => SetClickThrough(ShouldClickThrough());

        // STRG = Fenster greifen (verschieben); sonst HTTRANSPARENT-Fallback
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCAPTION = 2;
            const int HTTRANSPARENT = -1;

            if (m.Msg == WM_NCHITTEST)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    m.Result = (IntPtr)HTCAPTION; // drag-move
                    return;
                }
                if (ShouldClickThrough())
                {
                    m.Result = (IntPtr)HTTRANSPARENT; // Maus an Fenster darunter
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

            // NEU: Poll-Timer aktiviert/deaktiviert Click-Through, damit DragEnter überhaupt ankommt
            _clickThroughTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _clickThroughTimer.Tick += (_, __) => UpdateClickThroughState();
            _clickThroughTimer.Start();

            Move += (_, __) => SavePosition();
            Resize += (_, __) => SaveSize();

            DoubleClick += (_, __) => { _host.Show(); _host.Activate(); };

            MouseDown += (s, e) =>
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control && e.Button == MouseButtons.Left)
                {
                    _moving = true;
                    _moveOffset = new Point(e.X, e.Y);
                }
                UpdateClickThroughState();
            };
            MouseUp += (s, e) =>
            {
                _moving = false;
                UpdateClickThroughState();
            };
            MouseMove += (s, e) =>
            {
                if (_moving)
                {
                    var screenPos = PointToScreen(new Point(e.X, e.Y));
                    Location = new Point(screenPos.X - _moveOffset.X, screenPos.Y - _moveOffset.Y);
                }
            };

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
                    triggerNotifications: true);

                if (_config.PulseAnimation) StartPulse();
                if (_config.Notifications) Toast.Show("DropZone", "Ablage abgeschlossen", 3000);
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
