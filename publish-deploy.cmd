@echo off
setlocal

dotnet publish "%~dp0OperateTest.App\OperateTest.App.csproj" --configuration Release
if errorlevel 1 exit /b %errorlevel%

echo Deployment executable: %~dp0artifacts\Deploy\OperateTest.exe
