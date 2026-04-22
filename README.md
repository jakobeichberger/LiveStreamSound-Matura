# LiveStreamSound-Matura

> 🇩🇪 Kabellose Audio-Verteilung für Matura-Prüfungen — **eine** App, beim Start wählt man *senden* oder *empfangen*.
> 🇬🇧 Wireless audio distribution for school final exams — **one** app that asks whether to send or receive at launch.

English version: [README.en.md](README.en.md) · Lehrer-Anleitung: [docs/Anleitung.md](docs/Anleitung.md)

---

## Was das macht

Für die Englisch-Matura müssen Hörverstehens-Audiodateien in zusammengelegten Modulräumen abgespielt werden. Bisher wurde dafür ein Kabel durch die gesamte Klasse verlegt — umständlich und störanfällig.

**LiveStreamSound** ist eine einzige Windows-App. Beim Start entscheidest du:

- **📡 Ton senden** — dein Laptop ist der Host. Der Ton, der auf dem PC abgespielt wird (VLC, Browser, …), wird per WLAN/LAN an alle Clients gestreamt.
- **🎧 Ton empfangen** — dein Laptop ist Client am Beamer/Lautsprecher. Er empfängt den Stream synchron und gibt ihn über HDMI aus.

Dieselbe `.exe`. Rolle kann zur Laufzeit im Burger-Menü gewechselt werden.

## Features

