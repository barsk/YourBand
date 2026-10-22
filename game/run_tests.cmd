@echo off
rem Runs the EditMode tests in batch mode (close the project in the Editor first) and prints the result.
rem The project is opened once first: in test mode Unity does not import newly added scripts.
setlocal
set HERE=%~dp0
set UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe
rem results from an earlier run must not pass for this one's
if exist "%HERE%Logs\test-results.xml" del "%HERE%Logs\test-results.xml"
"%UNITY%" -batchmode -quit -projectPath "%HERE%." -logFile "%HERE%Logs\refresh.log"
if errorlevel 1 (
    echo The project did not open: see Logs\refresh.log
    exit /b 1
)
findstr /c:"error CS" "%HERE%Logs\refresh.log" >nul && (
    echo The scripts do not compile:
    findstr /c:"error CS" "%HERE%Logs\refresh.log"
    exit /b 1
)
"%UNITY%" -batchmode -projectPath "%HERE%." -runTests -testPlatform EditMode -testResults "%HERE%Logs\test-results.xml" -logFile "%HERE%Logs\tests.log"
set RESULT=%ERRORLEVEL%
findstr /r /c:"<test-run .*result=" "%HERE%Logs\test-results.xml"
if not "%RESULT%"=="0" echo Tests failed or did not run: see Logs\tests.log
exit /b %RESULT%
