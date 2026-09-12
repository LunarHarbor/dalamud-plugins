param([switch]$Bootstrap, [switch]$ChecksOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildRoot = Join-Path $projectRoot '.build'
$env:DOTNET_CLI_HOME = Join-Path $buildRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $buildRoot 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dalamudPath = Join-Path $buildRoot 'dalamud'
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetBinary = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (!(Test-Path -LiteralPath $dotnetBinary)) { throw 'Install .NET SDK 10.0.4xx before building.' }
function Invoke-DotNet([string[]]$Arguments) {
    & $dotnetBinary @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Push-Location $projectRoot
try {
    Invoke-DotNet -Arguments @('run', '--project', 'tests/Tracker.Checks', '-c', 'Release')
    if ($ChecksOnly) { return }
    if ($Bootstrap) {
        New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
        $archive = Join-Path $buildRoot 'dalamud.zip'
        $expectedHash = '2343AB848DEF4F749A8B7ECF9A139E82F38801A8F631F6B55CAF748C768AAB4C'
        if (!(Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest 'https://raw.githubusercontent.com/goatcorp/dalamud-distrib/86761b0e692b0346c9fe41fa96bdd0dd766f9b6f/latest.zip' -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
            throw 'Dalamud archive hash does not match the reviewed snapshot.'
        }
        Expand-Archive -LiteralPath $archive -DestinationPath $dalamudPath -Force
    }
    if (!(Test-Path -LiteralPath (Join-Path $dalamudPath 'Dalamud.dll'))) {
        throw 'Run scripts/Build.ps1 -Bootstrap once to fetch the pinned build references.'
    }
    $binaryOutput = Join-Path $buildRoot 'output/DeepDungeonTrackerPilgrim'
    Invoke-DotNet -Arguments @('build', 'DeepDungeonTracker.sln', '-c', 'Release', '--nologo', '-v', 'minimal',
        "-p:DalamudLibPath=$dalamudPath", "-p:OutputPath=$binaryOutput")
    $output = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $package = Join-Path $binaryOutput 'DeepDungeonTrackerPilgrim/latest.zip'
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'DeepDungeonTracker/DeepDungeonTracker.csproj')
    $version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    $destination = Join-Path $output "DeepDungeonTrackerPilgrim-$version.zip"
    Copy-Item -LiteralPath $package -Destination $destination -Force
    Compress-Archive -LiteralPath (Join-Path $projectRoot 'LICENSE.md'),(Join-Path $projectRoot 'docs/QUICKSTART.md') -DestinationPath $destination -Update
    Get-FileHash -LiteralPath $destination -Algorithm SHA256
    Write-Output "Package: $destination"
}
finally { Pop-Location }
