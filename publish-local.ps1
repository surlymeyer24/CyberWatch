# publish-local.ps1
# Genera el paquete de despliegue localmente (mismo resultado que el workflow de release).
# Uso:
#   .\publish-local.ps1
#   .\publish-local.ps1 -CredentialPath "auth\serviceAccountKey.json"
#   .\publish-local.ps1 -CredentialPath "C:\ruta\serviceAccountKey.json" -SkipZip
#   .\publish-local.ps1 -SkipZip   (solo carpeta publish_output, sin ZIP)

param(
    [string]$CredentialPath = "",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $root) { $root = Get-Location.Path }

$outDir = Join-Path $root "publish_output"
$uaDir  = Join-Path $root "publish_useragent"

Write-Host "Raiz del repo: $root" -ForegroundColor Cyan
Write-Host "Output: $outDir" -ForegroundColor Cyan

# Limpiar salida anterior
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
if (Test-Path $uaDir)  { Remove-Item $uaDir -Recurse -Force }

# 1) Publicar Service
Write-Host "`n[1/5] Publicando CyberWatch.Service..." -ForegroundColor Yellow
dotnet publish (Join-Path $root "CyberWatch.Service\CyberWatch.Service.csproj") `
    -c Release -r win-x64 --self-contained true -o $outDir
if ($LASTEXITCODE -ne 0) { throw "Publish Service fallo." }

# 2) Publicar UserAgent y copiar al output SIN sobrescribir archivos existentes
#    (el Service usa Google.Apis.Auth 1.68 vía FirebaseAdmin; si el UserAgent sobrescribe con otra versión, el servicio falla)
Write-Host "[2/5] Publicando CyberWatch.UserAgent..." -ForegroundColor Yellow
dotnet publish (Join-Path $root "CyberWatch.UserAgent\CyberWatch.UserAgent.csproj") `
    -c Release -r win-x64 --self-contained true -o $uaDir
if ($LASTEXITCODE -ne 0) { throw "Publish UserAgent fallo." }

$uaResolved = (Resolve-Path $uaDir).Path
$outResolved = (Resolve-Path $outDir).Path
Get-ChildItem -Path $uaResolved -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($uaResolved.Length + 1)
    if ($rel -eq "appsettings.json") { return }
    $dest = Join-Path $outResolved $rel
    if ($_.PSIsContainer) {
        if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force }
    } else {
        if (-not (Test-Path $dest)) { Copy-Item $_.FullName -Destination $dest -Force }
    }
}
Remove-Item $uaDir -Recurse -Force -ErrorAction SilentlyContinue

# 3) Credenciales Firebase
$credFile = Join-Path $outDir "serviceAccountKey.json"
if ($CredentialPath) {
    $src = $CredentialPath
    if (-not [System.IO.Path]::IsPathRooted($src)) { $src = Join-Path $root $src }
    if (Test-Path $src) {
        Write-Host "[3/5] Copiando credenciales desde $src" -ForegroundColor Yellow
        Copy-Item $src -Destination $credFile -Force
    } else {
        Write-Host "[3/5] AVISO: No existe $src - el paquete quedara sin serviceAccountKey.json" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[3/5] Sin -CredentialPath; no se copia serviceAccountKey.json" -ForegroundColor DarkYellow
}

# 4) Parchear appsettings.json (CredentialPath relativo, vaciar CredentialJson)
$appsettingsPath = Join-Path $outDir "appsettings.json"
if (Test-Path $appsettingsPath) {
    Write-Host "[4/5] Parcheando appsettings.json (CredentialPath = serviceAccountKey.json)" -ForegroundColor Yellow
    $json = Get-Content $appsettingsPath -Raw | ConvertFrom-Json
    $json.Firebase.CredentialPath = "serviceAccountKey.json"
    $json.Firebase.CredentialJson = ""
    $json | ConvertTo-Json -Depth 10 | Set-Content $appsettingsPath -Encoding UTF8
}

# 5) Copiar install.bat
Write-Host "[5/5] Copiando install.bat" -ForegroundColor Yellow
Copy-Item (Join-Path $root "CyberWatch.Service\install.bat") -Destination $outDir -Force

# ZIP del paquete (por defecto sí; usar -SkipZip para omitir)
if (-not $SkipZip) {
    $zipPath = Join-Path $root "CyberWatch.Service.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath
    Write-Host "`nZIP creado: $zipPath" -ForegroundColor Green
}

Write-Host "`nListo. Paquete en: $outDir" -ForegroundColor Green
if (-not $SkipZip) {
    Write-Host "ZIP listo para copiar o subir como asset de release: CyberWatch.Service.zip" -ForegroundColor Gray
}
Write-Host "Para instalar en otra PC: copiar la carpeta (o el ZIP), descomprimir y ejecutar install.bat como administrador." -ForegroundColor Gray
