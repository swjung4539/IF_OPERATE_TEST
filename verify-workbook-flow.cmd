@echo off
setlocal

dotnet run --project "%~dp0OperateTest.FlowVerification\OperateTest.FlowVerification.csproj" --configuration Release
if errorlevel 1 exit /b %errorlevel%

echo Workbook flow verification completed successfully.
