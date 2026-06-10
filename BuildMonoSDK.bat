@echo off
rem BuildMonoSDK.bat
rem Build Mono runtime + libs from dotnet/runtime source, then copy artifacts
rem into this SDK repository's platform subdirectory (Win64).
rem
rem Usage:
rem   BuildMonoSDK.bat <dotnet-src-dir>
rem
rem Arguments:
rem   dotnet-src-dir   Path to the dotnet/runtime repository root.
rem                    Platform is always Win64 on Windows.
rem
rem Example:
rem   BuildMonoSDK.bat C:\Code\DotNet

setlocal enabledelayedexpansion

rem -- Arguments -------------------------------------------------------------------
if "%~1"=="" (
    echo Error: dotnet source directory is required. >&2
    echo Usage: %~nx0 ^<dotnet-src-dir^> >&2
    exit /b 1
)

set "DOTNET_SRC=%~f1"
set "SDK_DIR=%~dp0"
rem Remove trailing backslash from SDK_DIR
if "%SDK_DIR:~-1%"=="\" set "SDK_DIR=%SDK_DIR:~0,-1%"
set "BUILD_TYPE=Debug"
set "PLATFORM=Win64"

echo === BuildMonoSDK ===
echo   Source  : %DOTNET_SRC%
echo   Platform: %PLATFORM%
echo   SDK dir : %SDK_DIR%
echo.

rem -- Build -----------------------------------------------------------------------
cd /d "%DOTNET_SRC%"

echo ^>^>^> Building Mono + libs for Windows (x64)...
call build.cmd mono+libs
if errorlevel 1 (
    echo Error: build failed. >&2
    exit /b 1
)

echo.
echo ^>^>^> Build complete. Copying artifacts into SDK directory...

rem -- Copy artifacts --------------------------------------------------------------
rem SDK directory layout:
rem   MonoSDK\
rem     Win64\  include\  lib\  bin\  runtime\
rem     include\  (shared mono headers)

set "SRC_ARTIFACTS=%DOTNET_SRC%\artifacts"
set "MONO_TRIPLE=windows.x64.%BUILD_TYPE%"
set "RUNTIME_TFM=net10.0-windows-%BUILD_TYPE%-x64"
set "DEST=%SDK_DIR%\Win64"

if exist "%DEST%" rmdir /s /q "%DEST%"
mkdir "%DEST%"

xcopy /e /i /y "%SRC_ARTIFACTS%\obj\mono\%MONO_TRIPLE%\out\include\mono-2.0\" "%DEST%\include\"
xcopy /e /i /y "%SRC_ARTIFACTS%\obj\mono\%MONO_TRIPLE%\out\lib\"              "%DEST%\lib\"
xcopy /e /i /y "%SRC_ARTIFACTS%\obj\mono\%MONO_TRIPLE%\out\bin\"              "%DEST%\bin\"
xcopy /e /i /y "%SRC_ARTIFACTS%\bin\runtime\%RUNTIME_TFM%\"                   "%DEST%\runtime\"
xcopy /e /i /y "%SRC_ARTIFACTS%\bin\mono\%MONO_TRIPLE%\IL\"                   "%DEST%\runtime\"

rem Update shared include\ (mono headers are platform-independent)
if exist "%SDK_DIR%\include" rmdir /s /q "%SDK_DIR%\include"
xcopy /e /i /y "%SRC_ARTIFACTS%\obj\mono\%MONO_TRIPLE%\out\include\mono-2.0\" "%SDK_DIR%\include\"

rem -- Write VERSION.txt into the platform directory ------------------------------
for /f "delims=" %%b in ('git -C "%DOTNET_SRC%" rev-parse --abbrev-ref HEAD 2^>nul') do set "DOTNET_BRANCH=%%b"
for /f "delims=" %%c in ('git -C "%DOTNET_SRC%" rev-parse --short HEAD 2^>nul')      do set "DOTNET_COMMIT=%%c"
for /f "delims=" %%r in ('git -C "%DOTNET_SRC%" remote get-url origin 2^>nul')       do set "DOTNET_REMOTE=%%r"

(
    echo dotnet/runtime source
    echo   repo:   %DOTNET_REMOTE%
    echo   branch: %DOTNET_BRANCH%
    echo   commit: %DOTNET_COMMIT%
    echo.
    echo Platform: Windows ^(x64^)
    echo Build type: %BUILD_TYPE%
    echo Build command: build.cmd mono+libs
) > "%DEST%\VERSION.txt"

echo ^>^>^> Done. SDK updated at: %DEST%
endlocal
