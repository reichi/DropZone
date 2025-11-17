using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DropZoneApp
{
    public sealed class HotCornerForm : Form
    {
        // --- Singleton: verhindert Mehrfach-Instanzen / 3x nebeneinander ---
        private static HotCornerForm? s_current;

        // --- Stabiles App-Icon (einmalig geladen, nicht disposed) ---
        private static readonly Icon s_appIcon = LoadAppIcon();

        private static Icon LoadAppIcon()
        {
            try
            {
                var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                return ico ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private readonly AppConfig _config;
        private readonly MainForm _host;

        private bool _dragActive;
        private System.Windows.Forms.Timer? _hoverTimer;  // proaktives Umschalten für Click‑Through
        private System.Windows.Forms.Timer? _blinkTimer;

        // --- Click-Through via Extended Styles ---
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

        private bool ShouldClickThrough()
        {
            if (_dragActive) return false; // während Drag: Drop-Ereignisse empfangen
            // Wenn Cursor im Fenster und Maustaste gedrückt (externer Drag): nicht durchreichen
            bool inside = Bounds.Contains(Cursor.Position);
            bool mouseDown = Control.MouseButtons != MouseButtons.None;
            if (inside && mouseDown) return false;
            return true;
        }

        private void UpdateClickThrough() => SetClickThrough(ShouldClickThrough());

        // --- Konstruktor ---
        public HotCornerForm(AppConfig config, MainForm host)
        {
            // Singleton-Schutz: alte Instanz schließen/entsorgen
            if (s_current != null && !s_current.IsDisposed)
            {
                try { s_current.Close(); s_current.Dispose(); } catch { }
            }
            s_current = this;

            _config = config;
            _host = host;

            // Flackerfrei zeichnen, nur einmaliger Paint-Pfad (kein += Paint-Handler mehr)
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();

            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = Color.Black;
            Opacity = Math.Max(0.01, Math.Min(1.0, _config.HotCornerOpacity));
            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);

            AllowDrop = true;
            ApplyPosition();

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

            // Polling, damit bei gedrückter Taste (externer Drag) der Stil rechtzeitig umgeschaltet wird
            _hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _hoverTimer.Tick += (_, __) => UpdateClickThrough();
            _hoverTimer.Start();

            HandleCreated += (_, __) => UpdateClickThrough();
            Shown += (_, __) => { UpdateClickThrough(); Invalidate(); };
        }

        protected override void OnFormClosed(EventArgs e)
        {
            if (ReferenceEquals(s_current, this)) s_current = null;
            base.OnFormClosed(e);
        }

        // Hintergrund sauber löschen (verhindert Ghosting)
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        // EINZIGER Zeichenpfad: Rahmen + Icon
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Rahmen zeichnen
            using (var pen = new Pen(ColorUtil.FromHex(_config.HotBorderColorHex, Color.Gray),
                                     Math.Max(1, _config.HotBorderThickness)))
            {
                var rr = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                g.DrawRectangle(pen, rr);
            }

            // App-Icon mittig zeichnen (stabil, ohne Mehrfach-Zeichnung)
            try
            {
                int cfg = Math.Max(8, _config.IconSizeHotCorner);
                int size = Math.Min(cfg, Math.Min(ClientSize.Width, ClientSize.Height) - 2);
                size = Math.Max(8, size);

                var dest = new Rectangle(
                    (ClientSize.Width  - size) / 2,
                    (ClientSize.Height - size) / 2,
                    size, size);

                // Icon direkt zeichnen (kein mehrfaches ToBitmap/Dispose nötig)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawIcon(s_appIcon, dest);
            }
            catch { }
        }

        private static bool HasSupportedFormats(IDataObject? data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop)
                || data.GetDataPresent("FileGroupDescriptorW")
                || data.GetDataPresent("FileGroupDescriptor")
                || data.GetDataPresent("RenPrivateMessages");
        }

        private async System.Threading.Tasks.Task HandleDropAsync(IDataObject data)
        {
            try
            {
                await _host.ProcessDropAsync(data, null, null, false);
                if (_config.HotCornerBlink) Blink();
            }
            catch { }
        }

        public void ApplyConfig()
        {
            Width  = Math.Max(4, _config.HotCornerSize);
            Height = Math.Max(4, _config.HotCornerSize);
            Opacity = Math.Max(0.01, Math.Min(1.0, _config.HotCornerOpacity));
            ApplyPosition();
            TopMost = true;
            BringToFront();
            UpdateClickThrough();
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
                _blinkTimer?.Dispose();

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
