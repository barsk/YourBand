@echo off
rem Sets the project up (ProjectSetup.Run) and builds the desktop player into Builds\Desktop (close the project
rem in the Editor first). Prints the setup and build lines from Logs\build.log.
setlocal
set HERE=%~dp0
set UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe
"%UNITY%" -batchmode -quit -projectPath "%HERE%." -executeMethod GuitarTutor.EditorTools.ProjectSetup.BuildDesktop -logFile "%HERE%Logs\build.log"
set RESULT=%ERRORLEVEL%
findstr /c:"GuitarTutor setup" /c:"GuitarTutor build" /c:"error CS" "%HERE%Logs\build.log"
if not "%RESULT%"=="0" echo The build failed: see Logs\build.log
exit /b %RESULT%
