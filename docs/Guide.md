# LiveStreamSound — Teacher's guide

Step-by-step: cable-free audio distribution for the Matura classroom.

## Overview

One app, two roles. At launch you pick:

- **📡 Send audio** — your laptop is the host. Whatever plays on this PC (VLC, browser, …) goes on the network.
- **🎧 Receive audio** — your laptop is a client at the beamer/speakers (HDMI). It receives audio.

Both roles start from the same `.exe` — no separate installer per room.

## One-time setup

1. Double-click `LiveStreamSound-<version>.msi` on each laptop.
2. Firewall rules (TCP 5000 + UDP 5001 + TCP 5002) are added automatically.
3. **No separate .NET 10 needed** — the runtime is bundled inside the MSI.

## Before the exam (5-minute setup)

### On the host laptop

1. Launch LiveStreamSound.
2. On the start screen pick **"📡 Send audio"**.
3. Click **Start session**. A 6-digit code appears, plus the host IP.
4. Write down code + IP, or use *direct invite* (below).

### On each room PC (client)

**Option A — client joins itself:**
1. Launch LiveStreamSound.
2. Pick **"🎧 Receive audio"**.
3. Click the host in *Discovered hosts* (or enter IP manually).
4. Type the 6-digit code.
5. Set **output device** to HDMI.
6. Click **Connect**.

**Option B — host invites the room PC (new in v0.2!):**
1. On the room PC: start LiveStreamSound → pick "🎧 Receive" → leave start screen open.
2. On the host: **Start session** → click **"Add client"** at the top.
3. Dialog shows the room PC by its room number → click to invite.
4. The room PC shows an invitation card with host + session code → **Accept**.
5. Connected.

When it works, the status bar says "Connected • Good". On the host the new client appears under **Classrooms**.

## During the exam

1. Open VLC or the browser on the host, play the audio file.
2. Audio plays simultaneously on every client over HDMI.
3. Per room from the host you can:
   - Adjust **volume** (percent)
   - Toggle **mute** ("sound on" / "muted")
   - Change **output device**
   - **Kick** the client (red X)
   - New: **invite more room PCs** via "Add client"

## Switching role later

If you misclicked (picked "Receive" instead of "Send"):

- Top right: **↔ Switch role**.
- If you're connected, a dialog warns "Session will be disconnected — Continue?" → OK.
- Back to the start screen.

## After the exam

Click **Stop session** on the host. All clients disconnect. Close the program.

## Something's wrong? Read this.

Both roles show a **connection quality** bar at the bottom. Issues come with plain-language explanations.

### "No audio on host"
Nothing is playing. Start VLC.

### "Wi-Fi isolating clients"
School Wi-Fi separates devices; auto discovery can't see the host.
→ Enter IP manually or use **host → invite** (if mDNS is blocked, invite may fail too; then use a LAN cable or ask IT).

### "Firewall blocks the audio stream"
Control channel up, no sound.
→ The MSI should handle this. If not, allow the Windows Firewall prompt on first launch.

### "Port 5000/5001 in use"
Another instance is running or a different app uses the port.
→ Close the app and restart, or kill the other process.

### "Packet loss" / "High latency"
Weak Wi-Fi. Move closer to the AP or use a LAN cable.

## Log file

Each role has a log button (📄) top right. **Open log folder** jumps to `%LOCALAPPDATA%\LiveStreamSound\...\logs\`.

## Tips

- **Names**: room PCs auto-resolve to "Room 017" etc. based on the hostname (`HP-KB-017` → Room 017). Override before connecting if you want.
- **Light/Dark theme**: icon top right (🌓). Default follows system theme.
- **Language**: icon top right (🌐) — German/English at runtime.
- **Volume test**: before the exam play a test tone and level every room.
- **Invite flow**: best if room PCs auto-launch LiveStreamSound and wait in receive mode at boot — then the host needs 1 click per room.

Good luck with the Matura!
