using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
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
        private System.Windows.Forms.Timer? _hoverTimer;   // Click‑Through Umschalter
        private System.Windows.Forms.Timer? _topKeeper;    // Z‑Order‑Keeper (behutsam)

        private bool _dragActive;

        // Stabiler Icon-Cache
        private Bitmap? _iconBitmapCached;

        // ---- P/Invoke ----
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const int  GWL_EXSTYLE       = -20;
        private const int  WS_EX_TRANSPARENT = 0x00000020;

        private static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);

        private const uint SWP_NOSIZE        = 0x0001;
        private const uint SWP_NOMOVE        = 0x0002;
        private const uint SWP_NOZORDER      = 0x0004;
        private const uint SWP_NOACTIVATE    = 0x0010;
        private const uint SWP_FRAMECHANGED  = 0x0020;
        private const uint SWP_SHOWWINDOW    = 0x0040;

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

            ShowInTaskbar   = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = Color.Black;
            Opacity         = ClampOpacity(_config.HotCornerOpacity);

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

            // **Behutsamer Top‑Most‑Keeper**: alle 2s, aber tray‑freundlich
            _topKeeper = new System.Windows.Forms.Timer { Interval = 2000 };
            _topKeeper.Tick += (_, __) => EnsureOnTop();
            _topKeeper.Start();

            // Nach finaler Größen-/DPI-Init nochmals Größe & Position fixen + TopMost auffrischen
            HandleCreated += (_, __) =>
            {
                BeginInvoke(new Action(() =>
                {
                    ApplySquareSize();
                    PrepareIconCache();
                    ApplyPosition();
                    UpdateClickThrough();
                    EnsureOnTop();
                    Invalidate();
                }));
            };
            Shown += (_, __) =>
            {
                ApplySquareSize();
                ApplyPosition();
                UpdateClickThrough();
                EnsureOnTop();
                Invalidate();

                // Späte Runde, falls DPI/Layout noch nachzieht
                var t = new System.Windows.Forms.Timer { Interval = 60 };
                t.Tick += (s2, e2) => { t.Stop(); t.Dispose(); ApplySquareSize(); ApplyPosition(); EnsureOnTop(); Invalidate(); };
                t.Start();
            };

            // Bei DPI-Wechsel (Per-Monitor-DPI): Größe & Position neu, dann nach oben legen
            this.DpiChanged += (_, __) =>
            {
                ApplySquareSize();
                PrepareIconCache();
                ApplyPosition();
                EnsureOnTop();
                Invalidate();
            };

            // Auch bei manueller Positionsänderung (z. B. Config-Wechsel) nach oben legen
            Move   += (_, __) => EnsureOnTop();
            Resize += (_, __) => EnsureOnTop();
        }

        // ---- Öffentliche API (von Settings genutzt) ----
        public void ApplyConfig()
        {
            Opacity = ClampOpacity(_config.HotCornerOpacity);
            ApplySquareSize();
            PrepareIconCache();
            ApplyPosition();
            TopMost = true;
            BringToFront();
            UpdateClickThrough();
            EnsureOnTop();
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
        private void UpdateClickThrough()
        {
            SetClickThrough(ShouldClickThrough());
            // danach nicht aggressiv toppen; das macht EnsureOnTop() zeitgesteuert/ereignisgetriggert
        }

        // ---- Z‑Order / Always‑on‑Top (tray‑freundlich) ----
        /// <summary>Hebt die Hot‑Corner behutsam nach oben – außer der User interagiert mit dem System‑Tray/Taskleiste.</summary>
        private void EnsureOnTop()
        {
            if (!IsHandleCreated || !Visible) return;

            // 1) Wenn der Cursor im Taskleistenbereich ist → nichts tun (Tray‑Menü nicht stören)
            if (IsCursorInTaskbarArea()) return;

            // 2) Wenn der Vordergrund ein Explorer/Tray‑Fenster ist → nichts tun
            if (IsExplorerTrayForeground()) return;

            try
            {
                // Einfaches Re‑Assert TOPMOST (ohne NOTOPMOST‑Pulse, ohne Aktivierung)
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        private static bool IsExplorerTrayForeground()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return false;

                var sb = new StringBuilder(256);
                if (GetClassName(h, sb, sb.Capacity) <= 0) return false;
                string cls = sb.ToString();

                // Häufige Klassen: "Shell_TrayWnd", "NotifyIconOverflowWindow", "#32768" (Menü), "DV2ControlHost"
                if (cls.IndexOf("Tray", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (cls.IndexOf("Notify", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (cls.Equals("#32768", StringComparison.Ordinal)) return true; // klassisches Menü
                if (cls.IndexOf("DV2ControlHost", StringComparison.OrdinalIgnoreCase) >= 0) return true;

                return false;
            }
            catch { return false; }
        }

        private static bool IsCursorInTaskbarArea()
        {
            try
            {
                var primary = Screen.PrimaryScreen;
                if (primary == null) return false;

                Rectangle b = primary.Bounds;
                Rectangle wa = primary.WorkingArea;

                // Ableitung des Taskleisten‑Rechtecks (Differenz Bounds vs. WorkingArea)
                if (wa.Top > b.Top)    // Taskleiste oben
                    return new Rectangle(b.Left, b.Top, b.Width, wa.Top - b.Top).Contains(Cursor.Position);
                if (wa.Left > b.Left)  // links
                    return new Rectangle(b.Left, b.Top, wa.Left - b.Left, b.Height).Contains(Cursor.Position);
                if (wa.Right < b.Right) // rechts
                    return new Rectangle(wa.Right, b.Top, b.Right - wa.Right, b.Height).Contains(Cursor.Position);
                if (wa.Bottom < b.Bottom) // unten (Standard)
                    return new Rectangle(b.Left, wa.Bottom, b.Width, b.Bottom - wa.Bottom).Contains(Cursor.Position);

                return false;
            }
            catch { return false; }
        }

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
        private void ApplySquareSize()
        {
            int s = System.Math.Max(4, _config.HotCornerSize);
            if (Width != s || Height != s)
                Size = new Size(s, s);

            // Quadratisch halten (bis nächste Konfig‑Änderung)
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
            try { _topKeeper?.Stop(); _topKeeper?.Dispose(); } catch { }
            try { _blinkTimer?.Stop(); _blinkTimer?.Dispose(); } catch { }
            try { _iconBitmapCached?.Dispose(); } catch { }
        }
    }
}
