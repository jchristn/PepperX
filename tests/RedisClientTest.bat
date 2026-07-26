@echo off
setlocal enabledelayedexpansion
::============================================================================
:: RedisClientTest.bat - exercises the PepperX RESP (Redis) surface via redis-cli.
::
:: Usage:
::   RedisClientTest.bat [--host HOST] [--port PORT] [--password PW] [--db N]
::
:: Arguments (all optional; system defaults are assumed when omitted):
::   --host HOST      RESP host.        Default: 127.0.0.1
::   --port PORT      RESP port.        Default: 6379
::   --password PW    AUTH password.    Default: (none; RESP is unauthenticated)
::   --db N           Database index.   Default: 15  (maps to container resp15)
::
:: The chosen database is FLUSHDB'd at the start and end for isolation, so pick a
:: dedicated index (default 15) rather than one holding data you care about.
::============================================================================

set "RHOST=127.0.0.1"
set "RPORT=6379"
set "RPASS="
set "RDB=15"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--host"     ( set "RHOST=%~2" & shift & shift & goto parse )
if /i "%~1"=="--port"     ( set "RPORT=%~2" & shift & shift & goto parse )
if /i "%~1"=="--password" ( set "RPASS=%~2" & shift & shift & goto parse )
if /i "%~1"=="--db"       ( set "RDB=%~2"   & shift & shift & goto parse )
if /i "%~1"=="--help"     ( goto usage )
echo Unknown argument: %~1
goto usage
:parsed

:: --- dependency check -------------------------------------------------------
where redis-cli >nul 2>nul
if errorlevel 1 (
    echo.
    echo Missing dependency: 'redis-cli' command not found.
    echo Install redis-cli ^(redis-tools / Memurai / MSYS^) and ensure it is on PATH.
    echo.
    exit /b 2
)

:: --- base command -----------------------------------------------------------
set "RCLI=redis-cli -h %RHOST% -p %RPORT% -n %RDB%"
if not "%RPASS%"=="" set "RCLI=redis-cli -h %RHOST% -p %RPORT% -n %RDB% -a %RPASS% --no-auth-warning"

set "WORK=%TEMP%\pxredis_%RANDOM%%RANDOM%"
mkdir "%WORK%" 2>nul
set "FAILF=%WORK%\fails.txt"
type nul > "%FAILF%"
set /a PASS=0
set /a FAIL=0

echo Running RESP (redis-cli) tests against %RHOST%:%RPORT% (db %RDB%)
echo.

:: --- connection -------------------------------------------------------------
call :cap PING
call :eq "PING" "PONG"

call :cap ECHO hello
call :eq "ECHO" "hello"

:: --- clean slate ------------------------------------------------------------
call :cap FLUSHDB
call :eq "FLUSHDB (setup)" "OK"

:: --- strings ----------------------------------------------------------------
call :cap SET pxk hello
call :eq "SET" "OK"

call :cap GET pxk
call :eq "GET" "hello"

call :cap STRLEN pxk
call :eq "STRLEN" "5"

call :cap EXISTS pxk
call :eq "EXISTS" "1"

call :cap TYPE pxk
call :eq "TYPE" "string"

call :cap SETNX pxk2 v
call :eq "SETNX (new)" "1"

call :cap SETNX pxk2 v2
call :eq "SETNX (existing)" "0"

call :cap GETSET pxk world
call :eq "GETSET" "hello"

call :cap GET pxk
call :eq "GET (after GETSET)" "world"

call :cap MSET m1 a m2 b
call :eq "MSET" "OK"

call :cap MGET m1 m2
call :notempty "MGET"

:: --- counters (compare-and-swap INCR family) --------------------------------
call :cap INCR cnt
call :eq "INCR" "1"

call :cap INCRBY cnt 5
call :eq "INCRBY" "6"

call :cap DECR cnt
call :eq "DECR" "5"

call :cap DECRBY cnt 2
call :eq "DECRBY" "3"

:: --- keyspace ---------------------------------------------------------------
call :cap DBSIZE
call :notempty "DBSIZE"

call :cap KEYS *
call :notempty "KEYS"

call :cap SCAN 0
call :notempty "SCAN"

call :cap GETDEL m1
call :eq "GETDEL" "a"

call :cap DEL m2
call :eq "DEL" "1"

call :cap UNLINK pxk2
call :eq "UNLINK" "1"

call :cap EXISTS m2
call :eq "EXISTS (deleted)" "0"

:: --- teardown ---------------------------------------------------------------
call :cap FLUSHDB
call :eq "FLUSHDB (teardown)" "OK"

call :cap DBSIZE
call :eq "DBSIZE (empty)" "0"

goto summary

::============================================================================
:: Subroutines
::============================================================================

:cap
:: Runs redis-cli with all passed args and captures the last output line into OUT.
set "OUT="
for /f "delims=" %%a in ('%RCLI% %* 2^>nul') do set "OUT=%%a"
exit /b 0

:eq
:: %1 = test name, %2 = expected exact value (compared against captured OUT)
:: Result echoed at statement level so a ')' in a test name cannot close a block early.
set "STATUS=FAIL"
if "!OUT!"=="%~2" set "STATUS=PASS"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [expected '%~2', got '!OUT!']"
exit /b 0

:notempty
:: %1 = test name; passes when the captured OUT is non-empty
set "STATUS=PASS"
if "!OUT!"=="" set "STATUS=FAIL"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [empty reply]"
exit /b 0

:recordfail
>> "%FAILF%" echo %~1
exit /b 0

:summary
echo.
echo ============================================================
echo   Passed: !PASS!    Failed: !FAIL!
if !FAIL! GTR 0 (
    echo   Failed tests:
    type "%FAILF%"
)
echo ============================================================
echo.
rd /s /q "%WORK%" 2>nul
exit /b !FAIL!

:usage
echo Usage: RedisClientTest.bat [--host HOST] [--port PORT] [--password PW] [--db N]
echo   --host HOST     RESP host      ^(default 127.0.0.1^)
echo   --port PORT     RESP port      ^(default 6379^)
echo   --password PW   AUTH password  ^(default none^)
echo   --db N          Database index ^(default 15^)
exit /b 2
