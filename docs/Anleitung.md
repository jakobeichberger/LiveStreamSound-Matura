# LiveStreamSound — Anleitung für Lehrer/innen

Diese Anleitung beschreibt Schritt für Schritt, wie der Ton in der Matura-Klasse ohne Kabel verteilt wird.

## Überblick

Zwei Apps, zwei Rollen:

- **Host**: dein Laptop — startet die Sitzung, spielt die Audio-Datei ab (z.B. in VLC).
- **Client**: der Raum-Laptop am Beamer/Lautsprecher (HDMI) — empfängt den Ton.

Die beiden Apps finden sich automatisch, sobald sie im gleichen WLAN sind.

## Einmalig: Installation

1. Auf jedem Raum-PC `LiveStreamSound-Client-0.1.0.msi` doppelklicken → Installation durchläuft automatisch.
2. Auf deinem Laptop `LiveStreamSound-Host-0.1.0.msi` installieren.

Beide Installer fügen automatisch die nötigen Firewall-Regeln hinzu.

## Vor der Prüfung (5 min Setup)

### Am Host-Laptop

1. Starte **LiveStreamSound Host** aus dem Startmenü.
2. Klicke auf **Sitzung starten**. Ein 6-stelliger Code erscheint oben, dazu die Host-IP und ein QR-Code.
3. Schreibe dir **Code** und **IP** auf eine Tafel/Zettel — oder zeige den QR-Code am Beamer.

### An jedem Raum-PC (Client)

1. Starte **LiveStreamSound Client**.
2. Falls der Host-PC in der Liste **Gefundene Hosts** erscheint: anklicken. Sonst IP manuell eingeben.
3. **Code** eintippen.
4. **Ausgabegerät** prüfen (sollte auf HDMI-Ausgang stehen, damit der Beamer/Lautsprecher Ton bekommt).
5. **Verbinden** klicken.

Wenn alles klappt, steht unten „Verbunden • Good". Am Host siehst du den neuen Client in der Liste unter **Klassenräume**.

## Während der Prüfung

1. Öffne VLC oder den Browser auf dem Host-Laptop und spiele die Audio-Datei ab.
2. Der Ton kommt gleichzeitig auf allen Clients über HDMI aus.
3. Pro Raum kannst du am Host:
   - **Lautstärke** mit dem Slider regeln
   - **Stumm** schalten
   - **Ausgabegerät** wechseln (falls doch was Falsches eingestellt ist)
   - **Client trennen** (rotes Mülltonnen-Symbol)

## Nach der Prüfung

Klicke am Host auf **Sitzung beenden**. Alle Clients werden automatisch getrennt und können geschlossen werden.

## Problem? So geht's weiter

Beide Apps zeigen unten eine **Verbindungsqualität**. Bei Problemen erscheint dort direkt, was wahrscheinlich falsch ist und was du tun kannst.

Häufige Situationen:

### „Kein Audio auf dem Host"
Nichts wird gerade abgespielt. Öffne VLC und starte die Audio-Datei.

### „WLAN isoliert Clients"
Das Schul-WLAN trennt Geräte voneinander, die mDNS-Suche findet den Host nicht.
→ **IP manuell am Client eingeben**. Wenn möglich, die IT bitten, die Client-Isolation abzuschalten.

### „Firewall blockt den Audio-Stream"
Steuerverbindung läuft, aber kein Ton. Meist die Windows-Firewall.
→ Das sollte der Installer erledigen. Falls nicht: Beim ersten Start **Zugriff erlauben**.

### „Audio-Pakete gehen verloren" / „Hohe Netzwerk-Latenz"
Schwaches WLAN.
→ Host näher an den Access-Point. Wenn möglich: Ethernet-Kabel.

### Client reagiert nicht mehr
Am Host **Client trennen** (Mülltonne), dann am Client neu verbinden.

## Fehlerlog

Jede App hat rechts oben einen **Fehlerlog-Knopf** (📄). Dort siehst du, was passiert. Für den Support: **Log-Ordner öffnen** klickt sich direkt zum `%LOCALAPPDATA%\LiveStreamSound\...\logs\`-Ordner.

## Tipps

- **QR-Code benutzen**: Am Beamer zeigen, am Client-PC kann man den Link manuell aus dem QR übernehmen — schneller als abtippen.
- **Namen**: Die Client-App erkennt den Raum aus dem Hostnamen automatisch (`HP-KB-017` → „Raum 017"). Du kannst den Namen vor dem Verbinden noch überschreiben.
- **Hell/Dunkel-Modus**: Icon oben rechts (🌓).
- **Sprache**: Icon oben rechts (🌐).
- **Lautstärke testen**: Vor der Prüfung mit einem Testton (aus der Matura-CD oder YouTube) prüfen, dass alle Räume die gleiche Lautstärke haben.

Viel Erfolg mit der Matura!
