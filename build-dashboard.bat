@echo off
if "%~1"=="" (
    echo Usage: build-dashboard.bat [version-tag]
    echo Example: build-dashboard.bat v0.1.0
    exit /b 1
)

set "VERSION_TAG=%~1"
set "DOCKER_BUILD_CLOUD_REF=jchristn77/jchristn77"
set "DOCKER_BUILD_CLOUD_BUILDER=cloud-jchristn77-jchristn77"

echo Building PepperX Dashboard %VERSION_TAG% with Docker Build Cloud builder %DOCKER_BUILD_CLOUD_BUILDER%...
docker buildx inspect "%DOCKER_BUILD_CLOUD_BUILDER%" >nul 2>&1
if errorlevel 1 (
    echo Connecting Docker Build Cloud builder %DOCKER_BUILD_CLOUD_REF%...
    docker buildx create --driver cloud "%DOCKER_BUILD_CLOUD_REF%" >nul
    if errorlevel 1 (
        echo Failed to connect Docker Build Cloud builder %DOCKER_BUILD_CLOUD_REF%.
        exit /b %errorlevel%
    )
)

rem The build context is the repository root, not docker/dashboard: the Dockerfile copies both
rem dashboard/ and docker/dashboard/nginx.conf.
docker buildx build ^
    --builder "%DOCKER_BUILD_CLOUD_BUILDER%" ^
    --platform linux/amd64,linux/arm64/v8 ^
    -t jchristn77/pepperx-dashboard:%VERSION_TAG% ^
    -t jchristn77/pepperx-dashboard:latest ^
    -f docker/dashboard/Dockerfile ^
    --push ^
    .
if errorlevel 1 (
    echo PepperX Dashboard build failed.
    exit /b %errorlevel%
)

rem The multi-arch build above pushes to Docker Hub but leaves nothing in the local image store:
rem a multi-platform manifest cannot be --load'ed, and a second --load build would invoke the build
rem cloud again. Pull the tags we just pushed instead -- that hydrates the local store for the current
rem platform without a second cloud build.
echo Updating local image store from registry...
docker pull jchristn77/pepperx-dashboard:%VERSION_TAG%
if errorlevel 1 (
    echo Failed to pull jchristn77/pepperx-dashboard:%VERSION_TAG% into the local store.
    exit /b %errorlevel%
)
docker pull jchristn77/pepperx-dashboard:latest
if errorlevel 1 (
    echo Failed to pull jchristn77/pepperx-dashboard:latest into the local store.
    exit /b %errorlevel%
)

echo Done.
exit /b 0
