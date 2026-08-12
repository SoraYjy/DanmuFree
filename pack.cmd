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
where bash >nul 2>nul
if errorlevel 1 (
  echo bash not found. Install Git for Windows into PATH, or run ./pack.sh inside Git Bash.
  exit /b 1
)
bash pack.sh %*
