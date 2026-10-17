@echo off
rem Oylik demo: alohida demo\data bazasi bilan (haqiqiy %LOCALAPPDATA%\Oylik ga tegmaydi)
set "OYLIK_HOME=%~dp0demo\data"
start "" "%~dp0demo\bin\Oylik.exe"
