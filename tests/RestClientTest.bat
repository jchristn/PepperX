@echo off
setlocal enabledelayedexpansion
::============================================================================
:: RestClientTest.bat - exercises the PepperX native REST API surface via curl.
::
:: Usage:
::   RestClientTest.bat [--endpoint URL]
::
:: Arguments (all optional; system defaults are assumed when omitted):
::   --endpoint URL   Base REST endpoint. Default: http://localhost:8000
::
:: PepperX REST is unauthenticated by design, so no credentials are required.
::============================================================================

set "ENDPOINT=http://localhost:8000"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--endpoint" ( set "ENDPOINT=%~2" & shift & shift & goto parse )
if /i "%~1"=="--help"     ( goto usage )
echo Unknown argument: %~1
goto usage
:parsed

:: strip a trailing slash from the endpoint
if "%ENDPOINT:~-1%"=="/" set "ENDPOINT=%ENDPOINT:~0,-1%"

:: --- dependency check -------------------------------------------------------
where curl >nul 2>nul
if errorlevel 1 (
    echo.
    echo Missing dependency: 'curl' command not found.
    echo Install curl ^(bundled with Windows 10/11^) and ensure it is on PATH.
    echo.
    exit /b 2
)

:: --- workspace --------------------------------------------------------------
set "WORK=%TEMP%\pxrest_%RANDOM%%RANDOM%"
mkdir "%WORK%" 2>nul
set "BODYF=%WORK%\body.out"
set "CODEF=%WORK%\code.out"
set "JSONF=%WORK%\req.json"
set "PAYLOADF=%WORK%\payload.bin"
set "FAILF=%WORK%\fails.txt"
type nul > "%FAILF%"

set "CT=resttest%RANDOM%%RANDOM%"
set /a PASS=0
set /a FAIL=0

echo Running REST client tests against %ENDPOINT%
echo Test container: %CT%
echo.

:: --- health / discovery -----------------------------------------------------
set "EXTRA="
call :req "GET / (server info)" "/" 200
call :contains "GET / body contains PepperX" "PepperX"

set "EXTRA="
call :req "GET /v1.0/api/health" "/v1.0/api/health" 200

:: --- container lifecycle ----------------------------------------------------
> "%JSONF%" echo {"Name":"%CT%","Tags":{"team":"platform"}}
set "EXTRA=-X PUT -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "PUT /v1.0/containers (create)" "/v1.0/containers" 201

set "EXTRA="
call :req "GET /v1.0/containers/{c}" "/v1.0/containers/%CT%" 200

set "EXTRA=--head"
call :req "HEAD /v1.0/containers/{c}" "/v1.0/containers/%CT%" 200

set "EXTRA="
call :req "GET /v1.0/containers (list)" "/v1.0/containers" 200

> "%JSONF%" echo {"MaxResults":10}
set "EXTRA=-X POST -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "POST /v1.0/containers/enumerate" "/v1.0/containers/enumerate" 200

> "%JSONF%" echo {"team":"platform","env":"staging"}
set "EXTRA=-X PUT -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "PUT /v1.0/containers/{c}/tags" "/v1.0/containers/%CT%/tags" 200

:: --- per-container caching ---------------------------------------------------
set "EXTRA="
call :req "GET /v1.0/containers/{c}/cache" "/v1.0/containers/%CT%/cache" 200

> "%JSONF%" echo {"Enabled":true,"Policy":"LRU","MaxObjects":500,"MaxMemoryBytes":0,"EvictCount":5,"MaxCacheableObjectBytes":1048576}
set "EXTRA=-X PUT -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "PUT /v1.0/containers/{c}/cache" "/v1.0/containers/%CT%/cache" 200

:: --- object lifecycle -------------------------------------------------------
> "%PAYLOADF%" echo hello pepperx
set "EXTRA=-X PUT --data-binary "@%PAYLOADF%""
call :req "PUT object (raw body)" "/v1.0/containers/%CT%/object?key=hello.txt" 201

set "EXTRA="
call :req "GET object" "/v1.0/containers/%CT%/object?key=hello.txt" 200
call :contains "GET object body matches" "hello pepperx"

