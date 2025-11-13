using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace DropZoneApp
{
    public sealed class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly Action? _applyNow;

        private TextBox _txtTarget = null!;
        private NumericUpDown _numDays = null!;
        private CheckBox _chkTop = null!;
        private CheckBox _chkAuto = null!;
        private CheckBox _chkCloseToTray = null!;
        private CheckBox _chkMinToTray = null!;   // Minimize → Tray (bleibt)

        private CheckBox _chkDock = null!;
        private NumericUpDown _numDockW = null!;
        private NumericUpDown _numDockH = null!;
        private NumericUpDown _numDockOp = null!;
        private NumericUpDown _numDockIcon = null!;

        private CheckBox _chkHot = null!;
        private ComboBox _cmbCorner = null!;
        private NumericUpDown _numHotSize = null!;
        private NumericUpDown _numHotOpacity = null!;
        private NumericUpDown _numHotIcon = null!;
        private ComboBox _cmbMonitors = null!;

        public SettingsForm(AppConfig config, Action? applyNow = null)
        {
            _config = config;
            _applyNow = applyNow;

            Text = "Einstellungen";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            Width = 760; Height = 640;
            TopMost = true;

            // --- Ordner & Aufräumen oben ---
            var lbl1 = new Label { Text = "Zielordner:", Left = 12, Top = 18, Width = 120 };
            _txtTarget = new TextBox { Left = 140, Top = 15, Width = 480, Text = _config.TargetFolder };
            var btnBrowse = new Button { Left = 626, Top = 14, Width = 40, Text = "…" };
            btnBrowse.Click += (_, __) =>
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = Directory.Exists(_txtTarget.Text) ? _txtTarget.Text : _config.TargetFolder };
                if (dlg.ShowDialog(this) == DialogResult.OK) _txtTarget.Text = dlg.SelectedPath;
            };

            var lbl2 = new Label { Text = "Aufräumen nach (Tagen):", Left = 12, Top = 56, Width = 180 };
            _numDays = new NumericUpDown { Left = 198, Top = 53, Width = 80, Minimum = 1, Maximum = 3650, Value = Math.Max(1, _config.DaysToKeep) };

            // --- Allgemein (ohne Benachrichtigungen) ---
            var grpGeneral = new GroupBox { Text = "Allgemein", Left = 380, Top = 96, Width = 350, Height = 100 };
            _chkTop   = new CheckBox { Left = 12, Top = 22, Width = 220, Text = "Immer im Vordergrund", Checked = _config.AlwaysOnTop };
            _chkCloseToTray = new CheckBox { Left = 12, Top = 46, Width = 150, Text = "Schließen → Tray", Checked = _config.CloseToTray };
            _chkMinToTray = new CheckBox { Left = 180, Top = 46, Width = 160, Text = "Minimieren → Tray", Checked = _config.MinimizeToTray };
            grpGeneral.Controls.AddRange(new Control[] { _chkTop, _chkCloseToTray, _chkMinToTray });

            // --- Drop‑Zone ---
            var grpDZ = new GroupBox { Text = "Drop‑Zone", Left = 12, Top = 96, Width = 350, Height = 170 };
            var lblDZIcon = new Label { Left = 12, Top = 24, Text = "Icongröße (px):", Width = 110 };
            var numDZIcon = new NumericUpDown { Left = 130, Top = 20, Width = 80, Minimum = 16, Maximum = 256, Value = Math.Max(16, Math.Min(256, _config.IconSizeDropzone)) };
            var btnDZColor = new Button { Left = 12, Top = 52, Width = 110, Text = "Rahmenfarbe…" };
            var numDZThick = new NumericUpDown { Left = 130, Top = 52, Width = 80, Minimum = 1, Maximum = 12, Value = Math.Max(1, _config.DropBorderThickness) };
            grpDZ.Controls.AddRange(new Control[] { lblDZIcon, numDZIcon, btnDZColor, numDZThick });

            // --- Dock ---
            var grpDock = new GroupBox { Text = "Dock (halbtransparent)", Left = 380, Top = 220, Width = 350, Height = 220 };
            _chkDock = new CheckBox { Left = 12, Top = 22, Width = 180, Text = "Dock aktivieren", Checked = _config.DockEnabled };
            var lblDW = new Label { Left = 12, Top = 52, Text = "Breite:", Width = 60 };
            _numDockW = new NumericUpDown { Left = 80, Top = 48, Width = 80, Minimum = 30, Maximum = 1000, Value = Math.Max(30, _config.DockWidth) };
            var lblDH = new Label { Left = 12, Top = 80, Text = "Höhe:", Width = 60 };
            _numDockH = new NumericUpDown { Left = 80, Top = 76, Width = 80, Minimum = 60, Maximum = 2000, Value = Math.Max(60, _config.DockHeight) };
            var lblDO = new Label { Left = 12, Top = 110, Text = "Transparenz (%):", Width = 100 };
            _numDockOp = new NumericUpDown { Left = 120, Top = 106, Width = 60, Minimum = 10, Maximum = 100, Value = (decimal)(_config.DockOpacity * 100) };
            var lblDIcon = new Label { Left = 12, Top = 140, Text = "Icongröße (px):", Width = 100 };
            _numDockIcon = new NumericUpDown { Left = 120, Top = 136, Width = 60, Minimum = 16, Maximum = 256, Value = Math.Max(16, Math.Min(256, _config.IconSizeDock)) };
            var btnDockColor = new Button { Left = 12, Top = 168, Width = 110, Text = "Rahmenfarbe…" };
            var numDockThick = new NumericUpDown { Left = 130, Top = 168, Width = 80, Minimum = 1, Maximum = 12, Value = Math.Max(1, _config.DockBorderThickness) };
            var _chkDockClick = new CheckBox { Left = 12, Top = 196, Width = 300, Text = "Klicks durchlassen (STRG=Verschieben)", Checked = _config.DockClickThrough };
            grpDock.Controls.AddRange(new Control[] { _chkDock, lblDW, _numDockW, lblDH, _numDockH, lblDO, _numDockOp, lblDIcon, _numDockIcon, btnDockColor, numDockThick, _chkDockClick });

            // --- Hot Corner ---
            var grpHot = new GroupBox { Text = "Hot Corner", Left = 12, Top = 280, Width = 350, Height = 220 };
            _chkHot = new CheckBox { Left = 12, Top = 22, Width = 200, Text = "Hot Corner aktivieren", Checked = _config.HotCornerEnabled };
            var lblCorner = new Label { Left = 12, Top = 52, Text = "Ecke:", Width = 60 };
            _cmbCorner = new ComboBox { Left = 80, Top = 48, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbCorner.Items.AddRange(new object[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" });
            _cmbCorner.SelectedItem = _config.HotCornerCorner;
            var lblSize = new Label { Left = 12, Top = 80, Text = "Größe (px):", Width = 80 };
            _numHotSize = new NumericUpDown { Left = 100, Top = 76, Width = 80, Minimum = 4, Maximum = 64, Value = Math.Max(4, _config.HotCornerSize) };
            var lblHotOp = new Label { Left = 12, Top = 108, Text = "Transparenz (%):", Width = 110 };
            _numHotOpacity = new NumericUpDown { Left = 130, Top = 104, Width = 60, Minimum = 1, Maximum = 100, Value = (decimal)(Math.Max(1.0, Math.Min(100.0, _config.HotCornerOpacity * 100))) };
            var lblHotIcon = new Label { Left = 12, Top = 136, Text = "Icongröße (px):", Width = 100 };
            _numHotIcon = new NumericUpDown { Left = 120, Top = 132, Width = 60, Minimum = 8, Maximum = 128, Value = Math.Max(8, Math.Min(128, _config.IconSizeHotCorner)) };
            var btnHotColor = new Button { Left = 12, Top = 160, Width = 110, Text = "Rahmenfarbe…" };
            var numHotThick = new NumericUpDown { Left = 130, Top = 160, Width = 80, Minimum = 1, Maximum = 12, Value = Math.Max(1, _config.HotBorderThickness) };

            // Monitor-Label & Combo ohne Überlappung
            var lblMon = new Label { Left = 12, Top = 188, Text = "Monitor:", Width = 80 };
            _cmbMonitors = new ComboBox { Left = 100, Top = 184, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            grpHot.Controls.AddRange(new Control[] {
                _chkHot, lblCorner, _cmbCorner, lblSize, _numHotSize,
                lblHotOp, _numHotOpacity, lblHotIcon, _numHotIcon,
                btnHotColor, numHotThick, lblMon, _cmbMonitors
            });

            // --- Pulse / Buttons ---
            var btnPulse = new CheckBox { Text = "Pulse‑Animation", Left = 380, Top = 450, Width = 180, Checked = _config.PulseAnimation };
            var lblPulse = new Label { Text = "Pulse‑Dauer (ms):", Left = 380, Top = 480, Width = 120 };
            var numPulse = new NumericUpDown { Left = 500, Top = 476, Width = 80, Minimum = 100, Maximum = 2000, Value = Math.Max(100, _config.PulseDurationMs) };

            var btnLog = new Button { Text = "Log anzeigen …", Left = 12, Top = 520, Width = 120 };
            btnLog.Click += (_, __) => LogForm.ShowSingleton(this);

            var btnClose = new Button { Text = "Schließen", Left = 634, Top = 560, Width = 96, DialogResult = DialogResult.OK };

            Controls.AddRange(new Control[] {
                lbl1, _txtTarget, btnBrowse, lbl2, _numDays,
                grpGeneral, grpDZ, grpDock, grpHot,
                btnPulse, lblPulse, numPulse, btnLog, btnClose
            });

            // Autostart separat
            _chkAuto = new CheckBox { Left = 380, Top = 420, Width = 220, Text = "Autostart", Checked = _config.AutoStart || AutostartService.IsEnabled() };
            _chkAuto.CheckedChanged += (_, __) => { _config.AutoStart = _chkAuto.Checked; AutostartService.Apply(_config.AutoStart); _config.Save(); };
            Controls.Add(_chkAuto);

            // --- Events / Persistenz ---
            _txtTarget.TextChanged += (_, __) => {
                if (!string.IsNullOrWhiteSpace(_txtTarget.Text)) { Directory.CreateDirectory(_txtTarget.Text); _config.TargetFolder = _txtTarget.Text; _config.Save(); _applyNow?.Invoke(); }
            };
            _numDays.ValueChanged  += (_, __) => { _config.DaysToKeep = (int)_numDays.Value; _config.Save(); };

            _chkTop.CheckedChanged += (_, __) => { _config.AlwaysOnTop = _chkTop.Checked; _config.Save(); _applyNow?.Invoke(); };
            _chkCloseToTray.CheckedChanged += (_, __) => { _config.CloseToTray = _chkCloseToTray.Checked; _config.Save(); };
            _chkMinToTray.CheckedChanged += (_, __) => { _config.MinimizeToTray = _chkMinToTray.Checked; _config.Save(); };

            _chkDock.CheckedChanged += (_, __) => { _config.DockEnabled = _chkDock.Checked; _config.Save(); _applyNow?.Invoke(); };
            _numDockW.ValueChanged  += (_, __) => { _config.DockWidth = (int)_numDockW.Value; _config.Save(); _applyNow?.Invoke(); };
            _numDockH.ValueChanged  += (_, __) => { _config.DockHeight = (int)_numDockH.Value; _config.Save(); _applyNow?.Invoke(); };
            _numDockOp.ValueChanged += (_, __) => { _config.DockOpacity = (double)_numDockOp.Value / 100.0; _config.Save(); _applyNow?.Invoke(); };
            _numDockIcon.ValueChanged += (_, __) => { _config.IconSizeDock = (int)_numDockIcon.Value; _config.Save(); _applyNow?.Invoke(); };

            _chkHot.CheckedChanged  += (_, __) => { _config.HotCornerEnabled = _chkHot.Checked; _config.Save(); _applyNow?.Invoke(); };
            _cmbCorner.SelectedIndexChanged += (_, __) => { _config.HotCornerCorner = _cmbCorner.SelectedItem?.ToString() ?? "TopLeft"; _config.Save(); _applyNow?.Invoke(); };
            _numHotSize.ValueChanged += (_, __) => { _config.HotCornerSize = (int)_numHotSize.Value; _config.Save(); _applyNow?.Invoke(); };
            _numHotOpacity.ValueChanged += (_, __) => { _config.HotCornerOpacity = (double)_numHotOpacity.Value / 100.0; _config.Save(); _applyNow?.Invoke(); };
            _numHotIcon.ValueChanged += (_, __) => { _config.IconSizeHotCorner = (int)_numHotIcon.Value; _config.Save(); _applyNow?.Invoke(); };

            // Monitore
            _cmbMonitors.Items.Clear();
            var screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                _cmbMonitors.Items.Add($"#{i+1}: {s.Bounds.Width}x{s.Bounds.Height} {(s.Primary ? "(Primär)" : "")}");
            }
            int idx = Math.Max(0, Math.Min(_config.HotCornerMonitor, _cmbMonitors.Items.Count - 1));
            if (_cmbMonitors.Items.Count > 0) _cmbMonitors.SelectedIndex = idx;
            _cmbMonitors.SelectedIndexChanged += (_, __) => { _config.HotCornerMonitor = _cmbMonitors.SelectedIndex; _config.Save(); _applyNow?.Invoke(); };

            Controls.Add(grpGeneral);
        }
    }
}
