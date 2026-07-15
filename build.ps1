param([switch]$SkipInstaller)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnetCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\dotnet\dotnet.exe'),
    (Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) }
$dotnet = $dotnetCandidates | Select-Object -First 1
if (-not $dotnet) { throw 'Install .NET 8 SDK before building.' }

$icon = Join-Path $root 'src\Voltaris\Assets\Voltaris.ico'
if (-not (Test-Path $icon)) { & (Join-Path $root 'scripts\New-VoltarisIcon.ps1') -OutputPath $icon }

& $dotnet restore (Join-Path $root 'Voltaris.sln')
& $dotnet restore (Join-Path $root 'src\Voltaris\Voltaris.csproj') -r win-x64
& $dotnet build (Join-Path $root 'Voltaris.sln') -c Release --no-restore
& $dotnet run --project (Join-Path $root 'tests\Voltaris.SelfTest\Voltaris.SelfTest.csproj') -c Release --no-build

$publish = Join-Path $root 'artifacts\publish'
& $dotnet publish (Join-Path $root 'src\Voltaris\Voltaris.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    --no-restore -o $publish

if ($SkipInstaller) { return }
$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    'C:\Program Files\Inno Setup 7\ISCC.exe',
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe',
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) { throw 'Install Inno Setup 7 or 6 before building the installer.' }
& $iscc (Join-Path $root 'installer\Voltaris.iss')
