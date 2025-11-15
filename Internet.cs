using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;

namespace neTiPx
{
    public class Internet 
    { 
        public string IPAdresse { get; private set; } = "Unbekannt";
         
        private readonly string[] ipServices =
        {
            "https://api.ipify.org",
            "http://api.ipify.org",
            "https://ifconfig.me/ip"
        };

        public async Task LadeExterneIPAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            foreach (var url in ipServices)
            {
                try
                {
                    Debug.WriteLine($"[Internet] Versuche: {url}");
                    var response = await client.GetStringAsync(url);
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        IPAdresse = response.Trim();
                        Debug.WriteLine($"[Internet] Erfolg: {IPAdresse}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Internet] Fehler bei {url}: {ex.Message}");
                }
            }

            IPAdresse = "Fehler: keine Verbindung";
            Debug.WriteLine("[Internet] Kein Dienst lieferte eine IP-Adresse.");
        }
    }
}
