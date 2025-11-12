using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace DropZoneApp
{
    public static class AutostartService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DropZoneApp";

        private static string StartupShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DropZoneApp.lnk");

        private const string ExtraArgs = "--minimized";

        public static void Apply(bool enable)
        {
            bool regOk = TryRegistry(enable);
            bool shOk  = TryStartupShortcut(enable);
            try { Log.Info($"Autostart {(enable ? "aktiviert" : "deaktiviert")}: Registry={(regOk?"ok":"fail")}, StartupShortcut={(shOk?"ok":"fail")}"); } catch { }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false))
                {
                    if (key?.GetValue(ValueName) is string s && s.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            try { if (File.Exists(StartupShortcutPath)) return true; } catch { }
            return false;
        }

        private static bool TryRegistry(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        var exe = Application.ExecutablePath;
                        key.SetValue(ValueName, """ + exe + "" " + ExtraArgs);
                    }
                    else key.DeleteValue(ValueName, false);
                    return true;
                }
            }
            catch (Exception ex) { try { Log.Error("Autostart registry failed", ex); } catch { } return false; }
        }

        private static bool TryStartupShortcut(bool enable)
        {
            try
            {
                var lnk = StartupShortcutPath;
                if (!enable)
                {
                    if (File.Exists(lnk)) File.Delete(lnk);
                    return true;
                }

                var exe = Application.ExecutablePath;
                var dir = Path.GetDirectoryName(exe) ?? "";
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return false;
                var shell = Activator.CreateInstance(type);
                if (shell == null) return false;
                var sc = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var scType = sc!.GetType();
                scType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
                scType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { dir });
                scType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                scType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { ExtraArgs });
                scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, Array.Empty<object>());
                return File.Exists(lnk);
            }
            catch (Exception ex) { try { Log.Error("Autostart shortcut failed", ex); } catch { } return false; }
        }
    }
}
