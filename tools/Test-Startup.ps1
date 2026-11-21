param([string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\installer-payload\ScanControl.App.exe'))
$ErrorActionPreference = 'Stop'
$resolved = [IO.Path]::GetFullPath($Exe)
foreach ($mode in @('--startup-smoke-test', '--startup-smoke-test --safe-start')) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $resolved -ArgumentList $mode -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id
        throw "Startup timeout: $mode"
    }
    if ($process.ExitCode -ne 0) { throw "Startup failed ($($process.ExitCode)): $mode" }
    Write-Output "STARTUP PASSED: $mode, $($watch.ElapsedMilliseconds) ms (rendered, simulated display ready, no audio/serial access)."
}
