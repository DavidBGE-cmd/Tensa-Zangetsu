@echo off
chcp 65001 >nul
title Instalador Automatico - TensaZangetsu

echo ===================================================
echo      Instalador Automatico - TensaZangetsu
echo ===================================================

REM Verificar si dotnet existe en el sistema ocultando los errores
echo [*] Verificando requisitos del sistema (SDK de .NET 10.0)...

dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] No se encontro el SDK de .NET.
    echo [*] Iniciando la instalacion con winget...
    winget install Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
    
    echo ===================================================
    echo [!] IMPORTANTE: Se instalo .NET correctamente.
    echo [!] Para que Windows lo reconozca, CIERRA esta ventana.
    echo [!] Despues, vuelve a abrir setup.bat para continuar.
    echo ===================================================
    pause
    exit /b
)

echo [+] SDK de .NET detectado correctamente.
echo.

REM Restaurar dependencias
echo [*] Restaurando dependencias...
dotnet restore TensaZangetsu.sln
if %errorlevel% neq 0 (
    echo [X] Error al restaurar las dependencias.
    pause
    exit /b
)

REM Compilar el proyecto
echo.
echo [*] Compilando la solucion...
dotnet build TensaZangetsu.sln
if %errorlevel% neq 0 (
    echo [X] Error durante la compilacion. Revisa el registro arriba.
    pause
    exit /b
)

echo.
echo ===================================================
echo [+] Instalacion y compilacion completadas con exito.
echo ===================================================
echo.

set /p abrir="Deseas iniciar la aplicacion ahora? (S/N): "
if /i "%abrir%"=="S" (
    echo [*] Abriendo TensaZangetsu...
    dotnet run --project src/TensaZangetsu.App/TensaZangetsu.App.csproj
) else (
    echo [*] Instalacion finalizada. Puedes cerrar esta ventana.
    pause
)