@echo off
rem Copies the band's sounds in this folder to %APPDATA%\YourBand, where the engine keeps them. A file already
rem there is left as it is (/XC /XN /XO: only the missing ones are copied).
robocopy "%~dp0." "%APPDATA%\YourBand" *.sounds /S /XC /XN /XO /NJH /NJS /NDL
if %ERRORLEVEL% GEQ 8 (echo Copying the settings failed.) else (echo The settings are in %APPDATA%\YourBand.)
pause
