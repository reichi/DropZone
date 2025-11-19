using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DropZoneApp
{
    public sealed class HotCornerForm : Form
    {
        // ---- Singleton-Schutz: maximal 1 aktive HotCorner ----
        private static HotCornerForm? _active;
        private static readonly object _guard = new();

        private readonly AppConfig _config;
        private readonly MainForm _host;

        private System.Windows.Forms.Timer? _blinkTimer;
        private System.Windows.Forms.Timer? _hoverTimer;

        private bool _dragActive;

        // Stabiler Icon-Cache
        private Bitmap? _iconBitmapCached;

        // ---- Click-Through per Extended Styles ----
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

        public HotCornerForm(AppConfig config, MainForm host)
        {
            // Schutz gegen parallele/mehrfache Erstellung:
            lock (_guard)
            {
                if (_active != null && !_active.IsDisposed)
                {
                    try { _active.Close(); _active.Dispose(); } catch { }
                }
                _active = this;
            }

            _config = config;
            _host = host;

            // Flackerfrei & konsistentes Zeichnen
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            DoubleBuffered = true;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black;
            Opacity = ClampOpacity(_config.HotCornerOpacity);

            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);

            AllowDrop = true;

            // Erste (vorläufige) Positionierung – wird nach DPI/Shown nochmals korrekt gesetzt
            ApplyPosition();
            PrepareIconCache();

            // Rendering
            Paint += OnPaintHotCorner;

            // Drag&Drop
            DragEnter += (s, e) =>
            {
                _dragActive = true;
                UpdateClickThrough();
                e.Effect = HasSupportedFormats(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragLeave += (s, e) => { _dragActive = false; UpdateClickThrough(); };
            DragDrop  += async (s, e) =>
            {
                _dragActive = false; UpdateClickThrough();
                await HandleDropAsync(e.Data!);
            };

            // Proaktives Umschalten (damit DragEnter von außen sicher ankommt)
            _hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _hoverTimer.Tick += (_, __) => UpdateClickThrough();
            _hoverTimer.Start();

            // >>> WICHTIG: Nach finaler Größen-/DPI-Ermittlung nochmals positionieren <<<
            HandleCreated += (_, __) =>
            {
                // auf die nächste Idle-Message legen (Größe/DPI sind dann final)
                BeginInvoke(new Action(() =>
                {
                    PrepareIconCache();
                    ApplyPosition();   // **erneut und korrekt**
                    UpdateClickThrough();
                    Invalidate();
                }));
            };

            Shown += (_, __) =>
            {
                // Bei einigen Setups ist erst nach Shown die DPI korrekt – daher nochmal
                ApplyPosition();
                UpdateClickThrough();
                Invalidate();
            };

            // Reagiert auf DPI/Display-Änderungen (Monitor verschoben, Taskleiste, Skalierung …)
            this.DpiChanged += (_, __) => { PrepareIconCache(); ApplyPosition(); Invalidate(); };
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            // Monitorkonfiguration änderte sich -> neu einpassen
            ApplyPosition();
            Invalidate();
        }

        public void ApplyConfig()
        {
            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);
            Opacity = ClampOpacity(_config.HotCornerOpacity);
            PrepareIconCache();
            ApplyPosition();          // Korrekt einpassen (inkl. Clamping)
            TopMost = true;
            BringToFront();
            UpdateClickThrough();
            Invalidate();
        }

        private void OnPaintHotCorner(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Rahmen
            using (var pen = new Pen(ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray),
                                     Math.Max(1, _config.HotBorderThickness)))
            {
                var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                g.DrawRectangle(pen, rr);
            }

            // Icon
            try
            {
                var bmp = _iconBitmapCached;
                if (bmp != null)
                {
                    int cfg = Math.Max(8, _config.IconSizeHotCorner);
                    int size = Math.Min(cfg, Math.Min(ClientSize.Width, ClientSize.Height) - 2);
                    size = Math.Max(8, size);

                    var dest = new Rectangle(
                        (ClientSize.Width  - size) / 2,
                        (ClientSize.Height - size) / 2,
                        size, size);

                    var old = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, dest);
                    g.InterpolationMode = old;
                }
            }
            catch { }
        }

        private double ClampOpacity(double o) => Math.Max(0.01, Math.Min(1.0, o));

        private bool ShouldClickThrough()
        {
            if (_dragActive) return false;
            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false; // externer Drag
            return true;
        }

        private void SetClickThrough(bool enable)
        {
            if (!IsHandleCreated) return;
            try
            {
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
        private void UpdateClickThrough() => SetClickThrough(ShouldClickThrough());

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
                await _host.ProcessDropAsync(data, null, null, false);
                if (_config.HotCornerBlink) Blink();
            }
            catch { }
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

        // ---- Positionierung (mit Clamping in die WorkingArea) ----
        private void ApplyPosition()
        {
            var screen = GetTargetScreen();
            Rectangle wa = screen.WorkingArea;   // Taskleiste berücksichtigen

            int w = Width;
            int h = Height;

            int x, y;
            string corner = _config.HotCornerCorner ?? "TopLeft";
            switch (corner)
            {
                case "TopRight":
                    x = wa.Right - w - 1; y = wa.Top + 1; break;
                case "BottomLeft":
                    x = wa.Left + 1; y = wa.Bottom - h - 1; break;
                case "BottomRight":
                    x = wa.Right - w - 1; y = wa.Bottom - h - 1; break;
                default: // TopLeft
                    x = wa.Left + 1; y = wa.Top + 1; break;
            }

            // *** CLAMP: Stelle sicher, dass die Ecke nie in den Nachbar-Bildschirm hineinragt ***
            x = Math.Min(Math.Max(wa.Left,  x), wa.Right  - w);
            y = Math.Min(Math.Max(wa.Top,   y), wa.Bottom - h);

            // Bei negativen Koordinaten (linker Bildschirm) ebenfalls korrekt setzen
            Location = new Point(x, y);
        }

        private Screen GetTargetScreen()
        {
            var screens = Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_config.HotCornerMonitor, screens.Length - 1));
            // Falls Monitore vertauscht wurden und der Index nicht mehr existiert, einfach in der Nähe bleiben:
            try { return screens[idx]; } catch { return Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position); }
        }

        // ---- Icon-Caching (stabil auch nach Neustart / DPI) ----
        private void PrepareIconCache()
        {
            try { _iconBitmapCached?.Dispose(); } catch { }
            _iconBitmapCached = null;

            try
            {
                using var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null)
                {
                    _iconBitmapCached = ico.ToBitmap();
                    if (_iconBitmapCached != null) return;
                }
            }
            catch { }

            try { _iconBitmapCached = SystemIcons.Application.ToBitmap(); } catch { _iconBitmapCached = null; }
        }

        // ---- Lebenszyklus ----
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            lock (_guard)
            {
                if (_active == this) _active = null;
            }
            try { _hoverTimer?.Stop(); _hoverTimer?.Dispose(); } catch { }
            try { _blinkTimer?.Stop(); _blinkTimer?.Dispose(); } catch { }
            try { _iconBitmapCached?.Dispose(); } catch { }
            try { SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged; } catch { }
        }
    }
}
