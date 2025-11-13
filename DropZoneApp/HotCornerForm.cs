using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DropZoneApp
{
    public sealed class HotCornerForm : Form
    {
        private readonly AppConfig _config;
        private readonly MainForm _host;
        private System.Windows.Forms.Timer? _blinkTimer;
        private bool _dragActive;

        // Polling (keine Stile nötig)
        private System.Windows.Forms.Timer? _clickThroughTimer;

        private bool ShouldPassThrough()
        {
            if (_dragActive) return false;
            if ((ModifierKeys & Keys.Control) == Keys.Control) return false;
            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false;
            return true;
        }

        // Nur via WM_NCHITTEST durchreichen, keine Extended Styles
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST)
            {
                if (ShouldPassThrough())
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

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
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(bmp, dest);
                        g.InterpolationMode = old;
                    }
                }
                catch { }
            };

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

            _clickThroughTimer = new System.Windows.Forms.Timer { Interval = 60 };
            _clickThroughTimer.Tick += (_, __) => { /* nur HitTest, keine Stile */ };
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

        private async Task HandleDropAsync(IDataObject data)
        {
            try
            {
                await _host.ProcessDropAsync(data, null, null, false); // keine Notifications mehr
                if (_config.HotCornerBlink) Blink();
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
