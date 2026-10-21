@echo off
rem Runs the EditMode tests in batch mode (close the project in the Editor first) and prints the result.
setlocal
set HERE=%~dp0
set UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe
"%UNITY%" -batchmode -projectPath "%HERE%." -runTests -testPlatform EditMode -testResults "%HERE%Logs\test-results.xml" -logFile "%HERE%Logs\tests.log"
set RESULT=%ERRORLEVEL%
findstr /r /c:"<test-run .*result=" "%HERE%Logs\test-results.xml"
if not "%RESULT%"=="0" echo Tests failed or did not run: see Logs\tests.log
exit /b %RESULT%
