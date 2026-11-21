$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$existing = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B5758E5F-AF08-49FA-ABAC-41F740F19A85}_is1'
if (Test-Path $existing) { throw 'ScanControl ist bereits installiert; automatischer Installationstest würde die vorhandene Installation verändern.' }
$installer = Get-ChildItem (Join-Path $root 'artifacts\installer') -Filter 'ScanControl-Setup-*-win-x64.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) { throw 'Installer fehlt.' }
$target = [IO.Path]::GetFullPath((Join-Path $root ('artifacts\install-test-' + [guid]::NewGuid().ToString('N'))))
if (-not $target.StartsWith((Join-Path $root 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'Ungültiges Testverzeichnis.' }
$settingsPath = Join-Path $env:LOCALAPPDATA 'ScanControl\settings.json'
$before = if (Test-Path $settingsPath) { (Get-FileHash $settingsPath).Hash } else { 'missing' }
$log = Join-Path $root 'artifacts\installer-smoke.log'
$install = Start-Process -FilePath $installer.FullName -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', "/DIR=`"$target`"", "/LOG=`"$log`"") -WindowStyle Hidden -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installation fehlgeschlagen: $($install.ExitCode)" }
if (-not (Test-Path (Join-Path $target 'ScanControl.App.exe'))) { throw 'Installierte EXE fehlt.' }
$payload = Join-Path $root 'artifacts\installer-payload'
foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
    $relative = $file.FullName.Substring($payload.Length + 1)
    $installed = Join-Path $target $relative
    if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Installationsdatei beschädigt/fehlend: $relative" }
}
if (-not (Test-Path (Join-Path $target 'docs\licenses\NAudio-LICENSE.txt'))) { throw 'Lizenzdatei fehlt im Installer.' }
$sentinel = Join-Path $target 'Testprojekt-behalten.json'
[IO.File]::WriteAllText($sentinel, '{"test":"preserve user files"}')
$uninstaller = Join-Path $target 'unins000.exe'
if (-not $uninstaller.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ungültiger Uninstallerpfad.' }
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw "Deinstallation fehlgeschlagen: $($uninstall.ExitCode)" }
if (Test-Path (Join-Path $target 'ScanControl.App.exe')) { throw 'EXE nach Deinstallation noch vorhanden.' }
if (-not (Test-Path $sentinel)) { throw 'Unbekannte Projektdatei wurde bei Deinstallation gelöscht.' }
$after = if (Test-Path $settingsPath) { (Get-FileHash $settingsPath).Hash } else { 'missing' }
if ($before -ne $after) { throw 'Bestehende Einstellungen wurden verändert.' }
Write-Output 'INSTALLER TEST PASSED: payload hashes, licenses, uninstall, user-file preservation, unchanged settings.'
