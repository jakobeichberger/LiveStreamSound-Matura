# LiveStreamSound über Microsoft Intune ausrollen

Die LiveStreamSound-MSI ist als Intune-LOB-App (Line-of-Business) konzipiert — kein zusätzliches `.intunewin`-Wrapping nötig, weil Intune MSI direkt versteht.

## 1. MSI bauen

1. GitHub → Actions → **Build MSI (on demand)** → *Run workflow*
2. Optional: Version-Override eintragen (sonst wird der letzte Git-Tag genutzt)
3. Nach ~3 min ist das Artifact `LiveStreamSound-MSI-v<version>` fertig → herunterladen + entpacken → `LiveStreamSound-<version>.msi`

## 2. In Intune hochladen

Microsoft Intune Admin Center (https://intune.microsoft.com):

1. **Apps** → **Windows apps** → **Add**
2. **App type**: *Line-of-business app*
3. **App package file**: `LiveStreamSound-<version>.msi` auswählen

Intune liest folgende Felder **automatisch aus der MSI-Metadata**:

| Feld | Wert |
|---|---|
| Name | LiveStreamSound |
| Publisher | Jakob Eichberger |
| Product code | (dynamisch pro Build) |
| Version | z.B. 0.2.0.0 |
| Description | „Kabellose Audio-Verteilung für Matura-Prüfungen — eine App, beim Start wählst du senden oder empfangen." |
| Information URL | https://github.com/jakobeichberger/LiveStreamSound-Matura |
| Privacy URL | (leer, kann ergänzt werden) |
| Developer | Jakob Eichberger |

## 3. Install- und Uninstall-Parameter

Standard-Werte reichen. Intune generiert automatisch:

- **Install command** (intern): `msiexec /i "LiveStreamSound-<version>.msi" /quiet /norestart`
- **Uninstall command** (intern): `msiexec /x <ProductCode> /quiet /norestart`

Zusätzliche Parameter im „Command-line arguments"-Feld:
```
/quiet /norestart
```

Sollte die MSI bei Install einen Neustart anfordern (passiert bei uns nicht, weil perMachine ohne OS-Komponenten), wird Intune den 3010-Exit-Code als „Erfolg mit Restart nötig" erkennen.

## 4. Requirements + Detection

**Requirements:**
- **Operating system architecture**: 64-bit
- **Minimum OS**: Windows 10 21H2 / Windows 11 (wegen .NET 10 + WPF)
- **Disk space**: ~150 MB frei (self-contained .NET-Runtime + WPF-UI + Audio-Libs)
- **Memory**: 1 GB RAM

**Detection rules**: Intune erkennt MSI-Apps automatisch über den Product Code. **Keine manuelle Detection-Rule nötig**.

Falls man zusätzlich absichern will:
- **Registry**: `HKLM\Software\LiveStreamSound\InstalledVersion` — Value type: String, Detection method: Version ≥ der installierten.
- **File**: `C:\Program Files\LiveStreamSound\LiveStreamSound.exe` — File or folder: exists.

## 5. Assignments

- **Required** für automatische Installation auf allen Matura-Raum-PCs (via Entra-ID-Gruppe)
- **Available for enrolled devices** falls der Lehrer selbst entscheiden soll
- **Uninstall**: eigener Assignment-Typ, entfernt die App von allen zugewiesenen Geräten

## 6. Nach dem Rollout prüfen

Auf einem deployten Raum-PC sollten folgende Artifacts existieren:

| Pfad / Ort | Was |
|---|---|
| `C:\Program Files\LiveStreamSound\LiveStreamSound.exe` | Haupt-Exe mit gebundelter .NET 10 Runtime |
| Start-Menü → `LiveStreamSound\LiveStreamSound` | Shortcut (alle User) |
| Desktop (alle User) | Shortcut |
| Windows Defender Firewall (mit erweiterter Sicherheit) | 3 eingehende Regeln „LiveStreamSound (TCP Control/UDP Audio/TCP Invite)" |
| eventvwr.msc | Eigener Log-Ordner **LiveStreamSound** |
| `HKLM\Software\LiveStreamSound` | Registry-Key mit `InstalledVersion`, `firewall`, `eventlog_registered` |

Wenn eins davon fehlt, Intune-Install-Logs prüfen:
`C:\ProgramData\Microsoft\IntuneManagementExtension\Logs\IntuneManagementExtension.log`

## 7. Uninstall-Check

Bei Intune-Uninstall oder manuell via *Programme und Features* wird entfernt:

- ✅ Program-Files-Ordner `C:\Program Files\LiveStreamSound\` (komplett)
- ✅ Start-Menü-Ordner `LiveStreamSound\` mit Shortcut
- ✅ Desktop-Shortcut
- ✅ Alle 3 Windows-Firewall-Regeln
- ✅ Event-Log-Source + custom Event-Log-Ordner (nach nächstem Reboot oder EventLog-Service-Restart sichtbar entfernt)
- ✅ `HKLM\Software\LiveStreamSound` Registry-Key komplett (via `ForceDeleteOnUninstall`)

**Nicht** entfernt (User-Daten):
- `%LOCALAPPDATA%\LiveStreamSound\...\logs\*.log` — Fehler-Logs des Users
- `%LOCALAPPDATA%\LiveStreamSound\crashes\*.log` — Crash-Dumps

Diese bleiben bewusst stehen, damit User-Logs nach einem Re-Deploy nicht verloren gehen. Wer sie manuell loswerden will:
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\LiveStreamSound"
```

## 8. Update-Workflow

Upgrades laufen automatisch über MSI `MajorUpgrade` — Intune installiert die neue Version einfach oben drauf, die alte wird durch die WiX-Upgrade-Tabelle entfernt.

Reihenfolge bei Upgrade:
1. Neue MSI-Version an Intune hochladen (als *neue* App oder *Update* der bestehenden)
2. Intune deployt auf die Zielgeräte
3. MSI erkennt via `UpgradeCode` die alte Version, entfernt sie, installiert die neue
4. Shortcuts + Firewall-Regeln werden neu erstellt (gleiche Guids, idempotent)

## 9. Troubleshooting

| Symptom | Ursache | Fix |
|---|---|---|
| Intune-Install „Failed (0x80070643)" | MSI-Fehler beim Install, oft Berechtigungen | Als SYSTEM-Kontext installieren (Intune tut das bei Device-Assignment). User-Assignment erfordert Admin. |
| „Already installed" bei Version-Upgrade | UpgradeCode falsch oder Version nicht höher | UpgradeCode ist in Package.wxs stabil; Version muss strikt > alte sein (0.2.0.0 → 0.3.0.0 ok, nicht 0.2.0.0 → 0.2.0.0) |
| Firewall-Regel fehlt | Install in per-user-Kontext gelaufen | MSI ist `Scope="perMachine"`, muss mit Admin-Rechten oder SYSTEM installiert werden |
| Event-Log-Source fehlt | Install ohne Admin | Siehe oben. Alternativ: Admin + App einmal als User starten (fallback registriert Source wenn möglich) |

## 10. Silent-Deploy-Test ohne Intune

Zum Trockentest auf einem Test-PC:
```powershell
# Install
msiexec /i "LiveStreamSound-0.2.0.0.msi" /quiet /norestart /l*v install.log

# Uninstall (ProductCode aus dem MSI lesen oder via WMI)
$p = Get-WmiObject Win32_Product | ? Name -eq "LiveStreamSound"
msiexec /x $p.IdentifyingNumber /quiet /norestart
```

Die `install.log` zeigt etwaige Fehler im Detail.
