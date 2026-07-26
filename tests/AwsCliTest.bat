@echo off
setlocal enabledelayedexpansion
::============================================================================
:: AwsCliTest.bat - exercises the PepperX S3-compatible surface via the AWS CLI.
::
:: Usage:
::   AwsCliTest.bat [--endpoint URL] [--access-key KEY] [--secret-key KEY]
::                  [--region REGION]
::
:: Arguments (all optional; system defaults are assumed when omitted):
::   --endpoint URL    S3 endpoint.        Default: http://localhost:8001
::   --access-key KEY  S3 access key.      Default: pepperx
::   --secret-key KEY  S3 secret key.      Default: pepperx
::   --region REGION   S3 region.          Default: us-west-1
::============================================================================

set "ENDPOINT=http://localhost:8001"
set "ACCESSKEY=pepperx"
set "SECRETKEY=pepperx"
set "REGION=us-west-1"

:parse
if "%~1"=="" goto parsed
if /i "%~1"=="--endpoint"   ( set "ENDPOINT=%~2"  & shift & shift & goto parse )
if /i "%~1"=="--access-key" ( set "ACCESSKEY=%~2" & shift & shift & goto parse )
if /i "%~1"=="--secret-key" ( set "SECRETKEY=%~2" & shift & shift & goto parse )
if /i "%~1"=="--region"     ( set "REGION=%~2"    & shift & shift & goto parse )
if /i "%~1"=="--help"       ( goto usage )
echo Unknown argument: %~1
goto usage
:parsed

:: --- dependency check -------------------------------------------------------
where aws >nul 2>nul
if errorlevel 1 (
    echo.
    echo Missing dependency: 'aws' command not found.
    echo Install the AWS CLI ^(v2^) and ensure it is on PATH.
    echo.
    exit /b 2
)

:: --- credentials / behavior via environment ---------------------------------
set "AWS_ACCESS_KEY_ID=%ACCESSKEY%"
set "AWS_SECRET_ACCESS_KEY=%SECRETKEY%"
set "AWS_DEFAULT_REGION=%REGION%"
set "AWS_EC2_METADATA_DISABLED=true"
:: AWS CLI v2.23+ adds default integrity checksums that non-AWS servers may reject.
set "AWS_REQUEST_CHECKSUM_CALCULATION=when_required"
set "AWS_RESPONSE_CHECKSUM_VALIDATION=when_required"

set "AWS=aws --endpoint-url %ENDPOINT% --no-cli-pager --output json"

:: --- workspace --------------------------------------------------------------
set "WORK=%TEMP%\pxaws_%RANDOM%%RANDOM%"
mkdir "%WORK%" 2>nul
set "PAYLOADF=%WORK%\payload.txt"
set "OUTF=%WORK%\download.txt"
set "FAILF=%WORK%\fails.txt"
type nul > "%FAILF%"
> "%PAYLOADF%" echo hello pepperx over s3

:: bucket names must be 3-63 lowercase alphanumeric or hyphen
set "BUCKET=awstest%RANDOM%%RANDOM%"
set /a PASS=0
set /a FAIL=0

echo Running AWS CLI S3 tests against %ENDPOINT%  (region %REGION%)
echo Test bucket: %BUCKET%
echo.

%AWS% s3api list-buckets >nul 2>&1
call :res "ListBuckets" %errorlevel%

%AWS% s3api create-bucket --bucket %BUCKET% >nul 2>&1
call :res "CreateBucket" %errorlevel%

%AWS% s3api head-bucket --bucket %BUCKET% >nul 2>&1
call :res "HeadBucket" %errorlevel%

%AWS% s3api get-bucket-location --bucket %BUCKET% >nul 2>&1
call :res "GetBucketLocation" %errorlevel%

%AWS% s3api put-object --bucket %BUCKET% --key hello.txt --body "%PAYLOADF%" >nul 2>&1
call :res "PutObject" %errorlevel%

%AWS% s3api put-object --bucket %BUCKET% --key second.txt --body "%PAYLOADF%" >nul 2>&1
call :res "PutObject (second)" %errorlevel%

%AWS% s3api head-object --bucket %BUCKET% --key hello.txt >nul 2>&1
call :res "HeadObject" %errorlevel%

del "%OUTF%" 2>nul
%AWS% s3api get-object --bucket %BUCKET% --key hello.txt "%OUTF%" >nul 2>&1
call :res "GetObject" %errorlevel%

%AWS% s3api list-objects-v2 --bucket %BUCKET% >nul 2>&1
call :res "ListObjectsV2" %errorlevel%

%AWS% s3api list-objects --bucket %BUCKET% >nul 2>&1
call :res "ListObjects" %errorlevel%

%AWS% s3api put-bucket-tagging --bucket %BUCKET% --tagging "TagSet=[{Key=team,Value=platform}]" >nul 2>&1
call :res "PutBucketTagging" %errorlevel%

%AWS% s3api get-bucket-tagging --bucket %BUCKET% >nul 2>&1
call :res "GetBucketTagging" %errorlevel%

%AWS% s3api delete-bucket-tagging --bucket %BUCKET% >nul 2>&1
call :res "DeleteBucketTagging" %errorlevel%

%AWS% s3api put-object-tagging --bucket %BUCKET% --key hello.txt --tagging "TagSet=[{Key=env,Value=prod}]" >nul 2>&1
call :res "PutObjectTagging" %errorlevel%

%AWS% s3api get-object-tagging --bucket %BUCKET% --key hello.txt >nul 2>&1
call :res "GetObjectTagging" %errorlevel%

%AWS% s3api delete-object-tagging --bucket %BUCKET% --key hello.txt >nul 2>&1
call :res "DeleteObjectTagging" %errorlevel%

%AWS% s3api delete-object --bucket %BUCKET% --key hello.txt >nul 2>&1
call :res "DeleteObject" %errorlevel%

%AWS% s3api delete-objects --bucket %BUCKET% --delete "Objects=[{Key=second.txt}]" >nul 2>&1
call :res "DeleteObjects (batch)" %errorlevel%

%AWS% s3api delete-bucket --bucket %BUCKET% >nul 2>&1
call :res "DeleteBucket" %errorlevel%

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
if "!STATUS!"=="FAIL" call :recordfail "  - %~1 [aws exit code %~2]"
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
echo Usage: AwsCliTest.bat [--endpoint URL] [--access-key KEY] [--secret-key KEY] [--region REGION]
echo   --endpoint URL    S3 endpoint   ^(default http://localhost:8001^)
echo   --access-key KEY  S3 access key ^(default pepperx^)
echo   --secret-key KEY  S3 secret key ^(default pepperx^)
echo   --region REGION   S3 region     ^(default us-west-1^)
exit /b 2
