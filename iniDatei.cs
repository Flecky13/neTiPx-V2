using System.Runtime.InteropServices;
using System.Text;

namespace neTiPx
{
    public static class IniDatei
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder result, int size, string filePath);

        public static string Lesen(string section, string key, string defaultValue, string path)
        {
            var result = new StringBuilder(255);
            GetPrivateProfileString(section, key, defaultValue, result, 255, path);
            return result.ToString();
        }
    }
}
