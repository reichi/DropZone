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

        // Stabiler Icon-Cache (über Neustarts/Lifecycle)
        private Bitmap? _iconBitmapCached;

        // ---- Click-Through per Extended Styles (für echte Durchreiche) ----
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

        // ---- Konstruktor ----
        public HotCornerForm(AppConfig config, MainForm host)
        {
            // Schutz gegen parallele/mehrfache Erstellung:
            lock (_guard)
            {
                if (_active != null && !_active.IsDisposed)
                {
                    try { _active.Close(); _active.Dispose(); } catch { /* ignore */ }
                    _active = null;
                }
                _active = this;
            }

            _config = config;
            _host = host;

            // Stabileres Zeichnen (gegen „Ghosting“ / Artefakte):
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            DoubleBuffered = true;

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black; // Opacity nutzt Layered Window, daher keine TransparencyKey
            Opacity = ClampOpacity(_config.HotCornerOpacity);

            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);

            AllowDrop = true;

            // Position + Icon vorbereiten
            ApplyPosition();
            PrepareIconCache();

            // Events
            Paint += OnPaintHotCorner;

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

            HandleCreated += (_, __) =>
            {
                UpdateClickThrough();
                // Nach Handle-Erzeugung Icon noch einmal stabil laden (Win10/11 DPI/Explorer-Icons)
                PrepareIconCache();
            };
            Shown += (_, __) => { UpdateClickThrough(); Invalidate(); };

            // Auf DPI-Änderungen reagieren (Icon-Größe stabil halten)
            this.DpiChanged += (_, __) => { PrepareIconCache(); Invalidate(); };
        }

        // ---- Öffentliche API, die von außen aufgerufen wird ----
        public void ApplyConfig()
        {
            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);
            Opacity = ClampOpacity(_config.HotCornerOpacity);
            ApplyPosition();
            TopMost = true;
            BringToFront();
            PrepareIconCache();
            Invalidate();
            UpdateClickThrough();
        }

        // ---- Rendering ----
        private void OnPaintHotCorner(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Border aus Config
            var borderColor = ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray);
            int thickness = Math.Max(1, _config.HotBorderThickness);
            var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var pen = new Pen(borderColor, thickness))
            {
                g.DrawRectangle(pen, rr);
            }

            // Icon in Mitte
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
            catch { /* niemals crashen beim Paint */ }
        }

        // ---- Click‑Through ----
        private double ClampOpacity(double o) => Math.Max(0.01, Math.Min(1.0, o));

        private bool ShouldClickThrough()
        {
            if (_dragActive) return false;
            // Bei gedrückter Maustaste im Fenster: Drag kommt von außen => nicht durchreichen
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
            catch { /* Fehler gehen ins Log in ProcessDropAsync */ }
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

        // ---- Positionierung ----
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

        // ---- Icon-Caching (stabil auch nach Neustart / DPI) ----
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
            catch { /* ignore */ }

            try
            {
                _iconBitmapCached = SystemIcons.Application.ToBitmap();
            }
            catch
            {
                _iconBitmapCached = null; // als letztes Mittel: kein Icon
            }
        }

        // ---- Lebenszyklus ----
        protected override void OnFormClosed(FormClosedEventArgs e) // <-- KORREKTE SIGNATUR
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
