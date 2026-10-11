@echo off
setlocal

dotnet build "%~dp0OperateTest.sln" --configuration Debug
if errorlevel 1 exit /b %errorlevel%

echo Debug executable: %~dp0artifacts\Debug\OperateTest.exe
