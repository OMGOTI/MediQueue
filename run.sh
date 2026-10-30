#!/bin/sh
cd "$(dirname "$0")"
echo "MediQueue on http://localhost:5080  (Ctrl+C to stop)"
dotnet run
