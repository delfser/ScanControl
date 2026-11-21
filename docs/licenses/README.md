# Mitgelieferte Fremdkomponenten

Die Installation enthält NAudio (MIT) und Microsoft .NET einschließlich Windows Desktop Runtime (MIT und zusätzliche Komponentenhinweise). Die zugehörigen Lizenztexte liegen in diesem Verzeichnis. NAudio dient hier ausschließlich der Steuerung von Windows-Aufnahmeendpunkten.

Der Inno-Setup-Compiler wird nur beim Bauen verwendet und nicht als Entwicklungswerkzeug mit ScanControl weiterverteilt; sein Installer fügt die eigene Installations-/Deinstallationslaufzeit hinzu. Bezugsquelle und geprüfte Version stehen in `tools/Build-Installer.ps1`.

Die Uniden-Befehlsdokumentation wird nicht mitgeliefert. Quellen und Hinweis zur unabhängigen Protokollimplementierung stehen in `docs/PROTOKOLL.md`.
