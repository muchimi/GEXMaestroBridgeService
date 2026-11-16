@echo off
setlocal DisableDelayedExpansion
set SCRIPT_DIR=%~dp0
set PS_SCRIPT=%SCRIPT_DIR%push.ps1

if not exist "%PS_SCRIPT%" (
  echo Missing script: "%PS_SCRIPT%"
  exit /b 1
)

rem Initialize module paths for Windows PowerShell even when called from PowerShell 7.
set "PSModulePath="
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set ERR=%ERRORLEVEL%
if not "%ERR%"=="0" exit /b %ERR%
exit /b 0
