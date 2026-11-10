@echo off
setlocal DisableDelayedExpansion
rem Initialize module paths for Windows PowerShell even when called from PowerShell 7.
set "PSModulePath="
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-service.ps1" %*
exit /b %errorlevel%
