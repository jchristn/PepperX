@echo off
setlocal enabledelayedexpansion
::============================================================================
:: McpClientTest.bat - exercises the PepperX MCP (Model Context Protocol) surface
:: over the Streamable HTTP transport via curl. MCP is JSON-RPC 2.0.
::
:: Usage:
::   McpClientTest.bat [--endpoint URL]
::
:: Arguments (all optional; system defaults are assumed when omitted):
::   --endpoint URL   MCP JSON-RPC endpoint. Default: http://localhost:8003/mcp/rpc
::
:: MCP is unauthenticated by design, so no credentials are required.
::============================================================================

set "ENDPOINT=http://localhost:8003/mcp/rpc"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--endpoint" ( set "ENDPOINT=%~2" & shift & shift & goto parse )
if /i "%~1"=="--help"     ( goto usage )
echo Unknown argument: %~1
goto usage
:parsed

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
set "WORK=%TEMP%\pxmcp_%RANDOM%%RANDOM%"
mkdir "%WORK%" 2>nul
set "BODYF=%WORK%\body.out"
set "CODEF=%WORK%\code.out"
set "JSONF=%WORK%\req.json"
set "FAILF=%WORK%\fails.txt"
type nul > "%FAILF%"

set "CT=mcptest%RANDOM%%RANDOM%"
set "KEY=notes/hello.txt"
set /a PASS=0
set /a FAIL=0

echo Running MCP client tests against %ENDPOINT%
echo Test container: %CT%
echo.

:: --- handshake --------------------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"mcp-test","version":"1.0"}}}
call :call "initialize"
call :bodyhas "initialize advertises protocolVersion" "protocolVersion"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/list"}
call :call "tools/list"
call :bodyhas "tools/list includes pepperx_container_create" "pepperx_container_create"

:: --- Voltaic diagnostics ----------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping","arguments":{}}}
call :call "tools/call ping"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"message":"hi"}}}
call :call "tools/call echo"

:: --- containers -------------------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_create","arguments":{"name":"%CT%","tags":{"team":"platform"}}}}
call :call "pepperx_container_create"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_read","arguments":{"container":"%CT%"}}}
call :call "pepperx_container_read"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_list","arguments":{"maxResults":10}}}
call :call "pepperx_container_list"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_enumerate","arguments":{"maxResults":10}}}
call :call "pepperx_container_enumerate"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_update_tags","arguments":{"container":"%CT%","tags":{"env":"staging"}}}}
call :call "pepperx_container_update_tags"

:: --- objects ----------------------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_write","arguments":{"container":"%CT%","key":"%KEY%","dataBase64":"aGVsbG8gZnJvbSBNQ1A=","contentType":"text/plain","labels":["note"],"tags":{"source":"mcp"}}}}
call :call "pepperx_object_write"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_exists","arguments":{"container":"%CT%","key":"%KEY%"}}}
call :call "pepperx_object_exists"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_read_metadata","arguments":{"container":"%CT%","key":"%KEY%"}}}
call :call "pepperx_object_read_metadata"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_read","arguments":{"container":"%CT%","key":"%KEY%"}}}
call :call "pepperx_object_read"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_update_metadata","arguments":{"container":"%CT%","key":"%KEY%","labels":["note","reviewed"],"tags":{"source":"mcp"}}}}
call :call "pepperx_object_update_metadata"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_enumerate","arguments":{"container":"%CT%","maxResults":10}}}
call :call "pepperx_object_enumerate"

:: --- search and admin -------------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_search","arguments":{"maxResults":10}}}
call :call "pepperx_search"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_stats","arguments":{}}}
call :call "pepperx_stats"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_nodes","arguments":{}}}
call :call "pepperx_nodes"

:: --- negative: tool error is surfaced as isError, not a transport fault ------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_read","arguments":{"container":"no-such-container-zzz"}}}
call :callerr "pepperx_container_read (missing) is a tool error"

:: --- cleanup ----------------------------------------------------------------
> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_object_delete","arguments":{"container":"%CT%","key":"%KEY%"}}}
call :call "pepperx_object_delete"

> "%JSONF%" echo {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pepperx_container_delete","arguments":{"container":"%CT%","force":true}}}
call :call "pepperx_container_delete"

goto summary

::============================================================================
:: Subroutines
::============================================================================

:call
:: %1 = test name. Expects JSONF written. Passes when HTTP 200 and the JSON-RPC
:: reply carries a "result" and is NOT a tool error ("isError":true).
curl -s -o "%BODYF%" -w "%%{http_code}" -X POST -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary "@%JSONF%" "%ENDPOINT%" > "%CODEF%" 2>nul
set /p CODE=<"%CODEF%"
set "HASRESULT=0"
set "HASERR=0"
findstr /C:"\"result\"" "%BODYF%" >nul 2>nul && set "HASRESULT=1"
findstr /C:"\"isError\":true" "%BODYF%" >nul 2>nul && set "HASERR=1"
set "STATUS=FAIL"
if "!CODE!"=="200" if "!HASRESULT!"=="1" if "!HASERR!"=="0" set "STATUS=PASS"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [http !CODE!, result=!HASRESULT!, isError=!HASERR!]"
exit /b 0

:callerr
:: %1 = test name. Passes when the reply is a well-formed tool error
:: (HTTP 200 with "isError":true) -- proving tool failures are surfaced as MCP tool
:: errors rather than transport faults.
curl -s -o "%BODYF%" -w "%%{http_code}" -X POST -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" --data-binary "@%JSONF%" "%ENDPOINT%" > "%CODEF%" 2>nul
set /p CODE=<"%CODEF%"
set "HASERR=0"
findstr /C:"\"isError\":true" "%BODYF%" >nul 2>nul && set "HASERR=1"
set "STATUS=FAIL"
if "!CODE!"=="200" if "!HASERR!"=="1" set "STATUS=PASS"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [http !CODE!, isError=!HASERR!]"
exit /b 0

:bodyhas
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
echo Usage: McpClientTest.bat [--endpoint URL]
echo   --endpoint URL   MCP JSON-RPC endpoint ^(default http://localhost:8003/mcp/rpc^)
exit /b 2
