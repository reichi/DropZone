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
        private System.Windows.Forms.Timer? _hoverTimer;    // Click-Through Überwachung
        private System.Windows.Forms.Timer? _topMostTimer;  // Z‑Order „Auffrischung“

        private bool _dragActive;
        private bool _didPostShownReposition;

        // Stabiler Icon-Cache
        private Bitmap? _iconBitmapCached;

        // ---- P/Invoke ----
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private static readonly IntPtr HWND_TOPMOST    = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST  = new IntPtr(-2);

        private const uint SWP_NOSIZE        = 0x0001;
        private const uint SWP_NOMOVE        = 0x0002;
        private const uint SWP_NOZORDER      = 0x0004;
        private const uint SWP_NOREDRAW      = 0x0008;
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

            // WICHTIG: keine automatische Skalierung -> verhindert rechteckige Abweichungen
            AutoScaleMode = AutoScaleMode.None;

            // Flackerfrei zeichnen
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            DoubleBuffered = true;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black;
            Opacity   = ClampOpacity(_config.HotCornerOpacity);

            EnsureSquare();      // << immer quadratisch setzen
            AllowDrop = true;

            // Erste Position – wird nach Handle/Shown nochmals exakt gesetzt
            ApplyPosition();
            PrepareIconCache();

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

            // Proaktives Umschalten (damit DragEnter sicher ankommt)
            _hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _hoverTimer.Tick += (_, __) => { UpdateClickThrough(); };
            _hoverTimer.Start();

            // Regelmäßig Top‑Most „auffrischen“
            _topMostTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _topMostTimer.Tick += (_, __) => EnsureTopMost();
            _topMostTimer.Start();

            // Nach Handle-Erzeugung: Größe *quadratisch* + Position finalisieren + TopMost erzwingen
            HandleCreated += (_, __) =>
            {
                EnsureSquare();
                UpdateClickThrough();
                PrepareIconCache();
                ApplyPosition();
                EnsureTopMost();
                Invalidate();
            };

            // Nach dem ersten Shown noch einmal (nach erster DPI/Layout-Runde)
            Shown += (_, __) =>
            {
                if (!_didPostShownReposition)
                {
                    _didPostShownReposition = true;
                    BeginInvoke(new Action(() =>
                    {
                        EnsureSquare();
                        ApplyPosition();
                        EnsureTopMost();
                        UpdateClickThrough();
                        Invalidate();
                    }));
                }
            };

            // DPI/Display-Änderungen
            DpiChanged += (_, __) =>
            {
                EnsureSquare();
                PrepareIconCache();
                ApplyPosition();
                EnsureTopMost();
                Invalidate();
            };
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            EnsureSquare();
            ApplyPosition();
            EnsureTopMost();
            Invalidate();
        }

        // ==== öffentlich (Settings) ====
        public void ApplyConfig()
        {
            Opacity = ClampOpacity(_config.HotCornerOpacity);
            EnsureSquare();          // << immer setzen
            PrepareIconCache();
            ApplyPosition();
            EnsureTopMost();
            TopMost = true;
            BringToFront();
            UpdateClickThrough();
            Invalidate();
        }

        // ==== Rendering ====
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var pen = new Pen(ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray),
                                     Math.Max(1, _config.HotBorderThickness)))
            {
                var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                g.DrawRectangle(pen, rr);
            }

            try
            {
                var bmp = _iconBitmapCached;
                if (bmp != null)
                {
                    int cfg  = Math.Max(8, _config.IconSizeHotCorner);
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

        // ==== Größe & Click‑Through ====
        private void EnsureSquare()
        {
            int side = Math.Max(4, _config.HotCornerSize);
            if (Width != side || Height != side)
                Size = new Size(side, side);
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
        private void UpdateClickThrough()
        {
            SetClickThrough(ShouldClickThrough());
            // Nach Umschaltung sicherheitshalber z‑order erneuern
            EnsureTopMost();
        }

        // ==== Top‑Most Erzwingung ====
        private void EnsureTopMost()
        {
            if (!IsHandleCreated || !Visible) return;
            try
            {
                TopMost = true; // WinForms-Flag
                // Z‑Order aktiv ganz nach oben (ohne Fokuswechsel)
                SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch { }
        }

        // ==== Drop ====
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

        // ==== Positionierung (robust mit Clamping) ====
        private void ApplyPosition()
        {
            var screen = GetConfiguredScreen();
            var wa = screen.WorkingArea; // oberhalb der Taskleiste bleiben

            string corner = _config.HotCornerCorner ?? "TopLeft";
            Point p = corner switch
            {
                "TopRight"    => new Point(wa.Right  - Width - 1,  wa.Top + 1),
                "BottomLeft"  => new Point(wa.Left + 1,            wa.Bottom - Height - 1),
                "BottomRight" => new Point(wa.Right  - Width - 1,  wa.Bottom - Height - 1),
                _             => new Point(wa.Left + 1,            wa.Top + 1),
            };

            Location = p;

            // Hart einklemmen (kein Überstand)
            int x = Math.Min(Math.Max(wa.Left,  Location.X), wa.Right  - Width);
            int y = Math.Min(Math.Max(wa.Top,   Location.Y), wa.Bottom - Height);
            if (x != Location.X || y != Location.Y) Location = new Point(x, y);
        }

        private static Screen GetScreenBySafeIndex(int idx)
        {
            var all = Screen.AllScreens;
            if (all.Length == 0) return Screen.PrimaryScreen!;
            if (idx < 0 || idx >= all.Length) idx = 0;
            return all[idx];
        }

        private Screen GetConfiguredScreen() => GetScreenBySafeIndex(_config.HotCornerMonitor);

        // ==== Icon-Cache ====
        private void PrepareIconCache()
        {
            try { _iconBitmapCached?.Dispose(); } catch { /* ignore */ }
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

            try { _iconBitmapCached = SystemIcons.Application.ToBitmap(); }
            catch { _iconBitmapCached = null; }
        }

        // ==== Lifecycle ====
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            lock (_guard)
            {
                if (_active == this) _active = null;
            }
            try { _hoverTimer?.Stop(); _hoverTimer?.Dispose(); } catch { }
            try { _topMostTimer?.Stop(); _topMostTimer?.Dispose(); } catch { }
            try { _blinkTimer?.Stop(); _blinkTimer?.Dispose(); } catch { }
            try { _iconBitmapCached?.Dispose(); } catch { }
            try { SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged; } catch { }
        }
    }
}
