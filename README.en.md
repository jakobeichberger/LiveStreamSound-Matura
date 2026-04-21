# LiveStreamSound-Matura

> Wireless audio distribution for school final exams — **one** app that asks at launch whether to send or receive.

Deutsche Version: [README.md](README.md) · Teacher guide: [docs/Guide.md](docs/Guide.md)

---

## What it does

For the English Matura (Austrian final exam) listening-comprehension audio must be played in merged module rooms. Cabling through the room is clumsy; this app replaces the cable with LAN streaming.

**LiveStreamSound** is a single Windows app. At launch you pick:

- **📡 Send audio** — your laptop becomes the host. Whatever plays on this PC (VLC, browser, …) is streamed to every connected client.
- **🎧 Receive audio** — your laptop is a client attached to a beamer/speaker. It receives the stream synchronously and plays it through HDMI.

Same `.exe`. The role can be switched at runtime via the burger menu.

## Features

- 🎯 System audio loopback (WASAPI) — whatever plays on the host is streamed
- 🔒 6-digit session code, no passwords
- 🔍 Auto discovery via mDNS + manual IP fallback
- 📨 **Host can actively invite clients** (client confirms with accept/reject prompt) — teacher doesn't have to walk to each room PC
- ⏱️ Synchronous playback on all clients (timestamp-based jitter buffer, NTP-like clock sync)
- 🎚️ Per-client remote control: volume, mute, output device, kick
- 🏷️ Auto room detection from hostname (`HP-KB-017` → "Room 017"), categorized as classroom / workshop / other
- 🌐 German + English, runtime switch
- 🌓 Light + Dark (follows system theme at start, Fluent / Windows 11 look)
- 🩺 Connection-quality indicator with plain-language problem explanations
- 📓 Error log in-app + on disk (`%LOCALAPPDATA%\LiveStreamSound\...\logs\`)
- ❓ Built-in help panel in the GUI
- 📦 **Single MSI installer with bundled .NET 10 runtime** — no separate .NET install needed, firewall rules added automatically

## Tech stack

- **.NET 10** + **WPF** (Windows-only target)
- **WPF-UI** 3.0 (Fluent Design)
- **NAudio** 2.2 (WASAPI capture + playback)
- **Concentus** 2.2 (pure-C# Opus codec)
- **Makaretu.Dns.Multicast** (mDNS)
- **CommunityToolkit.Mvvm** (source-generator MVVM)
- **WiX Toolset 5** for the single-bundle MSI

## Project layout

```
LiveStreamSound-Matura/
├── LiveStreamSound.slnx
├── Directory.Build.props
├── src/
│   ├── LiveStreamSound.Shared/     # protocol, audio packet, i18n, diagnostics
│   ├── LiveStreamSound.Host/       # host services (lib): capture, session, control+audio server
│   ├── LiveStreamSound.Client/     # client services (lib): discovery, control+audio client, sync, playback
│   └── LiveStreamSound.App/        # WPF exe: role selection + host & client dashboards
├── installer/
│   └── App/                        # WiX project → single MSI incl. .NET runtime
├── .github/workflows/
│   └── build-msi-on-demand.yml     # CI: self-contained publish + MSI build + artifact upload
└── docs/
    ├── Anleitung.md                # teacher manual (DE)
    └── Guide.md                    # teacher manual (EN)
```

## Build

Requirements: **Windows** (WPF) + **.NET 10 SDK**.
For MSI: `dotnet tool install --global wix --version 5.0.2`.

```powershell
dotnet restore
dotnet build src/LiveStreamSound.App -c Release

# Self-contained publish (bundles .NET runtime):
dotnet publish src/LiveStreamSound.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false

# Build MSI (picks up the publish folder):
dotnet build installer/App -c Release
# → installer/App/bin/Release/LiveStreamSound-<version>.msi
```

Libraries (Shared/Host/Client) use `EnableWindowsTargeting=true`, so `dotnet restore`/`build` of the code projects also works on macOS/Linux. Running the app still requires Windows.

### CI (GitHub Actions)

[.github/workflows/build-msi-on-demand.yml](.github/workflows/build-msi-on-demand.yml) builds on-demand only (Actions tab → "Run workflow"):

1. `git describe --tags` → derives version (falls back to `v0.2.0` if no tag)
2. `dotnet restore` + libs + tests
3. `dotnet publish` self-contained win-x64 with `-p:Version=...`
4. Build WiX MSI project → `LiveStreamSound-<version>.msi`
5. Upload MSI + portable folder as versioned GitHub artifacts

The MSI artifact installs cleanly on a freshly imaged Windows PC without a separate .NET 10 install.

### Versioning

The version is derived from the latest git tag, e.g.:
- Tag `v0.3.0` → MSI version `0.3.0.0`, artifact `LiveStreamSound-MSI-v0.3.0.0`
- Tag `v1.0.0-beta.1` → MSI version `1.0.0.0` (pre-release suffix is stripped for MSI)
- No tag → falls back to `v0.2.0`

**Publish a new version:**
```bash
git tag -a v0.3.0 -m "Release v0.3.0"
git push --tags
```
Then trigger the workflow from the Actions tab — it picks up the new tag automatically.

Alternatively, when triggering the workflow manually you can enter an override version (useful for hotfix builds without a tag).

For local dev builds, `-p:Version=0.3.1.0` on `dotnet build/publish` overrides the `Directory.Build.props` default.

### Intune deployment

The MSI is ready for Microsoft Intune deployment as a Line-of-business app — no `.intunewin` wrapping needed. See [docs/intune-deployment.md](docs/intune-deployment.md) for the walkthrough.

Quick version:
1. Download the MSI from the CI artifact
2. Intune Admin Center → Apps → Windows apps → Add → Line-of-business
3. Upload the MSI — metadata is read automatically
4. Install args: `/quiet /norestart`, assign as Required or Available
5. Upgrades flow through `MajorUpgrade`; uninstall is clean (firewall rules, event-log source, registry are all removed)

## Network ports

| Port | Proto | Purpose |
|---|---|---|
| 5000 | TCP | Host control channel (JSON messages) |
| 5001 | UDP | Host audio source (fan-out to every client) |
| **ephemeral** | UDP | **Client audio receive** (OS-assigned, reported to host via `AudioClientReady`) |
| 5002 | TCP | Idle-client listener (receives invites from hosts) |
| 5353 | UDP | mDNS (service discovery) |

The client binds its UDP receive port to an ephemeral (OS-picked) port and tells the host after HELLO — so host and client can run **on the same machine** without clashing on 5001.

## Local smoke-test (both roles on one machine)

1. Launch one instance → pick **Send audio** → **Start session**.
2. Launch a second instance of the same `.exe` → pick **Receive audio**.
3. The host shows up in *Discovered hosts* on the client (on the real Wi-Fi IP, not the Hyper-V virtual adapter — virtual NICs are filtered).
4. Type the code shown on the host → **Connect**.

Or: use **Add client** on the host to invite the idle client — both paths end in the same HELLO flow.

The MSI adds TCP 5000, UDP 5001, TCP 5002 to the Windows Firewall automatically (scope: LocalSubnet).

## Protocol

**Discovery (mDNS):**
- `_livestreamsound._tcp` — active hosts
- `_lssclient._tcp` — idle clients waiting for invitation
- TXT records `v` (protocol version), `name` (instance name / session name / room name)

**Control channel (TCP, port 5000):** JSON with `type` discriminator.
- Client → Host: `hello {code, clientName, protocolVersion}`
- Host → Client: `welcome {...}` | `authFail {reason}`
- Host → Client: `setVolume`, `setMute`, `setOutputDevice`, `listOutputDevices`, `kick`
- Both: `ping`/`pong` (keepalive + clock sync)
- Host → idle Client (port 5002): `invitation {sessionCode, hostAddress, hostControlPort, hostDisplayName}` → Client replies `invitationResponse {accepted, reason?}`

**Audio channel (UDP, port 5001):** binary packet
```
[magic "LSSA" 4B][version 1B][payloadType 1B][payloadLen 2B][seq 4B][serverTimestampMs 8B][opus payload]
```
Frames are 20 ms (960 samples @ 48 kHz).

**Sync:** Client computes clock offset via PING/PONG (best-RTT estimate). Every audio frame is played at `serverTimestamp + 100 ms` local time → clients stay in sync.

## Status

v0.2 — unified app refactor complete, invite protocol (bidirectional session start) implemented. End-to-end test with multiple real clients pending (next Matura dry-run).

## License

Private / school project. Third-party libraries under their respective licenses (MIT/BSD).
