@echo off
rem Makes a release zip of GuitarTutor to install on another computer (see make_release.ps1 for the options).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_release.ps1" %*
