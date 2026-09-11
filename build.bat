@echo off
setlocal

:: Set script directory to project root
set "ROOT_DIR=%~dp0"
set "SLN_PATH=%ROOT_DIR%InventorValidator.sln"
set "PROJECT_PATH=%ROOT_DIR%src\InventorValidator\InventorValidator.csproj"
set "PUBLISH_DIR=%ROOT_DIR%publish"
set "PUBLISH_EXE=%PUBLISH_DIR%\InventorValidator.exe"

:: Verify dotnet is available
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK 'dotnet' was not found in your PATH.
    echo Please install the .NET 8.0 SDK from https://dotnet.microsoft.com/download
    exit /b 1
)

:: Parse command-line arguments
set "ACTION=%~1"
set "PARAM=%~2"

if "%ACTION%"=="" goto :Menu

if /i "%ACTION%"=="run" (
    if /i "%PARAM%"=="release" goto :DoRunRelease
    goto :DoRunDebug
)
if /i "%ACTION%"=="run-release" goto :DoRunRelease
if /i "%ACTION%"=="run-debug" goto :DoRunDebug

if /i "%ACTION%"=="build" (
    if /i "%PARAM%"=="release" goto :DoBuildRelease
    goto :DoBuildDebug
)
if /i "%ACTION%"=="build-debug" goto :DoBuildDebug
if /i "%ACTION%"=="build-release" goto :DoBuildRelease

if /i "%ACTION%"=="release" goto :DoRelease
if /i "%ACTION%"=="publish" goto :DoRelease

if /i "%ACTION%"=="test" (
    if /i "%PARAM%"=="all" goto :DoTestAll
    goto :DoTestUnit
)
if /i "%ACTION%"=="test-unit" goto :DoTestUnit
if /i "%ACTION%"=="test-all" goto :DoTestAll
if /i "%ACTION%"=="tests" goto :DoTestUnit

if /i "%ACTION%"=="clean" goto :DoClean

if /i "%ACTION%"=="help" goto :DoHelp
if /i "%ACTION%"=="-h" goto :DoHelp
if /i "%ACTION%"=="--help" goto :DoHelp
if /i "%ACTION%"=="/?" goto :DoHelp

echo [ERROR] Unknown command: "%ACTION%"
echo.
goto :DoHelp

:: ============================================================
:: Actions (CLI Mode - Direct execution, exits with code)
:: ============================================================

:DoRunDebug
echo [INFO] Starting InventorValidator (Debug)...
dotnet run --project "%PROJECT_PATH%" -c Debug
exit /b %ERRORLEVEL%

:DoRunRelease
if exist "%PUBLISH_EXE%" (
    echo [INFO] Launching published executable: "%PUBLISH_EXE%"
    start "" "%PUBLISH_EXE%"
    exit /b 0
) else (
    echo [INFO] Published executable not found at "%PUBLISH_EXE%".
    echo [INFO] Running Release build via dotnet run...
    dotnet run --project "%PROJECT_PATH%" -c Release
    exit /b %ERRORLEVEL%
)

:DoBuildDebug
echo [INFO] Building solution (Debug)...
dotnet build "%SLN_PATH%" -c Debug
exit /b %ERRORLEVEL%

:DoBuildRelease
echo [INFO] Building solution (Release)...
dotnet build "%SLN_PATH%" -c Release
exit /b %ERRORLEVEL%

:DoRelease
echo [INFO] Publishing single-file release executable to "%PUBLISH_DIR%"...
dotnet publish "%PROJECT_PATH%" -c Release -o "%PUBLISH_DIR%"
set "PUB_EXIT=%ERRORLEVEL%"
if %PUB_EXIT% equ 0 (
    echo.
    echo ============================================================
    echo [SUCCESS] Single-file release build ready:
    echo %PUBLISH_EXE%
    echo ============================================================
) else (
    echo.
    echo [ERROR] Publish failed with exit code %PUB_EXIT%.
)
exit /b %PUB_EXIT%

:DoTestUnit
echo [INFO] Running unit tests [excluding COM integration tests]...
dotnet test "%SLN_PATH%" --filter "FullyQualifiedName!~Integration"
exit /b %ERRORLEVEL%

:DoTestAll
echo [INFO] Running all tests [including Inventor COM integration tests]...
dotnet test "%SLN_PATH%"
exit /b %ERRORLEVEL%

:DoClean
echo [INFO] Cleaning solution...
dotnet clean "%SLN_PATH%"
if exist "%PUBLISH_DIR%" (
    echo [INFO] Removing publish directory "%PUBLISH_DIR%"...
    rmdir /s /q "%PUBLISH_DIR%" 2>nul
)
echo [SUCCESS] Clean completed.
exit /b 0

