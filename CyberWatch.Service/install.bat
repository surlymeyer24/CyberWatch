@echo off
setlocal EnableDelayedExpansion
echo Instalando CyberWatch...

:: ── Comprobar privilegios de administrador ───────────────────────────────────
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo [ERROR] Este instalador debe ejecutarse como Administrador.
    echo         Clic derecho en install.bat -^> "Ejecutar como administrador"
    echo.
    pause
    exit /b 1
)

:: Crear directorio
if not exist "C:\Program Files\CyberWatch" mkdir "C:\Program Files\CyberWatch"

:: Copiar archivos (incluye CyberWatch.Service.exe, CyberWatch.UserAgent.exe, serviceAccountKey.json, appsettings.json)
xcopy /E /I /Y "%~dp0*" "C:\Program Files\CyberWatch\"

:: Instalar o actualizar servicio (idempotente: si ya existe, solo reiniciar para cargar los nuevos archivos)
sc.exe query CyberWatch >nul 2>&1
if %errorLevel% equ 0 (
    echo El servicio CyberWatch ya existe. Actualizando ruta y reiniciando...
    sc.exe config CyberWatch binPath= "C:\Program Files\CyberWatch\CyberWatch.Service.exe"
    sc.exe stop CyberWatch
    timeout /t 2 /nobreak >nul
    sc.exe start CyberWatch
) else (
    sc.exe create CyberWatch binPath= "C:\Program Files\CyberWatch\CyberWatch.Service.exe" start= auto
    sc.exe start CyberWatch
)

echo.
echo CyberWatch instalado correctamente.
echo El servicio registrará CyberWatch.UserAgent en el Programador de tareas al iniciar sesión.
pause
