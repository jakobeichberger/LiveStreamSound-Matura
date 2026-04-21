# Firewall-Regeln per GPO vorab ausrollen

Die LiveStreamSound-MSI legt die Firewall-Regeln selbst an. Für Umgebungen in denen die Regeln **vor** oder **unabhängig** von der App-Installation existieren sollen (z.B. Schul-AD mit vielen Raum-PCs), gibt's diese Alternativen.

## Option A — PowerShell-Script via GPO Startup Script (einfachste Variante)

Das Script [deployment/Add-LiveStreamSoundFirewallRules.ps1](../deployment/Add-LiveStreamSoundFirewallRules.ps1) fügt die drei inbound-Regeln `LocalSubnet`-scoped hinzu und ist idempotent (sicher mehrfach laufen).

### Schritte

1. **Script zentral ablegen** auf einem für alle Ziel-PCs erreichbaren UNC-Pfad, z.B.:
   ```
   \\<domain>\SYSVOL\<domain>\scripts\LiveStreamSound\Add-LiveStreamSoundFirewallRules.ps1
   ```
   Oder einfach in `SYSVOL\<domain>\Policies\<PolicyGuid>\Machine\Scripts\Startup\` des jeweiligen GPO-Ordners.

2. **GPO erstellen oder erweitern** (Group Policy Management Console):
   - Computer Configuration → Policies → Windows Settings → Scripts (Startup/Shutdown) → **Startup**
   - Tab **Scripts** → *Add* → Script Name: `Add-LiveStreamSoundFirewallRules.ps1`
   - Tab **PowerShell Scripts** ist moderner — lieber da hinzufügen
   - Scripts ausführen als *SYSTEM* (Default für Startup-Scripts)

3. **GPO an Ziel-OU verknüpfen** — z.B. `OU=MaturaRoomPCs`

4. **Testen**: einen Raum-PC neu starten → nach dem nächsten Boot sollten in `wf.msc` drei neue Regeln „LiveStreamSound (TCP Control)" / „LiveStreamSound (UDP Audio)" / „LiveStreamSound (TCP Invite)" erscheinen, Group = `LiveStreamSound`.

### Rückwärts: Regeln entfernen

[deployment/Remove-LiveStreamSoundFirewallRules.ps1](../deployment/Remove-LiveStreamSoundFirewallRules.ps1) löscht alles wieder. Einsetzbar als **Shutdown-Script** wenn die GPO aufgelöst werden soll.

## Option B — Intune Proactive Remediation

Wenn die Umgebung Intune-verwaltet ist, statt GPO eine *Proactive Remediation* anlegen:

1. Intune Admin Center → Devices → Scripts and remediations → Add
2. **Detection script**: prüft ob alle drei Regeln existieren
   ```powershell
   $expected = 'LiveStreamSound (TCP Control)','LiveStreamSound (UDP Audio)','LiveStreamSound (TCP Invite)'
   foreach ($name in $expected) {
     if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) { exit 1 }
   }
   exit 0
   ```
3. **Remediation script**: Inhalt von `Add-LiveStreamSoundFirewallRules.ps1`
4. Schedule: Once at login, oder hourly
5. Assignment: Ziel-Device-Gruppe

## Option C — ADMX-Template (Admin-freundliche Schalter)

Für Admins, die den Firewall-Rollout per Häkchen-Policy in der GPMC kontrollieren wollen (statt Script-Verwaltung), gibt's:

- [deployment/LiveStreamSound-Firewall.admx](../deployment/LiveStreamSound-Firewall.admx)
- [deployment/en-US/LiveStreamSound-Firewall.adml](../deployment/en-US/LiveStreamSound-Firewall.adml)

### Schritte

1. Beide Files in den zentralen ADMX-Store kopieren:
   ```
   \\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\LiveStreamSound-Firewall.admx
   \\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\en-US\LiveStreamSound-Firewall.adml
   ```

2. In GPMC erscheint jetzt: Computer Configuration → Policies → Administrative Templates → **LiveStreamSound** → *Enable LiveStreamSound firewall rules*.

3. Policy aktivieren → setzt `HKLM\Software\Policies\LiveStreamSound\FirewallRulesEnabled = 1`.

4. Startup-Script aus Option A bleibt weiterhin nötig — das Template ist nur das Schalter-UI. Im Startup-Script kannst du einen Pre-Check einbauen:
   ```powershell
   $gate = Get-ItemProperty -Path 'HKLM:\Software\Policies\LiveStreamSound' `
           -Name FirewallRulesEnabled -ErrorAction SilentlyContinue
   if ($gate.FirewallRulesEnabled -ne 1) { exit 0 }  # policy disabled → do nothing
   ```

(Das ADMX-Template ist optional — wer lieber direkt das Script über GPO deployt, kann's weglassen.)

## Port-Übersicht (zur Info)

| Port | Proto | Richtung | Zweck |
|---|---|---|---|
| 5000 | TCP | Inbound | Host-Control-Channel (JSON über TCP) |
| 5001 | UDP | Inbound | Host-Audio-Stream (Quelle, Clients binden ephemeral) |
| 5002 | TCP | Inbound | Idle-Client-Invite-Listener |
| 5353 | UDP | Inbound+Outbound | mDNS — meist schon durch Windows-Default-Regel erlaubt |

Scope = `LocalSubnet` — keine der Regeln öffnet die Ports ins Internet.

## MSI + GPO zusammen verwenden?

Kein Problem. Die MSI prüft beim Install nicht ob die Regeln schon existieren — sie legt sie zum zweiten Mal an. Windows akzeptiert doppelte Regel-Namen nicht, also können dabei Hiccups passieren. Empfohlene Reihenfolge:

- **Wenn MSI über Intune/GPO deployt wird → die MSI regelt's selbst**, GPO-Script ist redundant.
- **Wenn Raum-PCs zuerst per GPO firewall-ready sein sollen, MSI später** → GPO-Script läuft zuerst; MSI-Install findet identische Regeln vor und überschreibt sie (WiX Firewall-Extension ist stabil hiermit).
- **Nur GPO, keine MSI** (z.B. BYOD-Raum-PC mit portabler App) → nur GPO-Script, MSI nicht nötig.
