@echo off
echo Instalando CyberWatch...

:: Crear directorio
mkdir "C:\Program Files\CyberWatch"

:: Copiar archivos
xcopy /E /I /Y "%~dp0*" "C:\Program Files\CyberWatch\"

:: Instalar servicio
sc.exe create CyberWatch binPath= "C:\Program Files\CyberWatch\CyberWatch.Service.exe" start= auto

:: Iniciar servicio
sc.exe start CyberWatch

echo CyberWatch instalado correctamente.
pause