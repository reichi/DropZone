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

            btnPanel.Controls.Add(_btnOpen);
            btnPanel.Controls.Add(_btnCopy);
            btnPanel.Controls.Add(_btnClear);

            Controls.Add(_rtb);
            Controls.Add(btnPanel);
            Controls.Add(_autoScroll);

            Load += LogForm_Load;
            FormClosed += LogForm_FormClosed;
            FormClosing += LogForm_FormClosing;

            Log.LineAdded += OnLogLine;
        }

        private void LogForm_Load(object? sender, EventArgs e)
        {
            string[] lines = Log.ReadAll();
            if (lines.Length > 0)
            {
                _rtb.AppendText(string.Join(Environment.NewLine, lines));
                _rtb.AppendText(Environment.NewLine);
            }
        }

        private void LogForm_FormClosed(object? sender, EventArgs e)
        {
            Log.LineAdded -= OnLogLine;
        }

        private void LogForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private static string Quote(string s) => "\"" + s + "\"";

        private void BtnOpen_Click(object? sender, EventArgs e)
        {
            try
            {
                string p = Log.GetLogPath();
                if (File.Exists(p))
                {
                    var psi = new ProcessStartInfo("explorer.exe", "/select," + Quote(p)) { UseShellExecute = true };
                    Process.Start(psi);
                }
                else
                {
                    string dir = Path.GetDirectoryName(p) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var psi = new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true };
                    Process.Start(psi);
                }
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
