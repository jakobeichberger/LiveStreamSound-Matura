# MSI-Upgrade-Verhalten — was wird ausgetauscht, was bleibt

Dokumentiert für Schul-IT + Auditing: was passiert technisch wenn man eine neue
LiveStreamSound-MSI über eine bestehende Installation drüber-installiert.

## Kurzfassung

| Bereich | Verhalten |
|---|---|
| **Programm-Dateien** (`C:\Program Files\LiveStreamSound\`) | **Vollständig ausgetauscht** |
| **Firewall-Regeln** | Alte gelöscht, neue eingerichtet |
| **Event-Log-Source** | Re-registriert |
| **WER-Crash-Dump-Konfig** | Re-registriert |
| **HKLM-Registry** (`Software\LiveStreamSound`) | Komplett neu |
| **Start-Menü + Desktop-Shortcuts** | Aktualisiert auf neue EXE |
| **User-Logs** (`%LOCALAPPDATA%\LiveStreamSound\<role>\logs\`) | **Bleiben unverändert** |
| **User-Settings** (`%LOCALAPPDATA%\LiveStreamSound\settings.json`) | **Bleibt unverändert** |
| **Crash-Dumps + Crash-Logs** | **Bleiben unverändert** |

## Mechanismus

Die MSI nutzt `<MajorUpgrade Schedule="afterInstallInitialize">` (WiX-Default).
Das bedeutet:

1. User startet die neue MSI
2. Windows-Installer erkennt: »ich habe denselben `UpgradeCode` schon installiert«
3. **Phase 1 — Uninstall der alten Version** (vor dem Install der neuen):
   - Alle Component-getrackten Files in `C:\Program Files\LiveStreamSound\` werden entfernt
   - Alle Firewall-Regeln (3 Stück) werden gelöscht
   - Event-Log-Source `LiveStreamSound` wird de-registriert
   - HKLM-Registry-Keys werden mit `ForceDeleteOnUninstall="yes"` weggeräumt
   - Shortcuts werden entfernt
4. **Phase 2 — Install der neuen Version**:
   - Alle neuen Files werden in `C:\Program Files\LiveStreamSound\` installiert
   - Firewall-Regeln werden neu eingerichtet
   - Event-Log-Source neu registriert
   - HKLM-Registry-Keys neu erstellt
   - Shortcuts neu erstellt mit Pfad zur neuen EXE
5. **Phase 3 — Cleanup**:
   - Stale Caches geleert
   - `ARPSIZE` etc. aktualisiert

## Was AbsoluterLY NICHT angefasst wird

`%LOCALAPPDATA%\LiveStreamSound\` enthält User-Daten die zwischen Sitzungen
persistiert werden müssen:

- `LiveStreamSound-Host\logs\YYYY-MM-DD.log` — Host-Logs (max 14 Tage Retention)
- `LiveStreamSound-Client\logs\YYYY-MM-DD.log` — Client-Logs
- `crashes\crash-YYYY-MM-DD.log` — Crash-Logs (managed exceptions)
- `crashes\dumps\*.dmp` — WER native Mini-Dumps
- `settings.json` — UI-Modus, Sprache, Auto-Mute-Toggle

Die MSI deklariert KEINE Components für diesen Pfad → Installer **ignoriert** ihn
komplett. Logs vom letzten Matura-Tag bleiben nach Upgrade auch lesbar.

## Wenn du die User-Daten LOS WERDEN willst

`%LOCALAPPDATA%\LiveStreamSound\` von Hand löschen, **bevor** du die neue MSI
installierst. Oder via PowerShell:

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\LiveStreamSound" -ErrorAction SilentlyContinue
```

## Same-Version-Upgrades (für Tests)

Mit `AllowSameVersionUpgrades="yes"` kannst du eine 0.4.0.0 über eine
existierende 0.4.0.0 drüber-installieren (z.B. nach einem Bugfix in derselben
Version). Ohne diesen Schalter würde der Installer mit »already installed«
abbrechen.

In Production: `git tag v0.4.1` + Workflow-Run mit dem neuen Tag → MSI hat
`Version=0.4.1.0` → MajorUpgrade greift natürlich.

## Verifikations-Skript (für IT-Audit)

Auf einem Test-PC:

```powershell
# Vor dem Upgrade
Get-WmiObject Win32_Product | Where-Object Name -eq "LiveStreamSound" | Format-List Name, Version, InstallDate
$alt = Get-ChildItem "C:\Program Files\LiveStreamSound" -Recurse | Select-Object Name, LastWriteTime
$alt | Export-Csv "$env:TEMP\lss-pre.csv"

# Upgrade durchführen
msiexec /i .\LiveStreamSound-neu.msi /quiet /log "$env:TEMP\lss-install.log"

# Nach dem Upgrade
Get-WmiObject Win32_Product | Where-Object Name -eq "LiveStreamSound" | Format-List Name, Version, InstallDate
$neu = Get-ChildItem "C:\Program Files\LiveStreamSound" -Recurse | Select-Object Name, LastWriteTime
$neu | Export-Csv "$env:TEMP\lss-post.csv"

# Diff
Compare-Object $alt $neu -Property Name, LastWriteTime
```

Erwartetes Ergebnis:
- `Version` ist neu
- `InstallDate` ist heute
- Alle Files haben `LastWriteTime` von heute (= alle ausgetauscht)

## Bekannte Edge-Cases

### App ist beim Upgrade offen
Wenn `LiveStreamSound.exe` läuft während der MSI durchstartet, scheitert der
File-Replace. Windows fragt nach ob die App geschlossen werden soll (UI-Mode)
oder verschiebt das Replace auf den nächsten Boot (`/quiet`-Mode). User soll
**vor dem Upgrade die App schließen**.

### Firewall-Regeln auch via GPO ausgerollt
Wenn parallel zur MSI-Installation die GPO-Skripte aus `deployment/` laufen
(`Add-LiveStreamSoundFirewallRules.ps1`), entstehen **zwei Sätze** identisch
benannter Firewall-Regeln. Nicht kaputt, aber unschön. Entweder MSI **oder** GPO,
nicht beides.

### Mixed-Version-Deployment während Matura
Wenn ein Raum-PC noch v0.3 hat und der Lehrer-Laptop schon v0.4: HELLO
schlägt mit `PROTOCOL_VERSION_MISMATCH` fehl (clean error, nicht Crash). Vor
einer Matura sicherstellen dass alle PCs auf derselben Version sind.

## Rollback

Wenn ein neuer MSI Probleme macht und du auf alte Version zurück willst:
1. App schließen
2. Programme & Features → LiveStreamSound deinstallieren
3. Alte MSI-Datei (aus letzter funktionierender Version) installieren

User-Daten bleiben erhalten — du landest mit deinen Settings + Logs in der
alten Version.
