@echo off
rem
rem Factory reset: destroy the stack, its database, and its extents, then bring it back up empty and
rem optionally seed it.
rem
rem This deletes every object in the deployment. It is meant for development and demos.
rem
rem   reset.bat          reset and seed with sample data
rem   reset.bat --empty  reset and leave it empty
rem
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "COMPOSE_DIR=%SCRIPT_DIR%.."
set "SEED=1"

if /I "%~1"=="--empty" set "SEED=0"
if /I "%~1"=="-h" goto :usage
if /I "%~1"=="--help" goto :usage

pushd "%COMPOSE_DIR%" || exit /b 1

echo ==^> Stopping the stack and removing its volumes
rem -v is what makes this a factory reset rather than a restart: without it the Postgres data and the
rem extent files survive, and the node would come back up describing objects you meant to destroy.
docker compose down -v --remove-orphans
if errorlevel 1 goto :fail

echo ==^> Starting a clean stack
docker compose up -d --build
if errorlevel 1 goto :fail

echo ==^> Waiting for node1 to answer
set "HEALTHY=0"
for /L %%i in (1,1,60) do (
    if "!HEALTHY!"=="0" (
        curl --fail --silent --output NUL http://localhost:8000/v1.0/api/health
        if not errorlevel 1 (
            set "HEALTHY=1"
            echo     node1 is healthy
        ) else (
            timeout /t 2 /nobreak >NUL
        )
    )
)

if "!HEALTHY!"=="0" (
    echo node1 did not become healthy. Check: docker compose logs node1 1>&2
    goto :fail
)

if "%SEED%"=="1" (
    echo ==^> Seeding sample data
    python "%SCRIPT_DIR%seed.py" http://localhost:8000
    if errorlevel 1 goto :fail
)

echo.
echo Ready.
echo   Dashboard  http://localhost:3000  (connect to http://localhost:8000^)
echo   node1 REST http://localhost:8000    node2 REST http://localhost:8010
echo   S3         http://localhost:8001    RESP  localhost:6379
popd
exit /b 0

:usage
echo Usage: reset.bat [--empty]
echo   --empty   reset without seeding sample data
exit /b 0

:fail
popd
exit /b 1
