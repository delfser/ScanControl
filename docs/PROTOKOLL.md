# BC780XLT-Protokolldienst

Primärquelle: [Uniden Control Codes BC780XLT, v1.04](https://www.uniden.info/download/files/PCi_bc780_v104.pdf), enthaltene Operation Specification Remote Command v1.03. Die Originaldatei wird nicht mitgeliefert. Unabhängige Implementierung, keine ProScan-Dateien oder fremder Programmquellcode.

## Implementierter Umfang

| Befehl | Verwendung | Gedruckte Seite |
|---|---|---|
| SI | Modellprüfung beim Verbinden | 43–44 |
| KEY | SCAN, MAN, HOLD, LIMIT, L/O, SEARCH | 28 |
| LCD CHN/FRQ/LINE1/LINE2 | tatsächliche Anzeigefelder einzeln lesen | 29–30 |
| MD | Betriebsmodus auslesen | 35 |
| PM | Kanal lesen / Frequenz schreiben | 38–39 |
| MA | Zielkanal für kontextbezogene Befehle auswählen | 33–34 |
| TA C | Kanalname | 45–47 |
| RM / ST | Modulation / Schrittweite | 42 / 45 |
| DL / AT / AR / LO | Verzögerung, Abschwächung, Tape-Out, Lockout | 8 / 3–4 / 3 / 32 |
| CS | Tonnummer und Tone-Lock | 7, Tonnummerntabelle 58 |
| BA C | Beep-Alert pro Kanal | 4–5 |

ASCII, CR-Abschluss ohne LF. Die Transportwarteschlange wartet 60 ms zwischen Befehlen; es gibt keine parallelen Exchanges. Antworten sind begrenzt und werden strikt auf Felder und Kanalnummer geprüft. ERR, NG, FER und ORER gelten als Fehler. Bei Transportfehler wird der Port geschlossen: eine verspätete Antwort darf nicht dem nächsten Befehl zugeordnet werden. Unbekannte/ungefragte Meldungen werden nicht als Erfolg interpretiert.

PM verändert Delay/Abschwächung/Record. Der Schreiber setzt diese Eigenschaften danach erneut, ebenso die übrigen unterstützten Einstellungen. Der Übertragungsdienst liest anschließend den gesamten Kanal erneut und vergleicht das Datenmodell. Eine OK-Antwort allein reicht nicht als Upload-Erfolg. KEY-OK bestätigt laut Spezifikation nicht die tatsächliche Wirksamkeit der Taste; maßgeblich bleibt die nachfolgende Anzeige.

Kein AC (Speicherinitialisierung), kein proprietärer Clone-Modus, keine Lautstärke-/Squelchschreibbefehle. Die LCD-Darstellung zeigt gelesene Felder, bildet aber nicht sämtliche Icons und Blinkzustände des physischen Displays nach. Modusnummern werden unverändert dargestellt. Unbekannte Firmwarevarianten müssen anhand tatsächlicher Antworten geprüft werden; keine stillschweigende Protokollannahme.

Nicht implementierte dokumentierte Funktionen (u.a. Banksysteme und Talkgroups) sind in der README als offen gekennzeichnet. Der Befehlsumfang ist kein vollständiger Geräteclone.

This product contains Uniden proprietary and/or copyright control codes. Used with permission.
