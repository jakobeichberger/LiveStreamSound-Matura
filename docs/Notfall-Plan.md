# LiveStreamSound — Notfall-Plan für Matura-Tag

> Drucken und an den Lehrer-Laptop kleben.
> Kontakt für Software-Probleme: **jakob@eichberger.tech**

---

## 60-Sekunden-Triage (am Tag der Matura, vor Beginn)

```
       ┌─────────────────────────────────────────────────┐
       │  Audio läuft auf dem Host-Laptop (VLC offen)?   │
       └─────────────────────────────────────────────────┘
                ↓ JA                ↓ NEIN
       ┌────────────────┐    ┌─────────────────────┐
       │ Test-Ton-Button│    │ VLC neu starten,     │
       │ klicken: alle  │    │ Audio-Ausgabegerät   │
       │ Räume zeigen   │    │ in Windows Settings  │
       │ grünes Häkchen?│    │ prüfen.              │
       └────────────────┘    └─────────────────────┘
        ↓ JA       ↓ NEIN
   ALLES OK    Nicht-grüne Räume:
                · Lid offen?
                · WLAN verbunden?
                · LSS-App offen, "Empfangen"?
                · Code richtig eingegeben?
                · Falls nein → Plan B
```

---

## Wenn nichts hilft: Plan B (analog)

| Verfügbar? | Plan |
|---|---|
| **3.5mm Aux-Kabel** + Verstärker im Raum | Host-Laptop direkt an Verstärker anschließen, Audio physisch durch den Raum verteilen |
| **Bluetooth-Lautsprecher** (Reserve) | Mobilen BT-Speaker aufstellen, Host-Laptop verbinden |
| **USB-Stick mit Audio-Datei** | An jeden Raum-PC den Stick anstecken, Datei lokal abspielen — synchron starten ist nicht möglich aber Inhalt wird gespielt |
| **Smartphone** des Lehrers mit Audio | Über Beamer-Lautsprecher, Notlösung |

**Wichtig:** Eine analoge Backup-Methode immer im Raum haben — auch wenn LSS sonst zuverlässig funktioniert.

---

## IT-Admin-Cheatsheet (für IT-Support, falls da)

### MSI-Status prüfen
```powershell
Get-WmiObject Win32_Product | Where-Object Name -eq "LiveStreamSound"
```

### MSI neu installieren (admin)
```powershell
msiexec /i \\<server>\share\LiveStreamSound.msi /quiet /norestart
```

### Logs einsammeln
- Pfad: `%LOCALAPPDATA%\LiveStreamSound\<role>\logs\`
- Oder im laufenden Programm: »Diagnose-Paket erstellen« (Log-Panel) → ZIP landet auf Desktop

### Firewall-Status prüfen
```powershell
Get-NetFirewallRule -DisplayGroup "LiveStreamSound"
```

### App vollständig deinstallieren + neu installieren
```powershell
$msi = Get-WmiObject Win32_Product | ? Name -eq "LiveStreamSound"
$msi.Uninstall()
msiexec /i \\<server>\share\LiveStreamSound.msi /quiet
```

---

## Bekannte Probleme + Lösung

| Symptom | Ursache | Lösung |
|---|---|---|
| Client zeigt "Verbindung wird wiederhergestellt…" | WLAN-Hiccup | 5 Min warten, dann automatisch wieder verbunden. Sonst »Trennen« + neu verbinden. |
| Test-Ton hörbar im Host, aber Client zeigt rot | Firewall blockiert UDP | Windows-Firewall-Regel `LiveStreamSound (UDP Audio)` aktivieren oder MSI re-deployen |
| Mehrere "Räume mit gleicher Nummer" | mDNS-Duplikat (z.B. zwei Laptops im gleichen Raum) | Einen ausschalten oder beim mDNS-Eintrag den IP-Suffix ansehen |
| App startet nicht, SmartScreen-Popup | Unsignierte MSI / fresh image | "Weitere Informationen" → "Trotzdem ausführen" — einmalig pro PC |
| Host startet, aber kein Code wird angezeigt | Port 5000/5001/5002 belegt | Andere LSS-Instanzen schließen, oder Laptop neu starten |
| Lehrer hört Audio aus eigenem Laptop (störend) | Auto-Mute ist abgeschaltet | Toggle "Host stumm während Session" im Header anklicken |
| Versions-Konflikt-Fehler "Host und Client unterschiedliche Versionen" | Mixed-version-Deployment | Beide auf gleiche LSS-Version updaten (über IT-Admin / Intune) |

---

## 5-Minuten-Pre-Flight (am Vortag oder Morgen)

1. Host-Laptop einschalten, **»Senden«** wählen, Sitzung starten
2. Code merken
3. Erster Raum-PC: **»Empfangen«**, mDNS-Discovery wartet, Host-Tile sollte erscheinen
4. Code eintippen, Verbindung sollte stehen (grünes Pulse-Ring)
5. Im Host: »Test-Ton (10s)« klicken — dieser Raum sollte den Ton hören
6. Wiederholen für jeden Raum (nicht alle zusammen — einzeln zur Erkennung)
7. Wenn alle grün: Probe-Lauf mit echter VLC-Audio-Datei, 30 Sekunden
8. Sitzung **NICHT beenden** wenn Matura unmittelbar bevorsteht — der Stop-Click wird oft gemacht und dann ist alles weg

---

## Logs / Diagnose-Paket einsenden

Wenn etwas schief geht:
1. Im laufenden Programm: Log-Icon (oben rechts) → **»Diagnose-Paket erstellen«**
2. ZIP landet auf dem Desktop, Name: `LiveStreamSound-Diagnose-2026-05-04-1042-Host.zip`
3. Per E-Mail an `jakob@eichberger.tech` mit kurzer Beschreibung was passiert ist

Das Paket enthält Logs aller Komponenten + System-Info (kein PII außer dem Hostnamen). Logs-Retention: 14 Tage lokal, dann automatisch gelöscht. Daten verlassen das Gerät nicht außer du sendest sie aktiv.
