param(
    [string]$InstallDirectory = "$env:LOCALAPPDATA\MSC-HIFx\TestMode"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TestMode.App\TestMode.App.csproj'
if (-not (Test-Path -LiteralPath $project)) {
    throw "TestMode.App.csproj을 찾을 수 없습니다: $project"
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
dotnet publish $project -c Release --self-contained false -o $InstallDirectory
if ($LASTEXITCODE -ne 0) { throw 'TestMode 게시에 실패했습니다.' }

$executable = Join-Path $InstallDirectory 'TestMode.App.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = '"' + $executable + '" --usb-watch'
New-ItemProperty -Path $runKey -Name 'MSC-HIFx TestMode' -PropertyType String -Value $command -Force | Out-Null

Get-Process 'TestMode.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process -FilePath $executable -ArgumentList '--usb-watch' -WindowStyle Hidden

Write-Host "TestMode USB 자동 실행을 설치했습니다: $executable"
