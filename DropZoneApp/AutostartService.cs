using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Forms;

namespace DropZoneApp
{
    public static class AutostartService
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VALUE_NAME = "DropZoneApp";

        /// <summary>
        /// Aktiviert/Deaktiviert den Autostart (HKCU\...\Run). Pfad zur aktuellen EXE wird hinterlegt.
        /// </summary>
        public static void Apply(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true) ?? Registry.CurrentUser.CreateSubKey(RUN_KEY, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    // In Anführungszeichen, falls Leerzeichen im Pfad
                    string value = $"\"{exePath}\"";
                    key.SetValue(VALUE_NAME, value, RegistryValueKind.String);
                }
                else
                {
                    if (key.GetValue(VALUE_NAME) != null)
                        key.DeleteValue(VALUE_NAME, false);
                }
            }
            catch
            {
                // Keine Exception nach außen – UI bleibt responsiv
            }
        }

        /// <summary>
        /// Prüft, ob aktuell ein Autostart-Eintrag existiert.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false);
                if (key == null) return false;
                return key.GetValue(VALUE_NAME) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
