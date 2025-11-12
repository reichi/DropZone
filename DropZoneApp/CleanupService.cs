using System;
using System.Windows.Forms;

namespace DropZoneApp
{
    public sealed class CleanupService : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly AppConfig _config;
        private readonly Action<string>? _notify;

        public CleanupService(AppConfig cfg, Action<string>? notify)
        {
            _config = cfg;
            _notify = notify;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 60 * 60 * 1000; // hourly
            _timer.Tick += (s, e) => Run();
            _timer.Start();
        }

        public void Run()
        {
            try
            {
                var removed = IndexStore.Cleanup(_config.TargetFolder, _config.DaysToKeep);
                if (removed > 0 && _config.Notifications)
                    _notify?.Invoke($"{removed} Datei(en) automatisch entfernt.");
            }
            catch { }
        }

        public void Dispose()
        {
            try { _timer.Stop(); } catch {}
            _timer.Dispose();
        }
    }
}
