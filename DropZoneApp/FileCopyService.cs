using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DropZoneApp
{
    public static class FileCopyService
    {
        public sealed class CopyItem
        {
            public string SourcePath { get; init; } = "";
            public bool IsDirectory { get; init; }
        }

        public sealed class CopyResult
        {
            public List<string> CreatedFiles { get; init; } = new();
            public long TotalBytes { get; init; }
        }

        public static async Task<CopyResult> CopyFileSystemAsync(IEnumerable<CopyItem> items, string targetFolder, IProgress<long>? progress = null)
        {
            Directory.CreateDirectory(targetFolder);

            var files = new List<string>();
            foreach (var it in items)
            {
                if (it.IsDirectory)
                    files.AddRange(EnumerateAllFiles(it.SourcePath));
                else
                    files.Add(it.SourcePath);
            }

            long total = 0;
            try { total = files.Where(File.Exists).Sum(f => new FileInfo(f).Length); } catch {}
            long done = 0;
            var created = new List<string>();

            foreach (var src in files)
            {
                if (!File.Exists(src)) continue;

                var sanitized = SanitizeName(src);
                var dest = UniqueDestination(targetFolder, sanitized); // Zeitstempel nur bei Kollision

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await CopyStreamAsync(File.OpenRead(src), File.Create(dest), l => { done += l; progress?.Report(done); });
                created.Add(dest);
                IndexStore.Add(targetFolder, dest);
            }

            return new CopyResult { CreatedFiles = created, TotalBytes = total };
        }

        public static async Task<CopyResult> SaveStreamsAsync(IReadOnlyList<(string FileName, Stream Content, long? Length)> streams, string targetFolder, IProgress<long>? progress = null)
        {
            Directory.CreateDirectory(targetFolder);
            long total = 0;
            try { total = streams.Where(s => s.Length.HasValue).Sum(s => s.Length!.Value); } catch { }
            long done = 0;
            var created = new List<string>();

            foreach (var (name, stream, length) in streams)
            {
                var sanitized = SanitizeName(name);
                var dest = UniqueDestination(targetFolder, sanitized); // Zeitstempel nur bei Kollision

                await CopyStreamAsync(stream, File.Create(dest), l => { done += l; progress?.Report(done); });
                created.Add(dest);
                IndexStore.Add(targetFolder, dest);
            }

            return new CopyResult { CreatedFiles = created, TotalBytes = total };
        }

        private static IEnumerable<string> EnumerateAllFiles(string folder)
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                yield return f;
        }

        /// <summary>
        /// Erlaubt nur A–Z, a–z, 0–9, '_' und '.' im gesamten Dateinamen.
        /// Extension wird separat bereinigt (nur A–Z, a–z, 0–9).
        /// </summary>
        public static string SanitizeName(string originalPathOrName)
        {
            var originalName = Path.GetFileName(originalPathOrName) ?? "file";
            string baseName = Path.GetFileNameWithoutExtension(originalName) ?? "file";
            string ext      = Path.GetExtension(originalName) ?? "";

            string cleanBase = FilterLettersDigitsUnderscoreDot(baseName);
            if (string.IsNullOrEmpty(cleanBase)) cleanBase = "file";

            string cleanExt  = FilterLettersDigits(Path.GetExtension(originalName).TrimStart('.'));
            string finalExt  = string.IsNullOrEmpty(cleanExt) ? "" : "." + cleanExt;

            // Keine führenden/trailing Punkte/Unterstriche
            cleanBase = cleanBase.Trim('_').Trim('.');
            if (string.IsNullOrEmpty(cleanBase)) cleanBase = "file";

            return cleanBase + finalExt;
        }

        /// <summary>
        /// Gibt einen eindeutigen Zieldateinamen zurück.
        /// Erst ohne Zeitstempel; bei Kollisionen "_yyyyMMdd_HHmmssfff";
        /// falls immer noch Kollision, zusätzlich "_2", "_3", ...
        /// </summary>
        private static string UniqueDestination(string targetFolder, string sanitizedFileName)
        {
            string full = Path.Combine(targetFolder, sanitizedFileName);
            if (!File.Exists(full)) return full;

            string name = Path.GetFileNameWithoutExtension(sanitizedFileName);
            string ext  = Path.GetExtension(sanitizedFileName);
            string ts   = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");

            string withTs = Path.Combine(targetFolder, $"{name}_{ts}{ext}");
            if (!File.Exists(withTs)) return withTs;

            int i = 2;
            while (true)
            {
                string candidate = Path.Combine(targetFolder, $"{name}_{ts}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
                i++;
            }
        }

        private static async Task CopyStreamAsync(Stream src, Stream dst, Action<long>? onProgress)
        {
            using (src)
            using (dst)
            {
                var buffer = new byte[1024 * 1024];
                int r;
                while ((r = await src.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, r));
                    onProgress?.Invoke(r);
                }
                await dst.FlushAsync();
            }
        }

        private static string FilterLettersDigitsUnderscoreDot(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if ((ch >= 'A' && ch <= 'Z') ||
                    (ch >= 'a' && ch <= 'z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '_' || ch == '.')
                {
                    sb.Append(ch);
                }
                else if (char.IsWhiteSpace(ch) || ch == '-')
                {
                    sb.Append('_');
                }
                // andere Zeichen werden verworfen
            }
            // Mehrere Unterstriche zu einem
            string r = sb.ToString();
            while (r.Contains("__")) r = r.Replace("__", "_");
            return r;
        }

        private static string FilterLettersDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if ((ch >= 'A' && ch <= 'Z') ||
                    (ch >= 'a' && ch <= 'z') ||
                    (ch >= '0' && ch <= '9'))
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
