@echo off
cd /d "%~dp0"
echo MediQueue for the whole Wi-Fi: open http://YOUR-PC-IP:5080 on phones  (Ctrl+C to stop)
dotnet run --urls http://0.0.0.0:5080
pause
