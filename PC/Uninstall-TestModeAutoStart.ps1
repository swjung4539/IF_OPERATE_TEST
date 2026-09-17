param(
    [string]$InstallDirectory = "$env:LOCALAPPDATA\MSC-HIFx\TestMode"
)

$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'MSC-HIFx TestMode' -ErrorAction SilentlyContinue
Get-Process 'TestMode.App' -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path -LiteralPath $InstallDirectory) {
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
}

Write-Host 'TestMode USB 자동 실행을 제거했습니다.'
