## neTiPx — Netzwerk-Info Tool (C#)

![Alt text](Images/APP.png)

Kurze Beschreibung
-------------------
`neTiPx` ist ein kleines Windows-Tool (WPF, C#), das Netzwerkadapter aus einer Konfigurationsdatei lädt, deren Status anzeigt und einfache IP-Konfigurationen verwalten kann. Die App läuft im System Tray und zeigt u. a. externe IPv4-Adresse, lokale IPv4-/IPv6-Adressen, MAC-Adresse, Gateway und DNS-Server an.

Wesentliche Funktionen
----------------------

### Netzwerk-Übersicht (Tray-Popup)
- **Automatische Anzeige** bei Mausüberfahrt über das Tray-Icon
- Anzeige der **externen IPv4-Adresse** (mit Fallback-Diensten)
- Details zu **zwei konfigurierbaren Netzwerkadaptern**:
  - Name, MAC-Adresse
  - IPv4/IPv6-Adressen
  - Gateway (IPv4/IPv6)
  - DNS-Server

### Adapter-Auswahl
- Auswahl von **bis zu 2 physikalischen Netzwerkadaptern**
- Automatische Erkennung aller verfügbaren Adapter

### IP-Profil-Verwaltung
- **Mehrere IP-Konfigurationen** als separate Tabs (max. 10)
- Jedes Profil mit editierbarem Namen
- **Modus-Auswahl**: DHCP oder Manuell (IPv4)
- Bei manueller Konfiguration: IP, Subnetz, Gateway, DNS
- **Gateway-Erreichbarkeitsprüfung**
  - Zeigt Ping-Status mit RTT in ms
- **Ein-Klick-Anwendung** auf Netzwerkkarte (erfordert Admin-Rechte)

### Ping-Monitor (Tools → Ping)
- **Bis zu 6 gleichzeitige Ping-Überwachungen**
- Unterstützt **IPv4-Adressen UND DNS-Hostnamen** (z.B. google.com)
- Pro Eintrag:
  - Aktivierung per Checkbox
  - Eingabe: IP-Adresse oder Hostname
  - Live-Statistiken: Startzeit, fehlende Pakete, Paketverlust %, aktueller Status
  - RTT-Anzeige in ms bei erfolgreichen Pings
  - Reset-Funktion (↻) und Löschen-Funktion pro Zeile
- **Hintergrund-Modus**:
  - Im Hintergrund Ping-Überwachung weiterlaufen

Konfiguration (`config.ini`)
---------------------------
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


## IP Settings (Config-Fenster)
---------------------------
Bereich `IP Settings`, in dem du mehrere IP-Profile als Tabs anlegen kannst. Jedes Profil enthält:

- Ein editierbarer Profilname (wird als Schlüssel in `config.ini` verwendet)
- Auswahl eines Adapters (gefüllt aus `Adapter1`/`Adapter2`)
- Modus: `DHCP` oder `Manuell` (nur IPv4)
- Bei `Manuell`: Felder für `IP`, `Subnetz`, `Gateway`, `DNS`

![Alt text](Images/IP_Settings.png)


## Anwenden einer Konfiguration
----------------------------
- Wähle das gewünschte Profil-Tab und klicke `Anwenden`.
- Die Konfiguration wird zunächst auf die Netzwerkkarte geschrieben.


## Weitere Hinweise
----------------
- Für das Anwenden von IP-Konfigurationen wird `netsh` verwendet; die App fordert beim Ausführen der Änderung Administratorrechte an.
- Beim Speichern werden die IP-Profile in `config.ini` (Schlüssel `IpProfileNames` und `<ProfileName>.<Feld>`) persistiert.

### Lizenz & Kontakt
----------------
Siehe `LICENSE` im Repository. Für Fragen zum Code bitte Issues/PRs im Repo verwenden.

https://buymeacoffee.com/pedrotepe
