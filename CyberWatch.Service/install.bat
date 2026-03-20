@echo off
setlocal EnableDelayedExpansion
echo ============================================
echo   CyberWatch - Instalador / Actualizador
echo ============================================
echo.

:: ── Comprobar privilegios de administrador ───────────────────────────────────
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Este instalador debe ejecutarse como Administrador.
    echo         Clic derecho en install.bat -^> "Ejecutar como administrador"
    echo.
    pause
    exit /b 1
)

set INSTALL_DIR=C:\Program Files\CyberWatch

:: ── 1. Detener todo lo que esté corriendo ────────────────────────────────────
echo [1/5] Deteniendo servicios existentes...
sc.exe query CyberWatch >nul 2>&1
if %errorLevel% equ 0 (
    net stop CyberWatch >nul 2>&1
    echo       Service detenido.
) else (
    echo       Service no estaba instalado.
)
taskkill /F /IM CyberWatch.UserAgent.exe /T >nul 2>&1
echo       UserAgent detenido.
timeout /t 2 /nobreak >nul

:: ── 2. Copiar archivos ──────────────────────────────────────────────────────
echo [2/5] Copiando archivos a %INSTALL_DIR%...
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
xcopy /E /I /Y "%~dp0*" "%INSTALL_DIR%\"
echo       Archivos copiados.

:: ── 3. Registrar e iniciar el Windows Service ────────────────────────────────
echo [3/5] Configurando Windows Service...
sc.exe query CyberWatch >nul 2>&1
if %errorLevel% equ 0 (
    sc.exe config CyberWatch binPath= "%INSTALL_DIR%\CyberWatch.Service.exe"
    echo       Service actualizado.
) else (
    sc.exe create CyberWatch binPath= "%INSTALL_DIR%\CyberWatch.Service.exe" start= auto
    echo       Service creado.
)
net start CyberWatch
echo       Service iniciado.

:: ── 4. Esperar a que el Service genere el machine ID ─────────────────────────
echo [4/5] Esperando inicializacion del Service (5s)...
timeout /t 5 /nobreak >nul

:: ── 5. Lanzar UserAgent directamente ─────────────────────────────────────────
echo [5/5] Iniciando UserAgent...
if exist "%INSTALL_DIR%\CyberWatch.UserAgent.exe" (
    start "" "%INSTALL_DIR%\CyberWatch.UserAgent.exe"
    echo       UserAgent iniciado.
) else (
    echo       [WARN] CyberWatch.UserAgent.exe no encontrado en %INSTALL_DIR%
)

echo.
echo ============================================
echo   Instalacion completada
echo ============================================
echo.
echo   Service:   corriendo como Windows Service (auto-start)
echo   UserAgent: corriendo en esta sesion
echo              (se iniciara automaticamente en futuros logins via Task Scheduler)
echo   Logs:      %INSTALL_DIR%\cyberwatch_service.log
echo.
pause