- 🎯 System-Audio-Loopback (WASAPI) — alles was auf dem Host abgespielt wird, wird gestreamt
- 🔒 6-stelliger Session-Code, keine Passwörter
- 🔍 Automatische Discovery per mDNS + Fallback manuelle IP
- 📨 **Host kann Clients aktiv einladen** (Client bestätigt mit Accept/Reject-Dialog) — Lehrer muss nicht mehr zum Raum-PC gehen
- ✨ **Idle-Client-Toasts** im Host-Dashboard: wartende Raum-PCs erscheinen unten rechts als animierte Karten mit »+« zum Hinzufügen — kein Dialog-Wechsel nötig
- 👨‍🏫 **Lehrer-Modus / Techniker-Modus** im Client-Dashboard: Standard-Ansicht zeigt nur einen pulsierenden Heartbeat + Klartext-Status, Techniker-Ansicht entfaltet Sparklines und alle Metriken
- 🔇 **Auto-Mute am Host**: Lehrer-Lautsprecher werden während der Session stummgeschaltet (Stream zu Clients läuft weiter), beim Stoppen wird der Vorzustand wiederhergestellt
- ⏱️ Synchrone Wiedergabe auf allen Clients (Timestamp-basierter Jitter-Buffer, NTP-like Clock-Sync)
- 🎚️ Pro-Client Fernsteuerung: Lautstärke, Stumm, Ausgabegerät, Kick
- 🏷️ Automatische Erkennung der Raum-Nummer aus dem Hostnamen (`HP-KB-017` → „Raum 017"), kategorisiert nach Klassenraum / Werkstatt / Sonstige
- 🌐 Deutsch + Englisch, zur Laufzeit umschaltbar
- 🌓 Hell- und Dunkelmodus (folgt System-Theme beim Start, Fluent-/Windows-11-Optik)
- 🩺 Verbindungs-Qualitäts-Anzeige mit Klartext-Problem-Erklärungen
- 📓 Fehlerlog in-app und auf Datei (`%LOCALAPPDATA%\LiveStreamSound\...\logs\`)
- ❓ Eingebaute Bedienungshilfe in der GUI
- 📦 **Single MSI-Installer mit gebundeltem .NET 10 Runtime** — keine separate .NET-Installation nötig, Firewall-Regeln automatisch

## Technik-Stack

- **.NET 10** + **WPF** (Windows-only)
- **WPF-UI** 3.0 (Fluent Design)
- **NAudio** 2.2 für WASAPI-Capture und -Playback
- **Concentus** 2.2 für Opus Encoding/Decoding (reines C#, keine native DLL)
- **Makaretu.Dns.Multicast** für mDNS Service Discovery
- **CommunityToolkit.Mvvm** für MVVM mit Source Generators
- **WiX Toolset 5** für single-Bundle MSI-Installer

## Projekt-Struktur

```
LiveStreamSound-Matura/
├── LiveStreamSound.slnx
├── Directory.Build.props
├── src/
│   ├── LiveStreamSound.Shared/     # Protokoll, Audio-Packet, i18n, Diagnostik
│   ├── LiveStreamSound.Host/       # Host-Services (Lib): Capture, Session, Control/Audio-Server
│   ├── LiveStreamSound.Client/     # Client-Services (Lib): Discovery, Control/Audio-Client, Sync, Playback
│   └── LiveStreamSound.App/        # WPF-Exe: Role-Selection + Host- & Client-Dashboards
├── installer/
│   └── App/                        # WiX-Projekt → single MSI inkl. .NET Runtime
├── .github/workflows/
│   └── build-msi-on-demand.yml     # CI: self-contained Publish + MSI-Build + Artifact-Upload
└── docs/
    ├── Anleitung.md                # Lehrer-Anleitung (DE)
    └── Guide.md                    # Teacher manual (EN)
```

## Bauen

Voraussetzungen: **Windows** (wegen WPF) + **.NET 10 SDK**.
Für MSI zusätzlich: `dotnet tool install --global wix --version 5.0.2`

```powershell
# Vom Repo-Root aus:
dotnet restore
dotnet build src/LiveStreamSound.App -c Release

# Self-contained Publish mit gebundeltem .NET-Runtime:
dotnet publish src/LiveStreamSound.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

# MSI bauen (nutzt den Publish-Ordner automatisch):
dotnet build installer/App -c Release
# → installer/App/bin/Release/LiveStreamSound-<version>.msi
```

> Die Libraries (Shared, Host, Client) haben `EnableWindowsTargeting=true`, dadurch klappt `dotnet restore`/`build` der Code-Projekte auch auf macOS/Linux. Ausgeführt werden kann nur unter Windows (wegen WPF + WASAPI). Die App + MSI sollten auf Windows gebaut werden.

### CI (GitHub Actions)

Der Workflow [.github/workflows/build-msi-on-demand.yml](.github/workflows/build-msi-on-demand.yml) baut on-demand (Actions-Tab → „Run workflow") einen MSI:

1. `git describe --tags` → ermittelt Version (fallback `v0.2.0` wenn kein Tag)
2. `dotnet restore` + Libs + Tests
3. `dotnet publish` self-contained win-x64 mit `-p:Version=...`
4. Baut WiX-MSI-Projekt → `LiveStreamSound-<version>.msi`
5. Lädt MSI + Portable-App als versionierte GitHub-Artifacts hoch

Der MSI-Artifact kann direkt auf einen frischen Windows-PC gespielt werden — kein separates .NET 10 nötig.

### Versionierung

Die Version wird aus dem letzten Git-Tag abgeleitet, z.B.:
- Tag `v0.3.0` → MSI-Version `0.3.0.0`, Artifact `LiveStreamSound-MSI-v0.3.0.0`
- Tag `v1.0.0-beta.1` → MSI-Version `1.0.0.0` (Pre-Release-Suffix wird für MSI gestrippt)
- Kein Tag → `v0.2.0` als Fallback

**Neue Version veröffentlichen:**
```bash
git tag -a v0.3.0 -m "Release v0.3.0"
git push --tags
```
Dann im Actions-Tab den Workflow triggern — er picked automatisch den neuen Tag.

Alternativ beim manuellen Trigger im Actions-UI eine Override-Version eintragen (z.B. für Hotfix-Builds ohne Tag).

Für lokale Dev-Builds überschreibt `-p:Version=0.3.1.0` beim `dotnet build/publish` den Default aus `Directory.Build.props`.

### Intune-Deployment

Das MSI ist ready für Microsoft-Intune-Rollouts — LOB-App-Typ, kein `.intunewin`-Wrapping nötig. Details + step-by-step: [docs/intune-deployment.md](docs/intune-deployment.md).

Kurz:
1. MSI aus dem CI-Artifact herunterladen
2. Intune Admin Center → Apps → Windows apps → Add → Line-of-business
3. MSI hochladen — Metadata wird automatisch gelesen
4. Install-args: `/quiet /norestart`, Assignment als Required oder Available
5. Upgrades laufen via `MajorUpgrade`, Uninstall ist sauber (Firewall-Regeln + Event-Log-Source + Registry werden mit entfernt)

### Firewall-Rollout via GPO

Der MSI legt die Firewall-Regeln selbst an. Wer die Regeln **vor** der App-Installation auf den Raum-PCs haben will (z.B. AD-Schul-Umgebung, Intune Proactive Remediation, oder ADMX-Template für Admins), findet fertige Artefakte unter [deployment/](deployment/) und einen step-by-step-Guide in [docs/gpo-firewall-deployment.md](docs/gpo-firewall-deployment.md).

## Netzwerk-Ports

| Port | Protokoll | Zweck |
|---|---|---|
| 5000 | TCP | Host-Control-Channel (JSON-Messages) |
| 5001 | UDP | Host-Audio-Stream (Quelle, fan-out zu jedem Client) |
| **ephemeral** | UDP | **Client-Audio-Empfang** (vom OS gewählt, an Host gemeldet via `AudioClientReady`) |
| 5002 | TCP | Idle-Client-Listener (empfängt Einladungen vom Host) |
| 5353 | UDP | mDNS (Service Discovery) |

Client bindet seinen UDP-Empfangs-Port ephemeral (OS-vergeben) und teilt den Port dem Host nach dem HELLO mit — so können Host und Client auch **auf derselben Maschine** laufen, ohne dass 5001 kollidiert.

## Lokal testen (beide Rollen auf einem Rechner)

1. Eine Instanz starten → **Ton senden** wählen → **Sitzung starten**.
2. Zweite Instanz derselben `.exe` starten → **Ton empfangen**.
3. In der Client-Instanz taucht der Host in der Liste *Gefundene Hosts* auf (ggf. unter der WLAN-IP, nicht der Hyper-V-IP — virtuelle Adapter werden gefiltert).
4. Code aus der Host-Instanz eintippen → **Verbinden**.

Alternativ: die Host-Instanz kann über **Client hinzufügen** den idle-Client einladen. Beide Wege enden im selben HELLO-Flow.

Der MSI fügt TCP 5000, UDP 5001, TCP 5002 automatisch zu den Windows-Firewall-Regeln hinzu (Scope: LocalSubnet).

## Protokoll

**Discovery (mDNS):**
- `_livestreamsound._tcp` — aktive Hosts
- `_lssclient._tcp` — idle Clients warten auf Einladung
- TXT-Records `v` (Protocol-Version), `name` (Instanz-Name aka Session-Name bzw. Raum-Name)

**Control-Channel (TCP, Port 5000):** JSON mit `type`-Diskriminator.
- Client → Host: `hello {code, clientName, protocolVersion}`
- Host → Client: `welcome {clientId, audioUdpPort, sampleRate, channels, audioCodec, serverTimeMs}` | `authFail {reason}`
- Host → Client: `setVolume`, `setMute`, `setOutputDevice`, `listOutputDevices`, `kick`
- Beide: `ping`/`pong` (Keepalive + Clock-Sync)
- Host → idle Client (auf Port 5002): `invitation {sessionCode, hostAddress, hostControlPort, hostDisplayName}` → Client antwortet `invitationResponse {accepted, reason?}`

**Audio-Channel (UDP, Port 5001):** Binäres Paket
```
[Magic "LSSA" 4B][Version 1B][PayloadType 1B][PayloadLen 2B][Seq 4B][ServerTimestampMs 8B][Opus-Payload]
```
Frames sind 20 ms (960 Samples @ 48 kHz).

**Sync:** Client berechnet Clock-Offset zum Server via PING/PONG (best-RTT-Estimate). Jedes Audio-Frame wird bei `serverTimestamp + 100 ms` in lokaler Zeit abgespielt → synchron auf allen Clients.

## Status

v0.2 — Unified-App-Refactor fertig, Invite-Protokoll (bidirektionale Session-Initiierung) implementiert. Ende-zu-Ende-Test mit mehreren echten Clients ausstehend (nächster Matura-Testdurchlauf).

## Lizenz

Privat / schulisches Projekt. Fremdbibliotheken unter ihren jeweiligen Lizenzen (MIT/BSD).
