using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DropZoneApp
{
    public static class IndexStore
    {
        private const string IndexFileName = ".dropzone_index.json";

        private sealed class IndexModel
        {
            public List<Item> Items { get; set; } = new();
        }

        private sealed class Item
        {
            public string RelPath { get; set; } = "";
            public DateTime UtcAdded { get; set; }
        }

        private static string GetIndexPath(string target) => Path.Combine(target, IndexFileName);

        public static void Add(string targetFolder, string absolutePath)
        {
            var idx = Load(targetFolder);
            var rel = Path.GetRelativePath(targetFolder, absolutePath);
            idx.Items.Add(new Item { RelPath = rel, UtcAdded = DateTime.UtcNow });
            Save(targetFolder, idx);
        }

        public static int Cleanup(string targetFolder, int daysToKeep)
        {
            var idx = Load(targetFolder);
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, daysToKeep));
            int removed = 0;
            var kept = new List<Item>();

            foreach (var it in idx.Items)
            {
                if (it.UtcAdded <= cutoff)
                {
                    var full = Path.Combine(targetFolder, it.RelPath);
                    try { if (File.Exists(full)) { File.Delete(full); removed++; } }
                    catch { kept.Add(it); continue; }
                }
                else kept.Add(it);
            }

            idx.Items = kept;
            Save(targetFolder, idx);
            return removed;
        }

        private static IndexModel Load(string targetFolder)
        {
            Directory.CreateDirectory(targetFolder);
            var p = GetIndexPath(targetFolder);
            if (!File.Exists(p)) return new IndexModel();
            try
            {
                return JsonSerializer.Deserialize<IndexModel>(File.ReadAllText(p)) ?? new IndexModel();
            }
            catch { return new IndexModel(); }
        }

        private static void Save(string targetFolder, IndexModel index)
        {
            var p = GetIndexPath(targetFolder);
            File.WriteAllText(p, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
