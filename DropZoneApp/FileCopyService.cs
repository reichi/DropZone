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
        private const int MaxBaseLen = 150; // Basisname max. 150 Zeichen (Extension kommt hinzu)

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
            try { total = files.Where(File.Exists).Sum(f => new FileInfo(f).Length); } catch { }
            long done = 0;
            var created = new List<string>();

            foreach (var src in files)
            {
                if (!File.Exists(src)) continue;

                var sanitized = SanitizeName(src); // bereits 150er‑Limit (Basis)
                var dest = UniqueDestination(targetFolder, sanitized); // bei Kollision ggf. Suffix & erneutes Kürzen

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
                var sanitized = SanitizeName(name); // bereits 150er‑Limit (Basis)
                var dest = UniqueDestination(targetFolder, sanitized); // bei Kollision ggf. Suffix & erneutes Kürzen

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
        /// Nur A–Z, a–z, 0–9, '_' und '.'.
        /// Extension wird separat bereinigt (A–Z, a–z, 0–9). Basisname wird auf 150 Zeichen begrenzt.
        /// </summary>
        public static string SanitizeName(string originalPathOrName)
        {
            var originalName = Path.GetFileName(originalPathOrName) ?? "file";
            string baseName = Path.GetFileNameWithoutExtension(originalName) ?? "file";
            string ext      = Path.GetExtension(originalName) ?? "";

            string cleanBase = FilterLettersDigitsUnderscoreDot(baseName);
            if (string.IsNullOrEmpty(cleanBase)) cleanBase = "file";

            string cleanExtName  = FilterLettersDigits(ext.TrimStart('.'));
            string finalExt      = string.IsNullOrEmpty(cleanExtName) ? "" : "." + cleanExtName;

            // Keine führenden/abschließenden Punkte/Unterstriche
            cleanBase = cleanBase.Trim('_').Trim('.');
            if (string.IsNullOrEmpty(cleanBase)) cleanBase = "file";

            // **Längenlimit:** Basisname max. 150 (Extension kommt hinzu)
            if (cleanBase.Length > MaxBaseLen)
                cleanBase = cleanBase.Substring(0, MaxBaseLen);

            return cleanBase + finalExt;
        }

        /// <summary>
        /// Liefert eindeutigen Zielpfad. Bei Kollision:
        /// 1) Suffix "_yyyyMMdd_HHmmssfff" anhängen (Basis ggf. kürzen, so dass Basis+Suffix ≤ 150).
        /// 2) Falls weiterhin Kollision: "_2", "_3", … anhängen (Basis erneut passend kürzen).
        /// </summary>
        private static string UniqueDestination(string targetFolder, string sanitizedFileName)
        {
            string name = Path.GetFileNameWithoutExtension(sanitizedFileName);
            string ext  = Path.GetExtension(sanitizedFileName);
            string dest = Path.Combine(targetFolder, name + ext);

            if (!File.Exists(dest)) return dest;

            string ts = "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
            string baseTrimmed = TrimBaseToLimit(name, ts.Length);
            string withTs = Path.Combine(targetFolder, baseTrimmed + ts + ext);
            if (!File.Exists(withTs)) return withTs;

            int i = 2;
            while (true)
            {
                string suffix = "_" + i.ToString();
                string base2 = TrimBaseToLimit(name, ts.Length + suffix.Length);
                string candidate = Path.Combine(targetFolder, base2 + ts + suffix + ext);
                if (!File.Exists(candidate)) return candidate;
                i++;
            }
        }

        /// <summary>
        /// Kürzt den Basisnamen so, dass Basis + (Suffixlänge) ≤ 150 bleibt.
        /// </summary>
        private static string TrimBaseToLimit(string baseName, int suffixLen)
        {
            int allowed = Math.Max(1, MaxBaseLen - suffixLen);
            if (baseName.Length > allowed)
                return baseName.Substring(0, allowed);
            return baseName;
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
