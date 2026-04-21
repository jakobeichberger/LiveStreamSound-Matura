# LiveStreamSound — Anleitung für Lehrer/innen

Diese Anleitung beschreibt Schritt für Schritt, wie der Ton in der Matura-Klasse ohne Kabel verteilt wird.

## Überblick

Eine App, zwei Rollen. Beim Start wählst du:

- **📡 Ton senden** — dein Laptop ist der Host. Der Ton, der auf diesem PC abgespielt wird (VLC, Browser, …), geht ins Netz.
- **🎧 Ton empfangen** — dein Laptop ist Client am Beamer/Lautsprecher (HDMI). Er empfängt den Ton.

Beide Rollen starten aus derselben `.exe` — kein extra Installer pro Raum.

## Einmalig: Installation

1. `LiveStreamSound-<version>.msi` auf jedem Laptop doppelklicken → Installation läuft durch.
2. Die Firewall-Regeln (TCP 5000 + UDP 5001 + TCP 5002) werden automatisch angelegt.
3. **Kein separates .NET 10 nötig** — die Runtime ist im MSI enthalten.

## Vor der Prüfung (5 min Setup)

### Am Host-Laptop

1. LiveStreamSound starten.
2. Im Start-Screen **„📡 Ton senden"** wählen.
3. Klicke auf **Sitzung starten**. Ein 6-stelliger Code erscheint groß, dazu die Host-IP.
4. Schreibe Code + IP auf eine Tafel, oder verwende *direkte Einladung* (siehe unten).

### An jedem Raum-PC (Client)

**Option A — Client verbindet sich selbst:**
1. LiveStreamSound starten.
2. Im Start-Screen **„🎧 Ton empfangen"** wählen.
3. Host in der Liste *Gefundene Hosts* anklicken (oder IP manuell tippen).
4. 6-stelligen Code eintragen.
5. **Ausgabegerät** auf HDMI setzen.
6. **Verbinden** klicken.

**Option B — Host lädt Raum-PC ein (neu in v0.2!):**
1. Am Raum-PC: LiveStreamSound starten → „🎧 Ton empfangen" → warten (Start-Bildschirm bleibt offen).
2. Am Host-Laptop: **Sitzung starten** → Button **„Client hinzufügen"** oben klicken.
3. Im Dialog erscheint der Raum-PC mit seiner Raum-Nummer → anklicken.
4. Am Raum-PC erscheint die Einladungs-Karte mit Host-Name + Session-Code → **Annehmen**.
5. Verbindung ist automatisch da.

Wenn alles klappt, steht unten „Verbunden • Gut". Am Host siehst du den neuen Client unter **Klassenräume** gruppiert.

## Während der Prüfung

1. Öffne VLC oder den Browser auf dem Host-Laptop und spiele die Audio-Datei ab.
2. Der Ton kommt gleichzeitig auf allen Clients über HDMI aus.
3. Pro Raum am Host:
   - **Lautstärke** mit dem Slider (Prozent)
   - **Stumm** Schalter („Ton an" / „Stumm")
   - **Ausgabegerät** wechseln (z.B. auf HDMI 1)
   - **Client trennen** (rotes X)
   - Neu: **weitere Raum-PCs einladen** via „Client hinzufügen"

## Rolle nachträglich wechseln

Wenn du dich verklickt hast (z.B. „Empfangen" statt „Senden"):

- Oben rechts: **↔ Rolle wechseln** klicken.
- Wenn du verbunden bist, kommt ein Warndialog „Sitzung wird getrennt — Fortfahren?" → OK.
- Du landest wieder am Start-Screen.

## Nach der Prüfung

Klicke am Host auf **Sitzung beenden**. Alle Clients werden getrennt. Du kannst das Programm schließen.

## Problem? So geht's weiter

Beide Rollen zeigen unten eine **Verbindungsqualität**. Bei Problemen erscheint direkt, was wahrscheinlich falsch ist.

Häufige Situationen:

### „Kein Audio auf dem Host"
Nichts wird abgespielt. VLC starten.

### „WLAN isoliert Clients"
Das Schul-WLAN trennt Geräte voneinander — die automatische Suche findet den Host nicht.
→ **IP manuell eintragen** oder **Host lädt Client ein** (wenn mDNS blockiert ist, klappt oft auch Invite nicht; dann hilft nur LAN-Kabel oder IT fragen).

### „Firewall blockt den Audio-Stream"
Steuerverbindung läuft, aber kein Ton.
→ MSI sollte das erledigt haben. Falls nicht: bei erstem Start Windows-Firewall-Dialog **erlauben**.

### „Port 5000/5001 belegt"
Eine andere Instanz läuft noch oder ein anderes Programm blockiert den Port.
→ App schließen und neu starten, oder den anderen Prozess beenden.

### „Audio-Pakete gehen verloren" / „Hohe Latenz"
WLAN ist schwach. Näher an den AP oder LAN.

## Fehlerlog

Jede Rolle hat oben rechts ein **Fehlerlog-Icon** (📄). Dort laufen alle Events durch. **Log-Ordner öffnen** springt zu `%LOCALAPPDATA%\LiveStreamSound\...\logs\`.

## Tipps

- **Namen**: die Raum-PCs werden automatisch als „Raum 017" etc. erkannt, basierend auf dem Hostnamen (`HP-KB-017` → Raum 017). Überschreiben kann man vor dem Verbinden.
- **Hell/Dunkel-Modus**: Icon oben rechts (🌓). Default folgt System-Theme.
- **Sprache**: Icon oben rechts (🌐) — Deutsch/Englisch zur Laufzeit.
- **Lautstärke-Test**: vor der Prüfung einen Testton spielen und pro Raum einpegeln.
- **Invite-Flow**: am effizientesten wenn die Raum-PCs beim Einschalten automatisch LiveStreamSound starten und im Empfangs-Modus warten — dann reicht am Host 1 Klick pro Raum.

Viel Erfolg mit der Matura!
