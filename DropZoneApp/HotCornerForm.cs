using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            // Einzelinstanz sicherstellen
            lock (_guard)
            {
                if (_active != null && !_active.IsDisposed)
                {
                    try { _active.Close(); _active.Dispose(); } catch { }
                }
                _active = this;
            }

            _config = config;
            _host   = host;

            // Flackerfrei & keine automatische Größen-Skalierung
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            AutoScaleMode  = AutoScaleMode.None;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black;
            Opacity   = ClampOpacity(_config.HotCornerOpacity);

            ApplySquareSize();       // **Breite=Höhe fixieren**
            AllowDrop = true;

            // Erste Position (wird nach Handle/Shown nochmals sauber justiert)
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

            // **Wichtig**: Nach finaler Größen-/DPI-Init nochmals Größe & Position fixen
            HandleCreated += (_, __) =>
            {
                BeginInvoke(new Action(() =>
                {
                    ApplySquareSize();
                    PrepareIconCache();
                    ApplyPosition();
                    UpdateClickThrough();
                    Invalidate();
                }));
            };
            Shown += (_, __) =>
            {
                ApplySquareSize();
                ApplyPosition();
                UpdateClickThrough();
                Invalidate();

                // Späte Runde, falls DPI/Layout noch nachzieht
                var t = new System.Windows.Forms.Timer { Interval = 60 };
                t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ApplySquareSize(); ApplyPosition(); Invalidate(); };
                t.Start();
            };

            // Bei DPI-Wechsel (Per-Monitor-DPI): Größe & Position neu
            this.DpiChanged += (_, __) =>
            {
                ApplySquareSize();
                PrepareIconCache();
                ApplyPosition();
                Invalidate();
            };
        }

        // ---- Öffentliche API (von Settings genutzt) ----
        public void ApplyConfig()
        {
            Opacity = ClampOpacity(_config.HotCornerOpacity);
            ApplySquareSize();       // **immer** quadratisch setzen
            PrepareIconCache();
            ApplyPosition();         // korrekt in WorkingArea einklemmen
            TopMost = true;
            BringToFront();
            UpdateClickThrough();
            Invalidate();
        }

        // ---- Rendering ----
        private void OnPaintHotCorner(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Rahmen
            using (var pen = new Pen(ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray),
                                     System.Math.Max(1, _config.HotBorderThickness)))
            {
                var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                g.DrawRectangle(pen, rr);
            }

            // Icon mittig
            try
            {
                var bmp = _iconBitmapCached;
                if (bmp != null)
                {
                    int cfg  = System.Math.Max(8, _config.IconSizeHotCorner);
                    int size = System.Math.Min(cfg, System.Math.Min(ClientSize.Width, ClientSize.Height) - 2);
                    size = System.Math.Max(8, size);

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

        // ---- Click‑Through ----
        private double ClampOpacity(double o) => System.Math.Max(0.01, System.Math.Min(1.0, o));

        private bool ShouldClickThrough()
        {
            if (_dragActive) return false;
            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false;
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

        // ---- Drop‑Handling ----
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
                double target = System.Math.Min(1.0, System.Math.Max(orig, orig * 4.0));
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

        // ---- Größe & Position ----
        /// <summary>
        /// Erzwingt eine quadratische Größe gem. Config und sperrt sie über Min/MaxSize.
        /// Dadurch kann die Form nach dem Start nicht "breiter als hoch" werden.
        /// </summary>
        private void ApplySquareSize()
        {
            int s = System.Math.Max(4, _config.HotCornerSize);
            if (Width != s || Height != s)
                Size = new Size(s, s);

            // **hart** quadratisch halten (bis nächste Konfig‑Änderung)
            MinimumSize = new Size(s, s);
            MaximumSize = new Size(s, s);
        }

        private void ApplyPosition()
        {
            var wa = GetWorkingAreaForIndex(_config.HotCornerMonitor);
            if (wa == Rectangle.Empty) return;

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

            // Clamp in die WorkingArea (kein Herausstehen, auch bei negativen X)
            x = System.Math.Min(System.Math.Max(wa.Left + 1, x), wa.Right  - w - 1);
            y = System.Math.Min(System.Math.Max(wa.Top  + 1, y), wa.Bottom - h - 1);

            Location = new Point(x, y);
        }

        private Rectangle GetWorkingAreaForIndex(int idx)
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0)
                return Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;

            idx = System.Math.Max(0, System.Math.Min(idx, screens.Length - 1));
            return screens[idx].WorkingArea;
        }

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
        }
    }
}
