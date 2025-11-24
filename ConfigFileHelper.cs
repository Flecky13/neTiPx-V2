using System;
using System.IO;

namespace neTiPx
{
    public static class ConfigFileHelper
    {
        public static string GetConfigIniPath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "neTiPx");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, "config.ini");

                // If no config in AppData but one exists in program folder (installer placed), copy it on first run
                var old = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                try
                {
                    if (!File.Exists(target) && File.Exists(old))
                    {
                        File.Copy(old, target, false);
                    }
                }
                catch { }

                return target;
            }
            catch
            {
                // Fallback to app directory if AppData not available for some reason
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            }
        }
    }
}
