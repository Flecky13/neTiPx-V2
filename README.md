neTiPx — Netzwerk-Info Tool (Visual Studio - C# - WPF)
Kurzbeschreibung
neTiPx ist ein kleines Windows-Tool (geschrieben in Visual Studio - C# - WPF), das ausgewählte Netzwerkadapter aus einer INI-Datei liest und deren aktuelle Informationen anzeigt. Die Anwendung zeigt unter anderem die externe IPv4-Adresse, lokale IPv4-/IPv6-Adressen, MAC-Adresse, Gateway und DNS-Server an.

Wesentliche Funktionen
Ermittlung der externen IPv4-Adresse (mehrere Fallback-Dienste)
Anzeige pro Adapter: Name (NetConnectionID), MAC, IPv4 (alle), IPv6 (alle), Gatewayv4, Gatewayv6 DNSv4, DNSv6
Adapterliste über config.ini konfigurierbar
Automatische Aktualisierung im 1 min Intervall

config.ini
[Network]
Adapter1 = Ethernet
Adapter2 = WLAN

Hinweis: AdapterX muss den NetConnectionID-Namen der Adapter enthalten (z. B. "WLAN", "Ethernet"). Der Eintrag

Siehe LICENSE im Repository.

Kontakt
Für Fragen zur Migration oder zum Code bitte im Repo kommentieren oder mir hier kurz Bescheid geben.# neTiPx-V2
# neTiPx — Netzwerk-Info Tool (C#)
