@echo off
setlocal
rem Build di Mute Me. Individua MSVC e Windows SDK via vswhere, cosi' funziona
rem sia in locale sia sui runner windows-latest di GitHub Actions.

for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSPATH=%%i
if "%VSPATH%"=="" (echo Visual Studio Build Tools non trovati & exit /b 1)

call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if errorlevel 1 (echo vcvars64 fallito & exit /b 1)

cd /d "%~dp0"
if not exist build mkdir build

rem "build.bat debug" produce simboli invece di ottimizzare per dimensione.
set CFLAGS=/O1 /Os /GS-
set LDFLAGS=/OPT:REF /OPT:ICF
if /i "%~1"=="debug" (
    set CFLAGS=/Od /Zi
    set LDFLAGS=/DEBUG
)

rc.exe /nologo /fo build\muteme.res muteme.rc || exit /b 1

rem /MT collega il CRT staticamente: l'exe non dipende da nessun redistributable.
cl.exe /nologo %CFLAGS% /MT /W4 /DUNICODE /D_UNICODE /Fo:build\ /Fd:build\ ^
    muteme.c build\muteme.res ^
    /link /SUBSYSTEM:WINDOWS /OUT:build\MuteMe.exe %LDFLAGS% ^
    kernel32.lib user32.lib gdi32.lib shell32.lib ole32.lib advapi32.lib dwmapi.lib winmm.lib || exit /b 1

echo.
for %%f in (build\MuteMe.exe) do echo MuteMe.exe: %%~zf byte
