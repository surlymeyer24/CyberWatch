# Requiere: Windows, PowerShell 5.1+.
# Compara Get-Service con la misma whitelist JSON que usa CyberWatch.Service (sin leer ImagePath del registro).
# Uso (desde la raíz del repo):
#   .\scripts\verificar-servicios-no-base-vm.ps1
#   .\scripts\verificar-servicios-no-base-vm.ps1 -Mostrar 50

param(
    [string] $RutaRepo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [int] $Mostrar = 25
)

$ErrorActionPreference = "Stop"
$jsonPath = Join-Path $RutaRepo "CyberWatch.Service\Data\servicios_base_windows.json"
if (-not (Test-Path -LiteralPath $jsonPath)) {
    throw "No se encontró: $jsonPath"
}

$lista = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
$whitelist = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($n in $lista) { [void]$whitelist.Add([string]$n) }

$services = Get-Service
$noBase = foreach ($s in $services) {
    if (-not $whitelist.Contains($s.Name)) { $s }
}

Write-Host "Whitelist: $($lista.Count) nombres | Servicios en el sistema: $($services.Count) | Posibles 'no base': $($noBase.Count)"
if ($Mostrar -gt 0 -and $noBase.Count -gt 0) {
    $noBase | Select-Object -First $Mostrar Name, DisplayName, Status | Format-Table -AutoSize
}