:DoHelp
echo ============================================================
echo   InventorValidator Project Management Script
echo ============================================================
echo   Usage:
echo     build.bat                 Interactive menu
echo     build.bat run             Run app (Debug)
echo     build.bat run-release     Run published app or Release
echo     build.bat build           Build solution (Debug)
echo     build.bat build-release   Build solution (Release)
echo     build.bat release         Publish single-file exe to .\publish\
echo     build.bat publish         Alias for release
echo     build.bat test            Run unit tests (fast)
echo     build.bat test-all        Run all tests (incl. COM)
echo     build.bat clean           Clean bin, obj, and publish folders
echo     build.bat help            Show this help message
echo ============================================================
exit /b 0

:: ============================================================
:: Interactive Menu Mode
:: ============================================================

:Menu
cls
echo ============================================================
echo           InventorValidator - Build ^& Run Manager
echo ============================================================
echo   [1] Run Application (Debug)
echo   [2] Run Application (Release)
echo   [3] Build Solution (Debug)
echo   [4] Build Solution (Release)
echo   [5] Publish Release (Single-File Exe -^> publish\)
echo   [6] Run Unit Tests (Fast, 37 tests)
echo   [7] Run All Tests (Including Inventor COM)
echo   [8] Clean Build Artifacts
echo   [0] Exit
echo ============================================================
set "CHOICE="
set /p "CHOICE=Enter choice [0-8] (default: 1): "
if "%CHOICE%"=="" set "CHOICE=1"

if "%CHOICE%"=="1" goto :MenuRun
if "%CHOICE%"=="2" goto :MenuRunRelease
if "%CHOICE%"=="3" goto :MenuBuildDebug
if "%CHOICE%"=="4" goto :MenuBuildRelease
if "%CHOICE%"=="5" goto :MenuRelease
if "%CHOICE%"=="6" goto :MenuTestUnit
if "%CHOICE%"=="7" goto :MenuTestAll
if "%CHOICE%"=="8" goto :MenuClean
if "%CHOICE%"=="0" goto :Exit

echo.
echo [WARN] Invalid option "%CHOICE%". Please select 0-8.
timeout /t 2 >nul
goto :Menu

:MenuRun
echo.
echo [INFO] Starting InventorValidator (Debug)...
dotnet run --project "%PROJECT_PATH%" -c Debug
goto :MenuPause

:MenuRunRelease
echo.
if exist "%PUBLISH_EXE%" (
    echo [INFO] Launching published executable: "%PUBLISH_EXE%"
    start "" "%PUBLISH_EXE%"
) else (
    echo [INFO] Published executable not found at "%PUBLISH_EXE%".
    echo [INFO] Running Release build via dotnet run...
    dotnet run --project "%PROJECT_PATH%" -c Release
)
goto :MenuPause

:MenuBuildDebug
echo.
echo [INFO] Building solution (Debug)...
dotnet build "%SLN_PATH%" -c Debug
goto :MenuPause

:MenuBuildRelease
echo.
echo [INFO] Building solution (Release)...
dotnet build "%SLN_PATH%" -c Release
goto :MenuPause

:MenuRelease
echo.
echo [INFO] Publishing single-file release executable to "%PUBLISH_DIR%"...
dotnet publish "%PROJECT_PATH%" -c Release -o "%PUBLISH_DIR%"
if %ERRORLEVEL% equ 0 (
    echo.
    echo ============================================================
    echo [SUCCESS] Single-file release build ready:
    echo %PUBLISH_EXE%
    echo ============================================================
) else (
    echo.
    echo [ERROR] Publish failed with exit code %ERRORLEVEL%.
)
goto :MenuPause

:MenuTestUnit
echo.
echo [INFO] Running unit tests [excluding COM integration tests]...
dotnet test "%SLN_PATH%" --filter "FullyQualifiedName!~Integration"
goto :MenuPause

:MenuTestAll
echo.
echo [INFO] Running all tests [including Inventor COM integration tests]...
dotnet test "%SLN_PATH%"
goto :MenuPause

:MenuClean
echo.
echo [INFO] Cleaning solution...
dotnet clean "%SLN_PATH%"
if exist "%PUBLISH_DIR%" (
    echo [INFO] Removing publish directory "%PUBLISH_DIR%"...
    rmdir /s /q "%PUBLISH_DIR%" 2>nul
)
echo [SUCCESS] Clean completed.
goto :MenuPause

:MenuPause
echo.
echo Press any key to return to menu...
pause >nul
goto :Menu

:Exit
exit /b 0
