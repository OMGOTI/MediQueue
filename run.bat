@echo off
cd /d "%~dp0"
echo MediQueue starting on http://localhost:5080  (Ctrl+C to stop)
dotnet run
pause
