@echo off
rem Windows entry point for building the green-distribution release.
rem Runs pack.sh via Git Bash. Usage (PowerShell / cmd / double-click):
rem   pack.cmd                 -> auto-detect node.exe
rem   pack.cmd path\node.exe   -> use a specific node.exe
rem
rem Why not .\pack.sh directly? In PowerShell/cmd, .\pack.sh goes through the
rem .sh file association (-> git-bash.exe), spawning a separate bash window that
rem closes instantly so you never see output/errors. This wrapper invokes bash
rem in the current console so everything stays visible.
setlocal
cd /d "%~dp0"

set "BASH="
rem 1) bash already in PATH?
for /f "delims=" %%i in ('where bash 2^>nul') do if not defined BASH set "BASH=%%i"
rem 2) else derive from git.exe (Git for Windows puts git in PATH, not bash):
rem    git lives at <root>\cmd\ or <root>\mingw64\bin\; bash at <root>\bin\ or <root>\usr\bin\.
if not defined BASH for /f "delims=" %%i in ('where git 2^>nul') do if not defined BASH call :findbash "%%~dpi"

if not defined BASH (
  echo Could not find Git Bash. Install Git for Windows, or run pack.sh inside Git Bash.
  pause
  exit /b 1
)

"%BASH%" pack.sh %*
echo.
echo pack.cmd finished. Output: dist\DanmuFree\
pause
exit /b

:findbash
for %%I in ("%~1..\bin\bash.exe")        do if exist "%%~fI" set "BASH=%%~fI"
if defined BASH goto :eof
for %%I in ("%~1..\usr\bin\bash.exe")    do if exist "%%~fI" set "BASH=%%~fI"
if defined BASH goto :eof
for %%I in ("%~1..\..\usr\bin\bash.exe") do if exist "%%~fI" set "BASH=%%~fI"
goto :eof
