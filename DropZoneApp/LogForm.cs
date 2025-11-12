using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace DropZoneApp
{
    public sealed class LogForm : Form
    {
        private readonly RichTextBox _rtb;
        private readonly CheckBox _autoScroll;
        private Button _btnOpen = null!;
        private Button _btnCopy = null!;
        private Button _btnClear = null!;

        private static LogForm? _instance;
        public static void ShowSingleton(IWin32Window? owner = null)
        {
            if (_instance == null || _instance.IsDisposed) _instance = new LogForm();
            _instance.TopMost = true;
            if (owner != null) _instance.StartPosition = FormStartPosition.CenterParent;
            _instance.Show(owner);
            _instance.BringToFront();
            _instance.Activate();
        }

        public LogForm()
        {
            Text = "DropZone - Log";
            Width = 800;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;

            _rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false };
            _autoScroll = new CheckBox { Text = "Auto-Scroll", Dock = DockStyle.Top, Checked = true };

            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            _btnOpen = new Button { Text = "Im Explorer öffnen" };
            _btnOpen.Click += BtnOpen_Click;
            _btnCopy = new Button { Text = "Alles kopieren" };
            _btnCopy.Click += BtnCopy_Click;
            _btnClear = new Button { Text = "Log leeren" };
            _btnClear.Click += BtnClear_Click;

            btnPanel.Controls.AddRange(new Control[] { _btnOpen, _btnCopy, _btnClear });

            Controls.AddRange(new Control[] { _rtb, btnPanel, _autoScroll });

            Load += (s,e) =>
            {
                string[] lines = Log.ReadAll();
                if (lines.Length > 0)
                {
                    _rtb.AppendText(string.Join(Environment.NewLine, lines));
                    _rtb.AppendText(Environment.NewLine);
                }
            };
            FormClosed += (s,e) => Log.LineAdded -= OnLogLine;
            FormClosing += (s,e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };

            Log.LineAdded += OnLogLine;
        }

        private void BtnOpen_Click(object? sender, EventArgs e)
        {
            try
            {
                string p = Log.GetLogPath();
                if (File.Exists(p))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,"" + p + """) { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(p) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)) { UseShellExecute = true });
            }
            catch { }
        }

        private void BtnCopy_Click(object? sender, EventArgs e)
        {
            try { Clipboard.SetText(_rtb.Text); } catch { }
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            Log.Clear();
            _rtb.Clear();
        }

        private void OnLogLine(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(OnLogLine), line); return; }
            _rtb.AppendText(line + Environment.NewLine);
            if (_autoScroll.Checked)
            {
                _rtb.SelectionStart = _rtb.Text.Length;
                _rtb.ScrollToCaret();
            }
        }
    }
}
