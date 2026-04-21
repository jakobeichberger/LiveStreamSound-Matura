# LiveStreamSound-Matura

> 🇩🇪 Kabellose Audio-Verteilung für Matura-Prüfungen — Host/Client über Netzwerk statt Kabel.
> 🇬🇧 Wireless audio distribution for school final exams — LAN host/client instead of cables.

English version: [README.en.md](README.en.md) · Lehrer-Anleitung: [docs/Anleitung.md](docs/Anleitung.md)

---

## Was das macht

Für die Englisch-Matura müssen Hörverstehens-Audiodateien in zusammengelegten Modulräumen abgespielt werden. Bisher wurde dafür ein Kabel durch die gesamte Klasse verlegt — umständlich und störanfällig.

**LiveStreamSound** besteht aus zwei Apps:

- **Host** läuft auf dem Lehrer-Laptop. Der Ton, der auf dem Host abgespielt wird (z.B. in VLC oder Browser), wird per WLAN/LAN an alle Clients gestreamt.
- **Client** läuft auf den Raum-Laptops, die per HDMI mit Beamer/Lautsprechern verbunden sind. Der Client empfängt den Stream und gibt ihn synchron aus.

Lehrer steuern alles zentral: Lautstärke pro Raum, Stummschaltung, HDMI-Ausgabegerät-Wahl, Verbindung trennen.

## Features

- 🎯 System-Audio-Loopback (WASAPI) — alles was der Host abspielt wird gestreamt
- 🔒 6-stelliger Session-Code, keine Passwörter
- 🔍 Automatische Discovery per mDNS + Fallback manuelle IP + QR-Code
- ⏱️ Synchrone Wiedergabe auf allen Clients (Timestamp-basierter Jitter-Buffer)
- 🎚️ Pro-Client Fernsteuerung: Lautstärke, Stumm, Ausgabegerät, Kick
- 🏷️ Automatische Erkennung der Raum-Nummer aus dem Hostnamen (`HP-KB-017` → „Raum 017")
- 🌐 Deutsch + Englisch, zur Laufzeit umschaltbar
- 🌓 Hell- und Dunkelmodus (Fluent / Windows-11 Optik)
- 🩺 Verbindungs-Qualitäts-Anzeige mit Erklärungen bei Problemen
- 📓 Fehlerlog in-app und auf Datei (`%LOCALAPPDATA%\LiveStreamSound\...\logs\`)
- ❓ Eingebaute Bedienungshilfe in der GUI
- 📦 MSI-Installer für Host und Client (Firewall-Regeln automatisch)

## Technik-Stack

- **.NET 10** + **WPF** (Windows-only)
- **WPF-UI** 3.0 (Fluent Design)
- **NAudio** 2.2 für WASAPI-Capture und -Playback
- **Concentus** 2.2 für Opus Encoding/Decoding (reines C#, keine native DLL)
- **Makaretu.Dns.Multicast** für mDNS Service Discovery
- **QRCoder** für QR-Code-Generierung
- **CommunityToolkit.Mvvm** für MVVM mit Source Generators
- **WiX Toolset 5** für MSI-Installer

## Projekt-Struktur

```
LiveStreamSound-Matura/
├── LiveStreamSound.slnx
├── Directory.Build.props        # Gemeinsame Build-Einstellungen
├── src/
│   ├── LiveStreamSound.Shared/  # Protokoll, Audio-Packet, i18n, Diagnostik
│   ├── LiveStreamSound.Host/    # Lehrer-App: capture + server
│   └── LiveStreamSound.Client/  # Raum-App: receive + HDMI-Ausgabe
├── installer/
│   ├── Host/                    # WiX MSI für Host
│   └── Client/                  # WiX MSI für Client
└── docs/
    └── Anleitung.md             # Lehrer-Anleitung
```

## Bauen

Voraussetzungen: **Windows** (wegen WPF) + **.NET 10 SDK**.
Für MSI zusätzlich: `dotnet tool install --global wix --version 5.0.2`

```powershell
# Vom Repo-Root aus:
dotnet restore
dotnet build -c Release

# MSI bauen (erzeugt .msi unter installer/Host/bin/Release/ bzw. installer/Client/bin/Release/)
dotnet build installer/Host       -c Release
dotnet build installer/Client     -c Release
```

> Die Host- und Client-Projekte haben `EnableWindowsTargeting=true`, dadurch klappen `dotnet restore` und **compile** der Code-Projekte auch auf macOS/Linux. Ausgeführt werden kann nur unter Windows (wegen WPF + WASAPI).
>
> Die WiX-Installer-Projekte (`installer/Host`, `installer/Client`) sind **Windows-only** (WiX Toolset selbst unterstützt nichts anderes). Auf macOS/Linux schlägt deren Build fehl — das ist erwartet. Bauen nur auf Windows:
> ```powershell
> dotnet build src/LiveStreamSound.Shared src/LiveStreamSound.Host src/LiveStreamSound.Client -c Release
> dotnet build installer/Host installer/Client -c Release
> ```
> Auf Nicht-Windows-Systemen kann man per `dotnet build src/...` alle Code-Projekte einzeln bauen.

## Protokoll

**Discovery (mDNS):** `_livestreamsound._tcp`, TXT-Records `v` (Protocol-Version), `name` (Session-Name).

**Control-Channel (TCP, Port 5000):** JSON-Messages mit `type`-Diskriminator. Erste Message Client → Host: `HELLO {code, clientName, protocolVersion}`. Antwort: `WELCOME {clientId, audioUdpPort, sampleRate, channels, audioCodec, serverTimeMs}` oder `AUTH_FAIL {reason}`.

**Audio-Channel (UDP, Port 5001):** Binäres Paket
```
[Magic "LSSA" 4B][Version 1B][PayloadType 1B][PayloadLen 2B][Seq 4B][ServerTimestampMs 8B][Opus-Payload]
```
Frames sind 20 ms (960 Samples @ 48 kHz).

**Sync:** Client berechnet Clock-Offset zum Server via PING/PONG. Jedes Audio-Frame wird bei `serverTimestamp + 100 ms` in lokaler Zeit abgespielt → synchron auf allen Clients.

## Status

Erste Version (0.1.0) — alle Kernkomponenten implementiert und kompiliert, Ende-zu-Ende-Test mit mehreren echten Clients ausstehend (Hardware wird beim nächsten Matura-Testdurchlauf erprobt).

## Lizenz

Privat / schulisches Projekt. Fremdbibliotheken unter ihren jeweiligen Lizenzen (MIT/BSD).
