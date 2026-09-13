$ErrorActionPreference = 'Stop'
$diagnosticExe = Join-Path $PSScriptRoot '..\artifacts\win-x64\TabCloser.exe'

if (-not (Test-Path -LiteralPath $diagnosticExe)) {
    throw 'Diagnostic executable is missing. See tools/DIAGNOSTICS.md for the publish command.'
}

if (Get-Process -Name TabCloser -ErrorAction SilentlyContinue) {
    throw 'Choose Exit from the running TabCloser tray menu first. Enabled off/on or hiding the icon does not exit the app.'
}

Start-Process -FilePath (Resolve-Path -LiteralPath $diagnosticExe).Path -ArgumentList '--diagnostics' -WindowStyle Hidden
Write-Output 'Diagnostic launch requested. Confirm the tray menu shows Diagnostic session v4 (auto recovery).'
Write-Output 'Logs: %LOCALAPPDATA%\TabCloser\Diagnostics'
Write-Output 'After CS2 exit, make two failed attempts within 10 seconds, wait 3 seconds, then try two fresh gestures. Do not use Restart worker.'
Write-Output 'Leave TabCloser running for at least 10 seconds afterward and report whether automatic recovery worked.'
