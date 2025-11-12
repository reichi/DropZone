using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                var fileName = SanitizeName(Path.GetFileName(src));
                var dest = UniqueDestination(targetFolder, fileName);
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
                var safe = SanitizeName(name);
                var dest = UniqueDestination(targetFolder, safe);
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

        private static string UniqueDestination(string targetFolder, string fileName)
        {
            var basePath = Path.Combine(targetFolder, fileName);
            if (!File.Exists(basePath)) return basePath;

            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            int i = 2;
            while (true)
            {
                var cand = Path.Combine(targetFolder, $"{name}_{i}{ext}");
                if (!File.Exists(cand)) return cand;
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

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";

            // Leerzeichen → Unterstrich
            var s = name.Replace(' ', '_');

            // Ungültige OS-Zeichen ersetzen
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            // Sicher extra Sonderzeichen ersetzen (falls nicht im obigen Set)
            char[] extra = { '<','>',':','"','/','\\','|','?','*' };
            foreach (var ch in extra)
                s = s.Replace(ch, '_');

            s = s.Trim().TrimEnd('.');
            if (s.Length == 0) s = "unnamed";
            return s;
        }
    }
}
