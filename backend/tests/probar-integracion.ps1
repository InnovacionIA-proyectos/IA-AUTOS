# Corre TODAS las pruebas de integracion en dos tandas seguidas: primero las de API y luego las de migracion.
# Juntas en una sola ejecucion tardan mas (compiten por el mismo SQL Server); por separado cada tanda aprovecha todo el paralelismo.
# Requiere Docker encendido. Uso:  pwsh backend/tests/probar-integracion.ps1   (o -SinCompilar si ya se compilo)
param([switch]$SinCompilar)

$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot 'IA-AUTOS.Tests.Integracion'

if (-not $SinCompilar) {
    dotnet build $proyecto --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$total = [System.Diagnostics.Stopwatch]::StartNew()
$fallo = 0
foreach ($tanda in @(
        @{ Nombre = 'API y demas'; Filtro = 'FullyQualifiedName!~Infraestructura.Migracion' },
        @{ Nombre = 'Migraciones'; Filtro = 'FullyQualifiedName~Infraestructura.Migracion' })) {
    Write-Host "== Tanda: $($tanda.Nombre)" -ForegroundColor Cyan
    dotnet test $proyecto --no-build --nologo --filter $tanda.Filtro
    if ($LASTEXITCODE -ne 0) { $fallo = $LASTEXITCODE }
}

Write-Host ("Tiempo total: {0:mm\:ss}" -f $total.Elapsed)
exit $fallo
