# LiveStreamSound-Matura

> Wireless audio distribution for Austrian Matura (school final exam) rooms — a host/client pair over LAN instead of a long cable.

Deutsche Version: [README.md](README.md) · Teacher guide: [docs/Guide.md](docs/Guide.md)

---

## What it does

The English Matura exam requires listening-comprehension audio to be played across merged module rooms. The traditional solution is a long audio cable — clumsy and fragile.

**LiveStreamSound** ships two apps:

- **Host** runs on the teacher's laptop. Everything the host PC plays (VLC, browser, etc.) is streamed over Wi-Fi/LAN to every connected client.
- **Client** runs on the room PCs connected via HDMI to the beamer/speakers. It receives the stream and plays it synchronously with the other clients.

The teacher controls everything centrally: per-client volume, mute, output-device selection, kick.

## Features

- 🎯 System audio loopback (WASAPI) — whatever plays on the host is streamed
- 🔒 6-digit session code, no passwords
- 🔍 Auto discovery via mDNS + manual-IP fallback + QR code
- ⏱️ Synchronous playback on all clients (timestamp-based jitter buffer)
- 🎚️ Per-client remote control: volume, mute, output device, kick
- 🏷️ Auto room detection from hostname (`HP-KB-017` → "Room 017")
- 🌐 German + English, switchable at runtime
- 🌓 Light + Dark theme (Fluent Design / Windows 11 look)
- 🩺 Connection-quality indicator with plain-language problem explanations
- 📓 Error log in-app and on disk (`%LOCALAPPDATA%\LiveStreamSound\...\logs\`)
- ❓ Built-in help panel in the GUI
- 📦 MSI installers for host and client (firewall rules included)

## Tech stack

- **.NET 10** + **WPF** (Windows-only target)
- **WPF-UI** 3.0 (Fluent design)
- **NAudio** 2.2 (WASAPI capture + playback)
- **Concentus** 2.2 (pure-C# Opus codec)
- **Makaretu.Dns.Multicast** (mDNS)
- **QRCoder**
- **CommunityToolkit.Mvvm**
- **WiX Toolset 5** for MSI output

## Build

Requirements: **Windows** (because of WPF) + **.NET 10 SDK**.
For MSI builds: `dotnet tool install --global wix --version 5.0.2`.

```powershell
dotnet restore
dotnet build -c Release
dotnet build installer/Host       -c Release
dotnet build installer/Client     -c Release
```

Host/Client projects set `EnableWindowsTargeting=true`, so `dotnet restore` works on macOS/Linux. Building the runnable app still requires Windows.

## Protocol summary

- Discovery: mDNS service type `_livestreamsound._tcp`
- Control: TCP :5000, JSON frames with `type` discriminator (`hello`, `welcome`, `setVolume`, `setMute`, `setOutputDevice`, `kick`, `ping`/`pong`, `clientStatus`, …)
- Audio: UDP :5001, 20 ms Opus frames with 20-byte binary header (magic `LSSA`, version, payload type, sequence, server timestamp)
- Sync: clients NTP-like-sync their clock to the host via ping/pong, then play each frame at `serverTimestamp + 100 ms` local time

## Status

0.1.0 — all core components implemented and compiling. End-to-end test with multiple real clients pending (next Matura dry-run).

## License

Private / school project. Third-party libraries under their respective licenses (MIT/BSD).
