@echo off
setlocal

rem Pull the latest published images and restart the stack on them.
rem
rem Volumes survive: `docker compose down` without -v keeps postgres-data and extent-data, so this
rem updates the running version without touching stored objects. Use factory\reset.bat when you
rem actually want the data gone.

cd /d "%~dp0"

docker compose down
if errorlevel 1 exit /b %errorlevel%

docker compose pull
if errorlevel 1 exit /b %errorlevel%

docker compose up -d
if errorlevel 1 exit /b %errorlevel%

docker ps -a

endlocal
