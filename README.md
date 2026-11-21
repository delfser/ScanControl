# ScanControl – Uniden BC780XLT

Windows-WPF-Anwendung in C#/.NET 10. Dunkles Design mit roter Navigation und orangefarbener Scanneranzeige nach der gelieferten Bildvorlage. Keine Audioaufnahme, Wiedergabe oder Streams.

Die Scannerfront besitzt eine geschichtete Gehäuseblende, ein kompaktes bernsteinfarbenes LCD, einen Ziffernblock und erhabene Funktionstasten. Ziffern und Funktionstasten senden im Hardwarebetrieb die dokumentierten KEY-Befehle. Die gezeichneten VFO- und Volume/Squelch-Knöpfe kennzeichnen physische Bedienelemente am Gerät; sie stellen keine fernsteuerbare Lautstärkefunktion dar.

## Start und Installation

Für den zweiten PC gibt es einen Windows-Installer mit Startmenüeintrag, optionalem Desktopsymbol und Deinstallation. Anleitung: [docs/INSTALLATION.md](docs/INSTALLATION.md). Installer bauen: `powershell.exe -NoProfile -File tools/Build-Installer.ps1`. Die benötigte .NET-Laufzeit ist enthalten; Projekte und Einstellungen bleiben bei Deinstallation erhalten.

Die veröffentlichte Anwendung liegt nach dem Publish in `artifacts/win-x64/ScanControl.App.exe`. Den **gesamten Ordner** kopieren; die EXE benötigt die mitgelieferten DLLs. Keine Installation und keine zusätzliche .NET-Runtime nötig (Windows x64, selbstenthaltender Publish). Das Programm ist nicht signiert.

Für Entwicklung: Windows 10/11 x64, .NET SDK 10 und Visual Studio mit WPF-/Desktopentwicklung und .NET-10-Unterstützung. `ScanControl.sln` öffnen und `ScanControl.App` als Startprojekt wählen. Debug und Release sind eingerichtet.

```powershell
dotnet restore ScanControl.sln
dotnet build ScanControl.sln -c Debug
dotnet test ScanControl.sln -c Release
dotnet publish ScanControl.App -p:PublishProfile=Windows
```

Beim ersten Start verbindet die Anwendung ausschließlich mit dem Simulator und zeigt zehn Beispielkanäle. Die Frequenzen/Namen sind Demonstrationsdaten und keine reale Kanalzuordnung. „Vom Scanner lesen“ mit Umfang „Alle 500 Kanäle“ lädt den gesamten simulierten Bestand. SCAN und HOLD/MAN sind testbar. SEARCH ist im Simulator ausdrücklich nicht implementiert.

## Offline weiterarbeiten

Version 0.2.0 enthält ein eigenes ScanControl-Icon in der EXE, im Fenster und im Programmkopf sowie „Über ScanControl“ mit Versionsanzeige. Bearbeitbare Iconquellen liegen unter `ScanControl.App/Assets` (SVG und WPF-XAML). `powershell.exe -NoProfile -STA -File tools/Build-Icon.ps1` erzeugt PNG und ICO reproduzierbar. Das ICO enthält 16, 24, 32, 48, 64, 128 und 256 Pixel. Windows kann bei bereits bekannten EXE-Pfaden vorübergehend noch ein zwischengespeichertes altes Symbol anzeigen.

- Kanalsuche oberhalb der Tabelle: Nummer, Name, Frequenz (Punkt oder Komma) und Modulation; kombinierbar mit der Bankauswahl. Die Anzeige nennt sichtbare Kanäle und Gesamtbestand. Übertragungsumfang „Alle 500 Kanäle“ bleibt unabhängig vom Suchfilter.
- Rückgängig/Wiederholen: bis zu 50 lokale Bearbeitungsschritte, einschließlich Einfügen, Leeren, CSV-Übernahme und eingelesenen Daten. Diese Aktionen senden keine Scannerbefehle. Öffnen eines anderen Projekts beginnt einen neuen Verlauf. Nach Rückgängig bleibt das Projekt vorsichtshalber als geändert markiert.
- CSV-Import zeigt nach vollständiger Validierung zuerst alle einzulesenden Kanäle zur Prüfung. Erst „In Tabelle übernehmen“ und gegebenenfalls die Rückfrage zu lokalen Änderungen ersetzen den Bestand.
- Simulator: Kanalnummer 1–500 über den Ziffernblock eingeben und mit E oder MAN übernehmen. HOLD/LIMIT navigieren mit Überlauf an den Speichergrenzen. SCAN durchsucht alle 500 Speicherplätze und überspringt leere sowie gesperrte Kanäle. Ohne geeignete Kanäle erscheint eine klare Meldung. Andere nicht implementierte Simulatortasten melden ihre Einschränkung.

