# Ejecuta el CLI de dotnet dentro de un contenedor .NET 9 SDK (no requiere SDK local).
# Uso: ./scripts/dotnet.ps1 build
#      ./scripts/dotnet.ps1 ef migrations add Inicial --project src/ProjectM.Api
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$backend = Join-Path $root 'backend'

$dockerArgs = @(
  'run', '--rm',
  '-v', "${backend}:/src",
  '-v', 'projectm-nuget:/root/.nuget/packages',
  '-v', 'projectm-dotnet-tools:/root/.dotnet/tools',
  '-w', '/src',
  '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
  '-e', 'DOTNET_NOLOGO=1',
  'mcr.microsoft.com/dotnet/sdk:9.0'
)

if ($Args.Count -gt 0 -and $Args[0] -eq 'ef') {
  # dotnet-ef se restaura desde el manifiesto .config/dotnet-tools.json
  $cmd = "dotnet tool restore >/dev/null && dotnet " + ($Args -join ' ')
  & docker @dockerArgs bash -c $cmd
} else {
  & docker @dockerArgs dotnet @Args
}
exit $LASTEXITCODE
