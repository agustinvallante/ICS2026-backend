$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\Dsw2025Tpi.Api\Dsw2025Tpi.Api.csproj"

$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

try {
    $rng.GetBytes($bytes)
    $secret = [Convert]::ToBase64String($bytes)

    dotnet user-secrets set "Jwt:Key" $secret --project $project

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo configurar el secreto JWT."
    }

    Write-Host "Secreto JWT generado y configurado correctamente."
}
finally {
    $rng.Dispose()
    [Array]::Clear($bytes, 0, $bytes.Length)
    $secret = $null
}