Diese Funktionen sind ohne Gerät testbar; sie ersetzen keinen späteren Hardwaretest. Die zusätzliche Uploadprüfung sperrt auch das Überschreiben bestehender Trunkingkanäle durch konventionelle Kanäle.

## Scanner anschließen

BC780XLT über ein gerades RS-232-Kabel mit dem PC verbinden, bei Bedarf mit USB-RS-232-Adapter und dessen Herstellertreiber. Kein Nullmodemkabel. Am Scanner den Remote-/PC-Control-Modus nach Gerätehandbuch aktivieren; Baudrate auf beiden Seiten gleich einstellen. COM-Nummer im Windows-Geräte-Manager nachsehen. Unterstützt: 2400, 4800, 9600 und 19200 Baud, ASCII, 8 Datenbits, keine Parität, 1 Stoppbit, keine Flusskontrolle, Abschluss CR. Standard: 9600 Baud.

Simulationsmodus abwählen, COM-Port einstellen und „Verbinden“ wählen. Die Modellantwort wird geprüft. Bei einem Abbruch wird die Verbindung verworfen; „Verbinden“ eröffnet einen neuen Port. Keine automatische Fortsetzung eines abgebrochenen Uploads. Andere Scannerprogramme müssen den COM-Port freigeben. Alarmmonitor darf seinen Audioeingang weiterhin unabhängig verwenden.

## Lesen, bearbeiten, übertragen

1. Für Hardwarebetrieb zunächst echte Scannerkanäle lesen. Simulationsprojekte sind für Hardwareuploads gesperrt.
2. Umfang wählen: alle 500 Speicherplätze, ausgewählte Bänke (Mehrfachauswahl mit Strg) oder markierte Tabellenkanäle. Für einen Upload müssen alle Kanäle des gewählten Umfangs lokal vorhanden sein.
3. Tabellenwerte mit Doppelklick bearbeiten. Weitere Felder liegen rechts im horizontalen Scrollbereich. Namen: maximal 16 druckbare ASCII-Zeichen. Frequenzen: MHz mit Punkt, 100-Hz-Auflösung. Ungültige Werte verhindern Speichern/Übertragen.
4. „Zum Scanner übertragen“ liest zuerst den gewählten Bestand und speichert ihn als JSON-Sicherung. Schlägt dies fehl, erfolgt kein Upload. Die Änderungen werden vollständig zur Bestätigung angezeigt.
5. Vor dem Schreiben wird der Scannerstand nochmals mit der Sicherung verglichen. Geschrieben werden ausschließlich Unterschiede. Jeder geschriebene Kanal wird vollständig zurückgelesen und verglichen. Bei Abweichung, Timeout oder Abbruch wird **kein Erfolg** gemeldet.

Lesen übernimmt den neuen Bestand erst nach vollständigem Empfang. Fehlgeschlagene Lesevorgänge können nach sicherem Neuverbinden wiederholt werden. Lokale Änderungen erfordern eine Rückfrage vor dem Ersetzen. Ein manueller Abbruch kann beim seriellen Lesen bis zum konfigurierten Timeout dauern.

Während eines Vorgangs hält eine Sperre auch über den Bestätigungsdialog hinweg alle weiteren Scannerzugriffe einschließlich Displaypolling zurück. Die Oberfläche bleibt asynchron bedienbar. Ein dauerhaftes Transferjournal (`*.transfer.json`) steht neben der Sicherung: `Unvollständig` vor dem ersten Schreiben, `Verifiziert` erst nach erfolgreichem Vergleich. Nach Prozessabsturz oder Stromausfall bleiben unvollständige Journale sichtbar. Es erfolgt kein automatischer Rollback; Sicherung laden, prüfen und erneut bestätigten Upload ausführen.

**Wichtig:** Auslesen der erweiterten Kanaleigenschaften verwendet die dokumentierte Kanalauswahl und versetzt den Scanner in Manual. Ein vorheriger Scan wird nicht automatisch wieder gestartet. Während eines Uploads auch am Gerät selbst keine Einstellungen ändern.

## Projekte, CSV und Sicherungen

JSON-Projektversion 1 enthält Modell, Herkunftsmodus, Zeitstempel und sämtliche vom Kanalmodell unterstützten Eigenschaften: Speicherplatz/Bank, Frequenz, Name, Modulation, Schrittweite, Trunkflag, Lockout, Abschwächung, Recordflag, Beep-Alert, Delay, CTCSS/DCS-Tonnummer und Tone-Lock. Recordflag schaltet lediglich die Scanner-Tape-Out-Funktion; ScanControl zeichnet nichts auf.

