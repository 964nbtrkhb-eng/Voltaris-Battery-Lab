param(
    [switch]$SkipInstaller,
    [string]$SignThumbprint,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnetCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\dotnet\dotnet.exe'),
    (Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) }
$dotnet = $dotnetCandidates | Select-Object -First 1
if (-not $dotnet) { throw 'Install .NET 8 SDK before building.' }

function Sign-Artifact([string]$Path) {
    if (-not $SignThumbprint) { return }

    $thumbprint = $SignThumbprint -replace '\s', ''
    $certificate = Get-Item "Cert:\CurrentUser\My\$thumbprint" -ErrorAction Stop
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    if (-not $certificate.HasPrivateKey -or
        $certificate.NotBefore -gt (Get-Date) -or
        $certificate.NotAfter -le (Get-Date) -or
        $certificate.EnhancedKeyUsageList.ObjectId.Value -notcontains $codeSigningOid) {
        throw "Certificate $thumbprint is not a valid current-user code-signing certificate."
    }

    $signature = Set-AuthenticodeSignature -FilePath $Path -Certificate $certificate `
        -TimestampServer $TimestampServer -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') { throw "Signing failed for ${Path}: $($signature.StatusMessage)" }
}

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
Sign-Artifact (Join-Path $publish 'Voltaris.exe')

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
$installer = Get-ChildItem (Join-Path $root 'artifacts\installer') -Filter 'Voltaris-Setup-*.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) { throw 'Installer was not created.' }
Sign-Artifact $installer.FullName
