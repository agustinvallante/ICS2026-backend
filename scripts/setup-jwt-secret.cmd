@echo off

powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0setup-jwt-secret.ps1"

if errorlevel 1 (
  echo Ocurrio un error al configurar el secreto.
)

pause
