@echo off
setlocal enabledelayedexpansion
::============================================================================
:: MinioClientTest.bat - exercises the PepperX S3 surface via the MinIO client (mc).
::
:: Usage:
::   MinioClientTest.bat [--endpoint URL] [--access-key KEY] [--secret-key KEY]
::
:: Arguments (all optional; system defaults are assumed when omitted):
::   --endpoint URL    S3 endpoint.        Default: http://localhost:8001
::   --access-key KEY  S3 access key.      Default: pepperx
::   --secret-key KEY  S3 secret key.      Default: pepperx
::============================================================================

set "ENDPOINT=http://localhost:8001"
set "ACCESSKEY=pepperx"
:: mc requires a secret of at least 8 characters, and PepperX accepts S3 signatures
:: without verifying them (the static keys are a client affordance, not a credential
:: check), so any >=8-char secret works against a default node. The node's actual
:: default secret is "pepperx" (7 chars) which mc refuses; override with --secret-key
:: if your node genuinely verifies credentials.
set "SECRETKEY=pepperxsecret"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--endpoint"   ( set "ENDPOINT=%~2"  & shift & shift & goto parse )
if /i "%~1"=="--access-key" ( set "ACCESSKEY=%~2" & shift & shift & goto parse )
if /i "%~1"=="--secret-key" ( set "SECRETKEY=%~2" & shift & shift & goto parse )
if /i "%~1"=="--help"       ( goto usage )
echo Unknown argument: %~1
goto usage
:parsed

:: --- dependency check -------------------------------------------------------
where mc >nul 2>nul
if errorlevel 1 (
    echo.
    echo Missing dependency: 'mc' command not found.
    echo Install the MinIO Client ^(mc^) and ensure it is on PATH.
    echo   https://min.io/docs/minio/linux/reference/minio-mc.html
    echo.
    exit /b 2
)

:: --- workspace --------------------------------------------------------------
set "WORK=%TEMP%\pxmc_%RANDOM%%RANDOM%"
mkdir "%WORK%" 2>nul
set "PAYLOADF=%WORK%\payload.txt"
set "OUTF=%WORK%\download.txt"
set "FAILF=%WORK%\fails.txt"
type nul > "%FAILF%"
> "%PAYLOADF%" echo hello pepperx over mc

set "ALIAS=pxtest%RANDOM%"
set "BUCKET=mctest%RANDOM%%RANDOM%"
set /a PASS=0
set /a FAIL=0

echo Running MinIO client (mc) tests against %ENDPOINT%
echo Test bucket: %BUCKET%   alias: %ALIAS%
echo.

:: --- connect (alias) --------------------------------------------------------
:: Note: the MinIO client enforces its own credential rules -- the secret key must
:: be at least 8 characters. PepperX's default S3 secret ("pepperx") is 7, so mc
:: rejects it client-side; pass a compliant --secret-key (and configure the node to
:: match) when testing with mc.
mc alias set %ALIAS% %ENDPOINT% %ACCESSKEY% %SECRETKEY% --api S3v4 > "%WORK%\alias.out" 2>&1
if errorlevel 1 (
    echo.
    echo Could not configure mc alias for %ENDPOINT%. mc reported:
    type "%WORK%\alias.out"
    echo.
    echo If the message mentions the secret key, note that mc requires a secret of at
    echo least 8 characters; re-run with --access-key/--secret-key values that satisfy
    echo mc and that the target node accepts.
    echo.
    rd /s /q "%WORK%" 2>nul
    exit /b 3
)

mc mb %ALIAS%/%BUCKET% >nul 2>&1
call :res "mb (make bucket)" %errorlevel%

mc ls %ALIAS% >nul 2>&1
call :res "ls (list buckets)" %errorlevel%

mc cp "%PAYLOADF%" %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "cp (put object)" %errorlevel%

mc ls %ALIAS%/%BUCKET% >nul 2>&1
call :res "ls (list objects)" %errorlevel%

mc stat %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "stat (head object)" %errorlevel%

del "%OUTF%" 2>nul
mc cp %ALIAS%/%BUCKET%/hello.txt "%OUTF%" >nul 2>&1
call :res "cp (get object)" %errorlevel%

mc cat %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "cat (read object)" %errorlevel%

mc tag set %ALIAS%/%BUCKET% "team=platform" >nul 2>&1
call :res "tag set (bucket)" %errorlevel%

mc tag list %ALIAS%/%BUCKET% >nul 2>&1
call :res "tag list (bucket)" %errorlevel%

mc tag remove %ALIAS%/%BUCKET% >nul 2>&1
call :res "tag remove (bucket)" %errorlevel%

mc tag set %ALIAS%/%BUCKET%/hello.txt "env=prod" >nul 2>&1
call :res "tag set (object)" %errorlevel%

mc tag list %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "tag list (object)" %errorlevel%

mc tag remove %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "tag remove (object)" %errorlevel%

mc rm %ALIAS%/%BUCKET%/hello.txt >nul 2>&1
call :res "rm (delete object)" %errorlevel%

mc rb --force %ALIAS%/%BUCKET% >nul 2>&1
call :res "rb (remove bucket)" %errorlevel%

:: cleanup alias (not counted)
mc alias remove %ALIAS% >nul 2>&1

goto summary

::============================================================================
:: Subroutines
::============================================================================

:res
:: %1 = test name, %2 = errorlevel (0 = success)
:: Result echoed at statement level so a ')' in a test name cannot close a block early.
set "STATUS=FAIL"
if "%~2"=="0" set "STATUS=PASS"
if "!STATUS!"=="PASS" set /a PASS+=1
if "!STATUS!"=="FAIL" set /a FAIL+=1
echo [!STATUS!] %~1
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [mc exit code %~2]"
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
echo Usage: MinioClientTest.bat [--endpoint URL] [--access-key KEY] [--secret-key KEY]
echo   --endpoint URL    S3 endpoint   ^(default http://localhost:8001^)
echo   --access-key KEY  S3 access key ^(default pepperx^)
echo   --secret-key KEY  S3 secret key ^(default pepperx^)
exit /b 2
