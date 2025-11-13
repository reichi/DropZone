using System;
using System.IO;
using System.Text.Json;

namespace DropZoneApp
{
    public sealed class AppConfig
    {
        public string TargetFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dropzone");

        public int  DaysToKeep       { get; set; } = 7;
        public bool AlwaysOnTop      { get; set; } = true;
        public bool Notifications    { get; set; } = true;
        public bool CloseToTray    { get; set; } = true;  // Standard: an
        public bool MinimizeToTray { get; set; } = true;  // Standard: an
        public bool AutoStart      { get; set; } = true;  // Standard: an



        public int? WindowLeft       { get; set; }
        public int? WindowTop        { get; set; }

        public bool   DockEnabled    { get; set; } = false;
        public double DockOpacity    { get; set; } = 0.6;
        public int    DockWidth      { get; set; } = 60;
        public int    DockHeight     { get; set; } = 200;
        public int?   DockLeft       { get; set; }
        public int?   DockTop        { get; set; }
        public int    IconSizeDock   { get; set; } = 48;
        public bool   DockClickThrough { get; set; } = true;

        public string HotCornerCorner{ get; set; } = "TopLeft";
        public bool   HotCornerEnabled{ get; set; } = false;
        public int    HotCornerSize  { get; set; } = 8;
        public double HotCornerOpacity { get; set; } = 0.02;
        public bool   HotCornerBlink { get; set; } = true;
        public int    HotCornerMonitor { get; set; } = 0;
        public int    IconSizeHotCorner { get; set; } = 16;

        public int    IconSizeDropzone { get; set; } = 96;

        public string DropBorderColorHex { get; set; } = "#808080";
        public int    DropBorderThickness { get; set; } = 2;

        public string DockBorderColorHex { get; set; } = "#808080";
        public int    DockBorderThickness { get; set; } = 2;

        public string HotBorderColorHex  { get; set; } = "#808080";
        public int    HotBorderThickness { get; set; } = 1;

        public bool PulseAnimation { get; set; } = true;
        public int  PulseDurationMs { get; set; } = 350;

        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropZoneApp");
        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null)
                    {
                        Directory.CreateDirectory(cfg.TargetFolder);
                        return cfg;
                    }
                }
            }
            catch { }

            var def = new AppConfig();
            Directory.CreateDirectory(def.TargetFolder);
            def.Save();
            return def;
        }

        public void Save()
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }
}
