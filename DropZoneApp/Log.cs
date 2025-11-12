using System;
using System.IO;

namespace DropZoneApp
{
    public static class Log
    {
        private static readonly object _sync = new();
        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropZoneApp", "logs");
        private static readonly string _path = Path.Combine(_dir, "app.log");

        public static event Action<string>? LineAdded;

        public static string GetLogPath() => _path;

        public static void Info(string msg)  => Write("INFO",  msg);
        public static void Warn(string msg)  => Write("WARN",  msg);
        public static void Error(string msg, Exception? ex = null)
            => Write("ERROR", ex == null ? msg : $"{msg} :: {ex.GetType().Name}: {ex.Message}");

        public static void Clear()
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(_path, string.Empty);
            }
            Raise("--- log cleared ---");
        }

        public static string[] ReadAll()
        {
            try
            {
                Directory.CreateDirectory(_dir);
                if (!File.Exists(_path)) return Array.Empty<string>();
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();
                return content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            }
            catch { return Array.Empty<string>(); }
        }

        private static void Write(string level, string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
            lock (_sync)
            {
                Directory.CreateDirectory(_dir);
                using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs))
                {
                    sw.WriteLine(line);
                    sw.Flush();
                }
            }
            Raise(line);
        }

        private static void Raise(string line)
        {
            try { LineAdded?.Invoke(line); } catch { }
        }
    }
}
