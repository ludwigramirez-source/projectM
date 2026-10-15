# Corre las pruebas del backend en un contenedor .NET 9 SDK conectado a la red del compose
# (usa el SQL Server del servicio "db"; cada clase de prueba crea y borra su propia BD).
# Uso: ./scripts/test.ps1                     -> todas las pruebas
#      ./scripts/test.ps1 --filter AuthTests  -> argumentos extra para dotnet test
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Extra)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$backend = Join-Path $root 'backend'

$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) { throw "Falta .env (copia .env.example)" }
$sa = (Get-Content $envFile | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''

docker compose -f (Join-Path $root 'docker-compose.yml') up -d --wait db | Out-Null

$dockerArgs = @(
  'run', '--rm',
  '--network', 'projectm_default',
  '-v', "${backend}:/src",
  '-v', 'projectm-nuget:/root/.nuget/packages',
  '-w', '/src',
  '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
  '-e', 'DOTNET_NOLOGO=1',
  '-e', "TEST_SQL_CONNECTION=Server=db;User Id=sa;Password=$sa;TrustServerCertificate=True",
  'mcr.microsoft.com/dotnet/sdk:9.0',
  'dotnet', 'test', '--logger', 'console;verbosity=minimal'
)
& docker @dockerArgs @Extra
exit $LASTEXITCODE
