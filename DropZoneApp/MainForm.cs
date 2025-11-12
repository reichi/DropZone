using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace DropZoneApp
{
    public sealed class MainForm : Form
    {
        private readonly AppConfig _config;
        private readonly NotifyIcon _tray;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly Panel _dropPanel;
        private readonly CleanupService _cleanup;
        private bool _minimizeTipShown = false;
        private bool _realExit = false;

        private DockForm? _dock;
        private HotCornerForm? _hotCorner;

        private System.Windows.Forms.Timer? _pulseTimer;
        private int _pulseElapsed;

        public MainForm(AppConfig config)
        {
            _config = config;

            Text = "DropZone";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Size = new Size(540, 250);
            TopMost = _config.AlwaysOnTop;
            StartPosition = FormStartPosition.Manual;

            RestorePosition();

            _dropPanel = new Panel { Dock = DockStyle.Fill, AllowDrop = true, BackColor = Color.White };
            _dropPanel.Paint += DropPanel_Paint;
            _dropPanel.DragEnter += DropPanel_DragEnter;
            _dropPanel.DragDrop  += async (s, e) => await ProcessDropAsync(e.Data!);

            _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 18, Style = ProgressBarStyle.Continuous };
            _status   = new Label { Dock = DockStyle.Bottom, Height = 24, Text = "Bereit.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

            Controls.Add(_dropPanel);
            Controls.Add(_status);
            Controls.Add(_progress);

            _tray = new NotifyIcon { Text = "DropZoneApp" };
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { _tray.Icon = SystemIcons.Application; }
            _tray.Visible = true;
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
            _tray.DoubleClick += (_, __) => { RestoreFromTray(); };

            _tray.ContextMenuStrip = BuildTrayMenu();

            _cleanup = new CleanupService(_config, msg => Notify("DropZone", msg, ToolTipIcon.Info, 3000));

            Notify("DropZone", "Bereit zum Ablegen.", ToolTipIcon.Info, 2000);

            Move += (_, __) => SavePosition();
            Resize += (_, __) => { if (WindowState == FormWindowState.Minimized) MinimizeToTray(); };
            FormClosed += (_, __) => { _tray.Visible = false; _tray.Dispose(); _cleanup.Dispose(); _dock?.Close(); _hotCorner?.Close(); };
            FormClosing += (s, e) =>
            {
                if (!_realExit && _config.CloseToTray && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true; MinimizeToTray();
                }
            };

            ApplyDockAndHotCorner();

            Log.Info("Anwendung gestartet.");
        }

        private void DropPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(12, 12, _dropPanel.ClientSize.Width - 24, _dropPanel.ClientSize.Height - 60);

            var baseColor = ColorUtil.FromHex(_config.DropBorderColorHex, Color.Gray);
            using var pen = new Pen(baseColor, Math.Max(1, _config.DropBorderThickness)) { DashStyle = DashStyle.Dash };
            GraphicsUtil.DrawRoundedRectangle(g, pen, r, 16);

            try
            {
                using var appIco = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIco != null)
                {
                    using var bmp = appIco.ToBitmap();
                    int cfgSize = Math.Max(16, _config.IconSizeDropzone);
                    int iconSize = Math.Min(cfgSize, Math.Min(r.Width, r.Height) - 40);
                    iconSize = Math.Max(16, iconSize);
                    var dest = new Rectangle(
                        r.Left + (r.Width - iconSize) / 2,
                        r.Top + (r.Height - iconSize) / 2,
                        iconSize, iconSize);
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
                int alpha = (int)(90 * amp);
                int extra = (int)Math.Ceiling(4 * amp);
                using var penPulse = new Pen(ColorUtil.WithAlpha(baseColor, 60 + alpha), Math.Max(1, _config.DropBorderThickness + extra));
                GraphicsUtil.DrawRoundedRectangle(g, penPulse, new Rectangle(r.X-1, r.Y-1, r.Width+2, r.Height+2), 20);
            }
        }

        // Intercept native minimize to always tray
        protected override void WndProc(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MINIMIZE   = 0xF020;

            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_MINIMIZE)
            {
                MinimizeToTray();
                return;
            }
            base.WndProc(ref m);
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();

            var miShow = new ToolStripMenuItem("Drop‑Zone anzeigen", null, (_, __) => { RestoreFromTray(); });
            var miOpen = new ToolStripMenuItem("Zielordner öffnen", null, (_, __) => { try { Process.Start("explorer.exe", _config.TargetFolder); } catch { } });
            var miCleanup = new ToolStripMenuItem("Jetzt aufräumen", null, (_, __) => {
                var n = IndexStore.Cleanup(_config.TargetFolder, _config.DaysToKeep);
                if (_config.Notifications && n > 0) Notify("DropZone", $"{n} Datei(en) entfernt.", ToolTipIcon.Info, 3000);
                Log.Info($"Cleanup manuell: {n} Datei(en) entfernt.");
            });

            var miTop = new ToolStripMenuItem("Immer im Vordergrund") { Checked = _config.AlwaysOnTop };
            miTop.Click += (_, __) =>
            {
                _config.AlwaysOnTop = !miTop.Checked;
                miTop.Checked = _config.AlwaysOnTop;
                TopMost = _config.AlwaysOnTop;
                _config.Save();
            };

            var miNotif = new ToolStripMenuItem("Benachrichtigungen") { Checked = _config.Notifications };
            miNotif.Click += (_, __) =>
            {
                _config.Notifications = !miNotif.Checked;
                miNotif.Checked = _config.Notifications;
                _config.Save();
            };

            var miAuto = new ToolStripMenuItem("Autostart") { Checked = _config.AutoStart || AutostartService.IsEnabled() };
            miAuto.Click += (_, __) =>
            {
                var on = !miAuto.Checked;
                _config.AutoStart = on;
                miAuto.Checked = on;
                AutostartService.Apply(on);
                _config.Save();
            };

            var miCloseExit = new ToolStripMenuItem("Beim Schließen wirklich beenden") { Checked = !_config.CloseToTray };
            miCloseExit.Click += (_, __) =>
            {
                // ToolStripMenuItem toggles Checked itself; reflect to config:
                _config.CloseToTray = !miCloseExit.Checked;
                _config.Save();
            };

            var miDock = new ToolStripMenuItem("Dock (halbtransparent)") { Checked = _config.DockEnabled };
            miDock.Click += (_, __) =>
            {
                _config.DockEnabled = !miDock.Checked;
                miDock.Checked = _config.DockEnabled;
                _config.Save();
                ApplyDockAndHotCorner();
            };

            var miHot = new ToolStripMenuItem("Hot Corner") { Checked = _config.HotCornerEnabled };
            miHot.Click += (_, __) =>
            {
                _config.HotCornerEnabled = !miHot.Checked;
                miHot.Checked = _config.HotCornerEnabled;
                _config.Save();
                ApplyDockAndHotCorner();
            };

            var miSettings = new ToolStripMenuItem("Einstellungen …", null, (_, __) =>
            {
                using var dlg = new SettingsForm(_config, applyNow: ApplyDockAndHotCorner);
                dlg.TopMost = true;
                dlg.ShowDialog(this);
            });

            var miLog = new ToolStripMenuItem("Log anzeigen …", null, (_, __) => LogForm.ShowSingleton(this));

            var miExit = new ToolStripMenuItem("Beenden", null, (_, __) => { _realExit = true; _config.CloseToTray = false; _config.Save(); Close(); });

            menu.Items.AddRange(new ToolStripItem[] {
                miShow, miOpen, miCleanup,
                new ToolStripSeparator(),
                miTop, miNotif, miAuto, miCloseExit,
                new ToolStripSeparator(),
                miDock, miHot, miSettings, miLog,
                new ToolStripSeparator(),
                miExit
            });
            return menu;
        }

        private void RestorePosition()
        {
            if (_config.WindowLeft.HasValue && _config.WindowTop.HasValue)
            {
                var pt = new Point(_config.WindowLeft.Value, _config.WindowTop.Value);
                var bounds = Screen.AllScreens.Select(s => s.WorkingArea).FirstOrDefault(r => r.Contains(pt));
                if (bounds == Rectangle.Empty) StartPosition = FormStartPosition.WindowsDefaultLocation;
                else Location = pt;
            }
            else StartPosition = FormStartPosition.WindowsDefaultLocation;
        }

        private void SavePosition()
        {
            _config.WindowLeft = Left;
            _config.WindowTop = Top;
            _config.Save();
        }

        private void DropPanel_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = HasSupportedFormats(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private static bool HasSupportedFormats(IDataObject? data)
        {
            if (data == null) return false;
            return data.GetDataPresent(DataFormats.FileDrop)
                || data.GetDataPresent("FileGroupDescriptorW")
                || data.GetDataPresent("FileGroupDescriptor")
                || data.GetDataPresent("RenPrivateMessages");
        }

        private void Notify(string title, string text, ToolTipIcon icon, int timeoutMs)
        {
            try
            {
                if (!_config.Notifications) return;
                if (_tray.Icon == null) { try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { _tray.Icon = SystemIcons.Application; } }
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = icon;
                if (!_tray.Visible) _tray.Visible = true;
                _tray.ShowBalloonTip(timeoutMs);
            }
            catch { }
        }

        private void MinimizeToTray()
        {
            try
            {
                ShowInTaskbar = false;
                Hide();
                if (!_minimizeTipShown)
                {
                    Notify("DropZone", "Läuft weiter im Tray. Links‑Klick auf das Tray‑Icon zum Wiederherstellen.", ToolTipIcon.Info, 4000);
                    _minimizeTipShown = true;
                }
                Log.Info("Minimized to tray.");
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            try
            {
                ShowInTaskbar = true;
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                Activate();
                Log.Info("Restored from tray.");
            }
            catch { }
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
                _dropPanel.Invalidate();
            };
            if (_config.PulseAnimation) _pulseTimer.Start();
        }

        public async Task<List<string>> ProcessDropAsync(IDataObject data, ProgressBar? progressOverride = null, Label? statusOverride = null, bool triggerNotifications = true)
        {
            var created = new List<string>();
            var bar = progressOverride ?? _progress;
            var lbl = statusOverride ?? _status;

            try
            {
                bar.Style = ProgressBarStyle.Marquee;
                lbl.Text = "Verarbeite …";

                if (OutlookDragDrop.TryGetOutlookFiles(data, out var items) && items.Count > 0)
                {
                    long total = 0;
                    foreach (var a in items) total += a.Length ?? 0;

                    if (total > 0) { bar.Style = ProgressBarStyle.Continuous; bar.Value = 0; bar.Maximum = 1000; }

                    var prog = new Progress<long>(done =>
                    {
                        if (total > 0)
                        {
                            var pct = (int)Math.Clamp(done * 1000.0 / total, 0, 1000);
                            if (pct <= bar.Maximum) bar.Value = pct;
                        }
                    });

                    var result = await FileCopyService.SaveStreamsAsync(items, _config.TargetFolder, prog);
                    created.AddRange(result.CreatedFiles);
                }
                else if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    var paths = (string[])data.GetData(DataFormats.FileDrop);
                    var items2 = paths.Select(p => new FileCopyService.CopyItem { SourcePath = p, IsDirectory = Directory.Exists(p) }).ToList();

                    long total = 0;
                    try
                    {
                        total = items2.Where(i => !i.IsDirectory && File.Exists(i.SourcePath)).Sum(i => new FileInfo(i.SourcePath).Length)
                             + items2.Where(i => i.IsDirectory)
                                    .SelectMany(i => Directory.EnumerateFiles(i.SourcePath, "*", SearchOption.AllDirectories))
                                    .Sum(f => new FileInfo(f).Length);
                    }
                    catch { }

                    if (total > 0) { bar.Style = ProgressBarStyle.Continuous; bar.Value = 0; bar.Maximum = 1000; }

                    var prog2 = new Progress<long>(done =>
                    {
                        if (total > 0)
                        {
                            var pct = (int)Math.Clamp(done * 1000.0 / total, 0, 1000);
                            if (pct <= bar.Maximum) bar.Value = pct;
                        }
                    });

                    var result2 = await FileCopyService.CopyFileSystemAsync(items2, _config.TargetFolder, prog2);
                    created.AddRange(result2.CreatedFiles);
                }

                if (created.Count > 0)
                {
                    var txt = string.Join(Environment.NewLine, created);
                    try { Clipboard.SetText(txt); } catch { }
                    lbl.Text = $"{created.Count} Datei(en) kopiert. Pfad(e) in Zwischenablage.";
                    if (_config.Notifications && triggerNotifications)
                        Notify("DropZone", $"{created.Count} Datei(en) kopiert.", ToolTipIcon.Info, 3000);
                    StartPulse();
                }
                else
                {
                    lbl.Text = "Nichts kopiert.";
                }

                var removed = IndexStore.Cleanup(_config.TargetFolder, _config.DaysToKeep);
                if (removed > 0 && _config.Notifications && triggerNotifications)
                    Notify("DropZone", $"{removed} alte Datei(en) entfernt.", ToolTipIcon.Info, 2000);
            }
            catch (Exception ex)
            {
                lbl.Text = "Fehler";
                Log.Error("Fehler bei Drop-Verarbeitung", ex);
                MessageBox.Show(this, ex.Message, "Fehler beim Kopieren", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Value = 0;
            }

            return created;
        }

        private void ApplyDockAndHotCorner()
        {
            if (_config.DockEnabled)
            {
                if (_dock == null || _dock.IsDisposed) _dock = new DockForm(_config, this);
                _dock.ApplyConfig();
                _dock.Show();
            }
            else
            {
                if (_dock != null) { _dock.Close(); _dock = null; }
            }

            if (_config.HotCornerEnabled)
            {
                if (_hotCorner == null || _hotCorner.IsDisposed) _hotCorner = new HotCornerForm(_config, this);
                _hotCorner.ApplyConfig();
                _hotCorner.Show();
            }
            else
            {
                if (_hotCorner != null) { _hotCorner.Close(); _hotCorner = null; }
            }

            TopMost = _config.AlwaysOnTop;
            _dropPanel.Invalidate();
        }
    }
}
