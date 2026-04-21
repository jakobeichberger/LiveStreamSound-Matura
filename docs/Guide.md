# LiveStreamSound — Teacher's guide

Step-by-step: cable-free audio distribution for the Matura classroom.

## Overview

Two apps, two roles:

- **Host**: your laptop — starts the session, plays the audio file (in VLC, browser, etc.).
- **Client**: the room PC connected via HDMI to the beamer/speakers — receives the audio.

They find each other automatically when on the same Wi-Fi.

## One-time setup

1. On each room PC, double-click `LiveStreamSound-Client-0.1.0.msi` — installation is silent.
2. On your teacher laptop, install `LiveStreamSound-Host-0.1.0.msi`.

Both installers add the required firewall rules automatically.

## Before the exam (5-minute setup)

### On the host laptop

1. Launch **LiveStreamSound Host** from the Start menu.
2. Click **Start session**. A 6-digit code appears at the top, along with the host IP and a QR code.
3. Write down the code and IP, or show the QR code on the beamer.

### On each room PC (client)

1. Launch **LiveStreamSound Client**.
2. If the host appears in **Discovered hosts**, click it. Otherwise enter the IP manually.
3. Type the **code**.
4. Check the **output device** is set to HDMI (so the beamer/speakers get the sound).
5. Click **Connect**.

If everything works, the status bar says "Connected • Good". On the host you'll see the new client in the list under **Classrooms**.

## During the exam

1. Open VLC or the browser on the host laptop and play the audio file.
2. The sound plays simultaneously on every client over HDMI.
3. Per client from the host you can:
   - Adjust **volume** via the slider
   - Toggle **mute**
   - Change the **output device** (if something is mis-routed)
   - **Kick client** (red trash icon)

## After the exam

Click **Stop session** on the host. All clients disconnect automatically and can be closed.

## Something's wrong? Read this.

Both apps show a **connection quality** indicator. When there's a problem, the status area explains what's likely wrong and what to do about it.

Common situations:

### "No audio on host"
Nothing's playing right now. Open VLC and start the audio.

### "Wi-Fi isolating clients"
The school Wi-Fi separates devices from each other; mDNS discovery can't see the host.
→ Enter the IP manually on the client. If possible, ask IT to disable client isolation.

### "Firewall blocks the audio stream"
Control connection is up but no sound. Usually Windows Firewall.
→ The installer should have fixed this. If not, allow the app on first run.

### "Packet loss" / "High latency"
Weak Wi-Fi.
→ Move the host closer to the AP. If possible, use a LAN cable.

### Client stops responding
Kick it from the host (trash icon), then reconnect on the client.

## Log file

Each app has a log button (📄) at the top right. Click **Open log folder** to jump straight to `%LOCALAPPDATA%\LiveStreamSound\...\logs\`.

## Tips

- **QR code**: show it on the beamer. Clients can type the link or paste from a scan.
- **Names**: the client auto-detects the room from the hostname (`HP-KB-017` → "Room 017"). You can override the name before connecting.
- **Light/Dark theme**: icon top right (🌓).
- **Language**: icon top right (🌐).
- **Dry-run**: before the exam, play a test tone to make sure every room is at the same volume.

Good luck with the Matura!
