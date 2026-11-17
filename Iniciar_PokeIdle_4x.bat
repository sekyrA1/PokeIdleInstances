@echo off
setlocal
cd /d "%~dp0"

set "EXE_PATH=%~dp0bin\Release\net10.0-windows\PokeIdleMulti.exe"

if not exist "%EXE_PATH%" (
    echo Compilando PokeIdle Multi pela primeira vez...
    dotnet build -c Release
    if errorlevel 1 (
        echo Falha ao compilar o projeto.
        pause
        exit /b 1
    )
)

start "" "%EXE_PATH%"
