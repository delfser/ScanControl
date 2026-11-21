# ScanControl auf einem zweiten PC installieren

1. Im privaten GitHub-Repository `delfser/ScanControl` anmelden und **Releases** öffnen.
2. Unter **Assets** die Datei `ScanControl-Setup-0.2.1-win-x64.exe` herunterladen. Die automatisch angebotenen „Source code“-Archive sind nicht der Installer.
3. Setup starten und Zielordner bestätigen. Optional „Desktopsymbol erstellen“ auswählen.
4. ScanControl über Startmenü oder Desktop öffnen. Beim ersten Start ist die Simulation aktiv.

Voraussetzung: Windows 10 (ab Version 1809) oder Windows 11, x64. Die .NET-Laufzeit ist enthalten. Die Installation erfolgt nur für den angemeldeten Benutzer unter `%LOCALAPPDATA%\Programs\ScanControl`; Administratorrechte sind nicht erforderlich. Für einen USB-RS-232-Adapter kann separat der Treiber des Herstellers nötig sein.

Der Installer ist nicht mit einem Codesigning-Zertifikat signiert; Windows kann deshalb einen unbekannten Herausgeber anzeigen. Der SHA-256-Dateihash steht bei den Release-Dateien in `SHA256SUMS.txt`.

Projekte, Sicherungen und Protokolle liegen standardmäßig unter `Dokumente\ScanControl`. Einstellungen liegen unter `%LOCALAPPDATA%\ScanControl`. Diese Dateien werden bei einer Aktualisierung oder Deinstallation nicht gelöscht. Bestehende Einstellungen werden nicht zwischen PCs synchronisiert; COM-Port und Audioaufnahmegerät auf jedem PC passend einstellen. Projektdateien können über „Projekt speichern/öffnen“ übertragen werden.

Zum Aktualisieren neueren Installer ausführen. Vorher ScanControl regulär schließen und lokale Änderungen speichern. Zum Entfernen die Windows-Einstellungen „Installierte Apps“ oder den Startmenüeintrag zur Deinstallation verwenden.

## Installer selbst bauen

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Build-Installer.ps1
```

Das Skript prüft Tests, veröffentlicht eine selbstenthaltende Windows-x64-Anwendung und erstellt Setup plus SHA256SUMS unter `artifacts/installer`. Der signierte Inno-Setup-Compiler 6.7.4 wird bei Bedarf mit fester SHA-256-Prüfung portabel unter `artifacts/tools` bereitgestellt. Es wird kein Compiler global installiert. Alternativ kann `-InnoCompiler C:\Pfad\ISCC.exe` angegeben werden.

Die GitHub-Aktion erstellt bei Änderungen ein herunterladbares Build-Artefakt. Ein Versions-Tag wie `v0.2.0` erstellt zusätzlich eine als Vorabversion gekennzeichnete GitHub-Release. Hardwaretests sind dadurch nicht ersetzt.

## Schwarzes Fenster beim Start

Ab Version 0.2.1 wird die Oberfläche vor der Geräteinitialisierung gezeichnet. Audiogeräte werden erst in den Einstellungen abgefragt. Die Grafikdarstellung verwendet einen Software-Kompatibilitätsmodus. Im Startmenü kann außerdem „ScanControl (Kompatibilitätsstart)“ gewählt werden, um gespeicherte Einstellungen und automatische Hardwareverbindungen für diesen Start zu umgehen. Projekte und Einstellungen werden dadurch nicht gelöscht.

Falls das Problem bestehen bleibt: `%LOCALAPPDATA%\ScanControl\Diagnose\startup.log` öffnen. Der Eintrag `Content rendered` zeigt, dass WPF das Fenster gerendert hat; `READY` zeigt die abgeschlossene Initialisierung. Diese Einträge beweisen nicht, dass der Remote-PC den Bildinhalt fehlerfrei anzeigt, helfen aber, Initialisierungs- und Darstellungsprobleme zu unterscheiden.