Sicherungen liegen standardmäßig unter `Dokumente/ScanControl/Sicherungen`; Diagnoseprotokolle unter `Diagnose`. Vor Hardwareupload wird immer gesichert. Vor Löschen/Einfügen/CSV-Import und beim Überschreiben ungespeicherter Änderungen entstehen lokale Sicherungen. Weitere automatische lokale Sicherungen lassen sich in den Einstellungen abschalten. Das Speichern einer bestehenden gültigen Projektdatei sichert deren vorherige Version. Dateischreiben erfolgt über temporäre Datei und anschließendes Umbenennen.

„Sicherung wiederherstellen“ lädt eine JSON-Datei zur Prüfung in die Tabelle. Das verändert den Scanner noch nicht. CSV verwendet UTF-8, Semikolon, feste englische Spaltenüberschriften und Punkt als Dezimaltrenner; am besten zuerst eine Datei exportieren. Alle Felder bleiben beim CSV-Rundlauf erhalten. Fehlerhafte Zeilen, doppelte Nummern und ungültige Werte verhindern die gesamte Übernahme. CSV ersetzt nach Bestätigung den lokalen Kanalbestand und behält dessen Herkunftsmodus bei.

## Aufnahmepegel und Alarmmonitor

Einstellungen → Windows-Aufnahmepegel: Aufnahmegerät auswählen, bevorzugt Line-in. ScanControl enumeriert ausschließlich aktive **Capture-Endpunkte** über Windows Core Audio (NAudio), niemals Lautsprecherausgänge. Gewünschten Pegel 0–100 % und optional Mute einstellen, anschließend „Pegel anwenden“. „Aktuellen Pegel lesen“ fragt den tatsächlichen Windows-Wert ab. Einstellungen speichern bewahrt Geräte-ID, Wunschpegel und Mute. Beim Start werden gespeicherte Werte angezeigt, aber nicht ungefragt auf Windows angewendet.

Es wird weder WASAPI-Capture noch ein exklusiver Stream geöffnet. Keine virtuellen Kabel oder zusätzlichen Audiotreiber erforderlich. Alarmmonitor muss denselben Windows-Eingang verwenden. Ob dessen eigener Aufnahmeweg die gemeinsame Pegeländerung berücksichtigt, hängt von Treiber und Alarmmonitor ab und muss an der realen Hardware geprüft werden. Nicht verfügbare Pegel-/Mute-Steuerung oder nicht übernommene Werte erzeugen eine verständliche Meldung. Es gibt keine physische Scannerlautstärke über RS-232 und keine Audio-Pegelanzeige in dieser Version.

## Stand und Grenzen

Dies ist ein ausführbarer Stand für **konventionelle Kanäle**. Debug/Release, automatisierte Tests und Simulator lassen sich ohne Scanner prüfen. Hardwarekommunikation und Zusammenspiel mit Alarmmonitor wurden ohne angeschlossenes Gerät **nicht als Hardwaretest bestätigt**.

Noch nicht umgesetzt: vollständige globale Scannerkonfiguration, Banknamen/-systeme, Prioritätszuordnung, Suchbankverwaltung, Talkgroup-/Trunking-Systemverwaltung und vollständiger Speicherclone. Trunkingflags können eingelesen und in Dateien erhalten werden; Trunkinguploads sind gesperrt. „Alle 500 Kanäle“ bedeutet alle unterstützten Kanaldaten, **keinen vollständigen Scannerclone**. Diese Funktionen sind grundsätzlich teilweise dokumentiert; ihre fehlende Umsetzung ist eine Grenze dieser Version und keine Behauptung einer Protokollunmöglichkeit. Das Gerät kann Werte auf sein Frequenzraster runden; der Vergleich meldet dann eine Abweichung statt Erfolg. Für leere Speicher liefert manche Firmware möglicherweise nicht alle erweiterten Werte; unbekannte Antworten werden abgelehnt und nicht erfunden.

Die gelieferte Textdatei beginnt mitten im Downloadabschnitt. Abschnitte 1–6 des ursprünglichen Auftrags lagen nicht vor.

## Architektur und Tests

- `ScanControl.App`: WPF, Bedienabläufe, Bestätigungen, Capture-Pegelsteuerung.
- `ScanControl.Core`: Kanalmodell, Validierung, Scannervertrag, Simulator, Übertragungsverifikation.
- `ScanControl.Protocol.BC780XLT`: dokumentierte Befehle und strikter Parser.
- `ScanControl.Infrastructure`: serielle Warteschlange, JSON, CSV, atomare Dateien und Sicherungen.
- `ScanControl.Tests`: Parser, Wertevalidierung, JSON/CSV, Sicherungen, Abbruch, Kommunikationsfehler und fehlerhafte Rückleseverifikation.

Weitere Scannermodelle implementieren `IScanner`; die Oberfläche verwendet diesen Vertrag. Der Simulator verwendet keinen seriellen Transport. Protokolldetails und Quellen: [docs/PROTOKOLL.md](docs/PROTOKOLL.md).

This product contains Uniden proprietary and/or copyright control codes. Used with permission.
