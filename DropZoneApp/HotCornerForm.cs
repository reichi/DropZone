using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DropZoneApp
{
    public sealed class HotCornerForm : Form
    {
        private readonly AppConfig _config;
        private readonly MainForm _host;
        private System.Windows.Forms.Timer? _blinkTimer;
        private bool _dragActive;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private void SetClickThrough(bool enable)
        {
            try
            {
                if (!IsHandleCreated) return;
                int ex = GetWindowLong(this.Handle, GWL_EXSTYLE);
                int newEx = enable ? (ex | WS_EX_TRANSPARENT) : (ex & ~WS_EX_TRANSPARENT);
                if (newEx != ex)
                {
                    SetWindowLong(this.Handle, GWL_EXSTYLE, newEx);
                    SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch { }
        }

        private void UpdateClickThrough() => SetClickThrough(!_dragActive);

        public HotCornerForm(AppConfig config, MainForm host)
        {
            _config = config;
            _host = host;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black;
            Opacity = Math.Max(0.01, Math.Min(1.0, _config.HotCornerOpacity));
            Width = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);

            AllowDrop = true;
            ApplyPosition();

            Paint += (s, e) =>
            {
                try
                {
                    var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                    var baseColor = ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray);
                    using var pen = new Pen(baseColor, Math.Max(1, _config.HotBorderThickness));
                    var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                    g.DrawRectangle(pen, rr);

                    using var appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    if (appIco != null)
                    {
                        using var bmp = appIco.ToBitmap();
                        int cfg = Math.Max(8, _config.IconSizeHotCorner);
                        int size = Math.Min(cfg, Math.Min(ClientSize.Width, ClientSize.Height) - 2);
                        size = Math.Max(8, size);
                        var dest = new Rectangle((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);
                        var old = g.InterpolationMode;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, dest);
                        g.InterpolationMode = old;
                    }
                }
                catch { }
            };

            DragEnter += (s, e) =>
            {
                _dragActive = true;
                UpdateClickThrough();
                e.Effect = HasSupportedFormats(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragLeave += (s, e) => { _dragActive = false; UpdateClickThrough(); };
            DragDrop += async (s, e) =>
            {
                _dragActive = false; UpdateClickThrough();
                await HandleDropAsync(e.Data!);
            };

            // Klicks nicht behandeln -> gehen durch
            Shown += (s, e) => UpdateClickThrough();
            HandleCreated += (s, e) => UpdateClickThrough();
        }

        private static bool HasSupportedFormats(IDataObject? data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop)
                || data.GetDataPresent("FileGroupDescriptorW")
                || data.GetDataPresent("FileGroupDescriptor")
                || data.GetDataPresent("RenPrivateMessages");
        }

        private async Task HandleDropAsync(IDataObject data)
        {
            try
            {
                await _host.ProcessDropAsync(data, null, null, true);
                if (_config.Notifications) Toast.Show("DropZone", "Ablage abgeschlossen", 3000);
            }
            catch { }
        }

        public void ApplyConfig()
        {
            Width = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);
            Opacity = Math.Max(0.01, Math.Min(1.0, _config.HotCornerOpacity));
            ApplyPosition();
            TopMost = true;
            BringToFront();
            Invalidate();
            UpdateClickThrough();
        }

        private void ApplyPosition()
        {
            var screens = Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_config.HotCornerMonitor, screens.Length - 1));
            var wa = screens[idx].WorkingArea;
            var corner = _config.HotCornerCorner ?? "TopLeft";
            Point p = corner switch
            {
                "TopRight"    => new Point(wa.Right - Width - 1, wa.Top + 1),
                "BottomLeft"  => new Point(wa.Left + 1, wa.Bottom - Height - 1),
                "BottomRight" => new Point(wa.Right - Width - 1, wa.Bottom - Height - 1),
                _             => new Point(wa.Left + 1, wa.Top + 1),
            };
            Location = p;
        }

        private void Blink()
        {
            try
            {
                if (!_config.HotCornerBlink) return;
                double orig = Opacity;
                double target = Math.Min(1.0, Math.Max(orig, orig * 4.0));
                Opacity = target;
                _blinkTimer?.Stop();
                _blinkTimer = new System.Windows.Forms.Timer { Interval = 120 };
                _blinkTimer.Tick += (s, e) =>
                {
                    try { Opacity = orig; } catch { }
                    _blinkTimer!.Stop();
                    _blinkTimer!.Dispose();
                    _blinkTimer = null;
                };
                _blinkTimer.Start();
            }
            catch { }
        }
    }
}
