@echo off
rem Builds the engine with the Visual Studio Build Tools (MSVC, CMake and Ninja from the Build Tools).
rem Usage: build.cmd [Release|Debug]
setlocal
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release
set HERE=%~dp0

set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
if not defined VS (
    echo No Visual Studio installation with the C++ tools found.
    exit /b 1
)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

if not exist "%HERE%third_party\JUCE\CMakeLists.txt" (
    echo JUCE is missing: see third_party\README.md
    exit /b 1
)

cmake -G Ninja -S "%HERE%." -B "%HERE%build\%CONFIG%" -DCMAKE_BUILD_TYPE=%CONFIG% || exit /b 1
cmake --build "%HERE%build\%CONFIG%" || exit /b 1
echo.
echo Built: %HERE%build\%CONFIG%\gt_engine_artefacts\%CONFIG%\GuitarTutor Engine.exe