set "EXTRA=--head"
call :req "HEAD object" "/v1.0/containers/%CT%/object?key=hello.txt" 200

set "EXTRA="
call :req "GET object metadata" "/v1.0/containers/%CT%/object/metadata?key=hello.txt" 200

> "%JSONF%" echo {"Labels":["metric","cpu"],"Tags":{"reviewed":"true"},"ClearObject":false}
set "EXTRA=-X PUT -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "PUT object metadata (update)" "/v1.0/containers/%CT%/object/metadata?key=hello.txt" 200

> "%JSONF%" echo {"ContentType":"text/plain","DataBase64":"aGVsbG8="}
set "EXTRA=-X POST -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "POST object (JSON envelope)" "/v1.0/containers/%CT%/object?key=json1" 201

set "EXTRA="
call :req "GET objects (list)" "/v1.0/containers/%CT%/objects?maxResults=10" 200

> "%JSONF%" echo {"MaxResults":10}
set "EXTRA=-X POST -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "POST search in container" "/v1.0/containers/%CT%/objects/enumerate" 200

> "%JSONF%" echo {"MaxResults":10}
set "EXTRA=-X POST -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "POST search all containers" "/v1.0/objects/enumerate" 200

:: --- admin (read-only + safe rehydrate) -------------------------------------
set "EXTRA="
call :req "GET /v1.0/admin/stats" "/v1.0/admin/stats" 200
set "EXTRA="
call :req "GET /v1.0/admin/nodes" "/v1.0/admin/nodes" 200
set "EXTRA="
call :req "GET /v1.0/admin/settings" "/v1.0/admin/settings" 200

> "%JSONF%" echo {"Mode":"Verify"}
set "EXTRA=-X POST -H "Content-Type: application/json" --data-binary "@%JSONF%""
call :req "POST /v1.0/admin/rehydrate (Verify)" "/v1.0/admin/rehydrate" 200

:: NOTE: POST /v1.0/admin/restart is intentionally NOT exercised here; it would
:: terminate the node under test.

:: --- request history --------------------------------------------------------
set "EXTRA="
call :req "GET /v1.0/api/request-history" "/v1.0/api/request-history" 200
set "EXTRA="
call :req "GET /v1.0/api/request-history/summary" "/v1.0/api/request-history/summary" 200

:: --- negative path ----------------------------------------------------------
set "EXTRA="
call :req "GET missing container is 404" "/v1.0/containers/no-such-container-zzz" 404

:: --- cleanup / delete surface -----------------------------------------------
set "EXTRA=-X DELETE"
call :req "DELETE object" "/v1.0/containers/%CT%/object?key=hello.txt" 204
set "EXTRA=-X DELETE"
call :req "DELETE object (json1)" "/v1.0/containers/%CT%/object?key=json1" 204
set "EXTRA=-X DELETE"
call :req "DELETE container (force)" "/v1.0/containers/%CT%?force=true" 204

goto summary

::============================================================================
:: Subroutines
::============================================================================

:req
:: %1 = test name, %2 = path, %3 = expected HTTP status
:: NOTE: test names may contain parentheses, so the result is never echoed inside a
:: ( ) block -- an expanded ')' would close the block early. Echo at statement level.
curl -s -o "%BODYF%" -w "%%{http_code}" %EXTRA% "%ENDPOINT%%~2" > "%CODEF%" 2>nul
set /p CODE=<"%CODEF%"
set "STATUS=FAIL"
if "!CODE!"=="%~3" set "STATUS=PASS"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [expected %~3, got !CODE!]"
set "EXTRA="
exit /b 0

:contains
:: %1 = test name, %2 = substring expected in the last response body
findstr /C:"%~2" "%BODYF%" >nul 2>nul
set "STATUS=PASS"
if errorlevel 1 set "STATUS=FAIL"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [substring not found: %~2]"
exit /b 0

:recordfail
:: %1 = fully-formed failure line (quoted). Written at statement level so a ')' in the
:: test name is harmless.
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
echo Usage: RestClientTest.bat [--endpoint URL]
echo   --endpoint URL   Base REST endpoint ^(default http://localhost:8000^)
exit /b 2
