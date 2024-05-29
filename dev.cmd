@echo off
rem Entry point from PowerShell and cmd. The actual work lives in ./dev - one file,
rem so the same code runs locally and on the GitHub runner (shell: bash).
where bash >nul 2>&1
if errorlevel 1 (
    echo ERROR: bash not found. Install Git for Windows, it ships Git Bash.
    exit /b 1
)
bash "%~dp0dev" %*
