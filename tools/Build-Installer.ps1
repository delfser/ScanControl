param([string]$InnoCompiler, [switch]$SkipTests, [string]$PayloadDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    [xml]$project = Get-Content -LiteralPath 'ScanControl.App\ScanControl.App.csproj' -Raw
    $version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'App-Version muss Major.Minor.Patch entsprechen.' }
    if (-not $InnoCompiler) {
        $InnoCompiler = Join-Path $root 'artifacts\tools\InnoSetup\ISCC.exe'
        if (-not (Test-Path -LiteralPath $InnoCompiler)) {
            $toolRoot = Join-Path $root 'artifacts\tools'
            New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
            $download = Join-Path $toolRoot 'innosetup-6.7.4.exe'
            Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe' -OutFile $download -UseBasicParsing
            $expected = 'A549929319F9120716677C685535CA4671E4B937B5FDC87E2585F32D8EB30E8C'
            if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Inno-Setup-Prüfsumme stimmt nicht.' }
            $signature = Get-AuthenticodeSignature -LiteralPath $download
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B.V.') { throw 'Inno-Setup-Herstellersignatur ungültig.' }
            $destination = Join-Path $toolRoot 'InnoSetup'
            $setup = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/PORTABLE=1', "/DIR=`"$destination`"") -WindowStyle Hidden -Wait -PassThru
            if ($setup.ExitCode -ne 0) { throw "Inno Setup konnte nicht bereitgestellt werden: $($setup.ExitCode)" }
        }
    }
    if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Inno-Compiler nicht gefunden.' }
    if (-not $SkipTests) {
        & dotnet test ScanControl.Tests -c Release --artifacts-path artifacts/build
        if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen.' }
    }
    # Separate payload avoids touching a running portable application.
    $payload = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $root 'artifacts\installer-payload' }
    & dotnet publish ScanControl.App -c Release -r win-x64 --self-contained true --artifacts-path artifacts/build -o $payload
    if ($LASTEXITCODE -ne 0) { throw 'Publish fehlgeschlagen.' }
    $output = Join-Path $root 'artifacts\installer'
    & $InnoCompiler /Qp "/DAppVersion=$version" "/DPublishSource=$payload" "/DInstallerOutput=$output" (Join-Path $root 'installer\ScanControl.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer-Build fehlgeschlagen.' }
    $installer = Join-Path $output "ScanControl-Setup-$version-win-x64.exe"
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Installer erstellt: $installer"
} finally { Pop-Location }
