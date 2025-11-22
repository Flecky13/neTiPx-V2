## neTiPx — Netzwerk-Info Tool (C#)

![Alt text](Images/APP.png)

Kurze Beschreibung
-------------------
`neTiPx` ist ein kleines Windows-Tool (WPF, C#), das Netzwerkadapter aus einer Konfigurationsdatei lädt, deren Status anzeigt und einfache IP-Konfigurationen verwalten kann. Die App zeigt u. a. externe IPv4-Adresse, lokale IPv4-/IPv6-Adressen, MAC-Adresse, Gateway und DNS-Server an.

Wesentliche Funktionen
----------------------
- Anzeige von Netzwerkdetails pro Adapter (Name, MAC, IPs, Gateways, DNS)
- Ermittlung der externen IPv4-Adresse über mehrere Fallback-Dienste
- Auswahl von bis zu zwei physikalischen Adaptern über Config
- IP-Profile: mehrere vordefinierbare IP-Konfigurationen als Tabs (DHCP oder manuell)
- Anwenden einer IP-Konfiguration auf eine Netzwerkkarte (führt `netsh` aus, erfordert Administratorrechte)

Konfiguration (`config`)
---------------------------
Die App liest und speicher alles in `config.ini` aus dem Anwendungsverzeichnis. Beispiel:

```
Adapter1 = Ethernet
Adapter2 = WLAN
IpProfileNames = Office,Home
Office.Adapter = Ethernet
Office.Mode = Manual
Office.IP = 192.168.1.50
Office.Subnet = 255.255.255.0
Office.GW = 192.168.1.1
Office.DNS = 8.8.8.8
```
![Alt text](Images/Config.png)

Hinweis: Die Werte in `Adapter1`/`Adapter2` sollten den `NetConnectionID`-Namen der Adapter entsprechen (z. B. "Ethernet", "WLAN" oder die Anzeige "Name - Description", je nach System).

IP Settings (Config-Fenster)
---------------------------
Bereich `IP Settings`, in dem du mehrere IP-Profile als Tabs anlegen kannst. Jedes Profil enthält:

- Ein editierbarer Profilname (wird als Schlüssel in `config.ini` verwendet)
- Auswahl eines Adapters (gefüllt aus `Adapter1`/`Adapter2`)
- Modus: `DHCP` oder `Manuell` (nur IPv4)
- Bei `Manuell`: Felder für `IP`, `Subnetz`, `Gateway`, `DNS`

![Alt text](Images/IP_Settings.png)


Anwenden einer Konfiguration
----------------------------
- Wähle das gewünschte Profil-Tab und klicke `Anwenden`.
- Die Konfiguration wird zunächst auf die Netzwerkkarte geschrieben.


Weitere Hinweise
----------------
- Für das Anwenden von IP-Konfigurationen wird `netsh` verwendet; die App fordert beim Ausführen der Änderung Administratorrechte an.
- Beim Speichern werden die IP-Profile in `config.ini` (Schlüssel `IpProfileNames` und `<ProfileName>.<Feld>`) persistiert.

Lizenz & Kontakt
----------------
Siehe `LICENSE` im Repository. Für Fragen zum Code bitte Issues/PRs im Repo verwenden.
