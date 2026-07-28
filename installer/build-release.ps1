[CmdletBinding()]
param(
    [string]$Version,
    [string]$MakeNsis,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'src\VelvetTools\VelvetTools.csproj'
$publishDir = Join-Path $repoRoot 'out\publish-win-x64'
$releaseDir = Join-Path $repoRoot 'out\release'
$installerScript = Join-Path $PSScriptRoot 'VelvetTools.nsi'
$licenseSourcePath = Join-Path $PSScriptRoot 'LICENSE-AGREEMENT.txt'
$licenseBuildPath = Join-Path $releaseDir 'LICENSE-AGREEMENT-installer.txt'
$licenseText = ''

if (-not $Version) {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
    $Version = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
if (-not $Version) {
    throw 'Unable to determine the release version from VelvetTools.csproj.'
}

$versionMatch = [regex]::Match(
    $Version,
    '^(\d+)\.(\d+)\.(\d+)(?:-beta\.(\d+))?$',
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
)
if (-not $versionMatch.Success) {
    throw "Unsupported version format: $Version"
}
$revision = if ($versionMatch.Groups[4].Success) {
    [int]$versionMatch.Groups[4].Value
}
else {
    0
}
$fileVersion = '{0}.{1}.{2}.{3}' -f @(
    $versionMatch.Groups[1].Value,
    $versionMatch.Groups[2].Value,
    $versionMatch.Groups[3].Value,
    $revision
)

function Reset-BuildDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $requiredPrefix = $repoRoot.TrimEnd('\') + '\out\'
    if (-not $resolved.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the repository out folder: $resolved"
    }

    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Resolve-MakeNsis {
    if ($MakeNsis) {
        return (Resolve-Path -LiteralPath $MakeNsis).Path
    }

    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        'C:\Program Files (x86)\NSIS\makensis.exe',
        'C:\Program Files\NSIS\makensis.exe',
        'D:\cc\tool-cache\nsis-3.12\nsis-3.12\makensis.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'makensis.exe was not found. Install NSIS 3.12 or pass -MakeNsis explicitly.'
}

function Resolve-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -Recurse |
            Where-Object FullName -Match '\\x64\\signtool\.exe$' |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }
    throw 'A certificate thumbprint was supplied, but signtool.exe was not found.'
}

function Sign-File {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SignTool
    )

    & $SignTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed: $Path"
    }
}

Reset-BuildDirectory -Path $publishDir
Reset-BuildDirectory -Path $releaseDir

dotnet restore $projectPath -r win-x64 -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

dotnet build $projectPath -c Release --no-restore -p:TreatWarningsAsErrors=true -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    --no-restore `
    -o $publishDir `
    -p:NuGetAudit=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }

$signTool = $null
if ($CertificateThumbprint) {
    $signTool = Resolve-SignTool
    Sign-File -Path (Join-Path $publishDir 'VelvetTools.exe') -SignTool $signTool
}
else {
    Write-Warning 'No Authenticode certificate was provided. Beta artifacts will be unsigned.'
}

$estimatedSizeKb = [int][Math]::Ceiling(
    ((Get-ChildItem -LiteralPath $publishDir -File -Recurse | Measure-Object Length -Sum).Sum) / 1KB
)
$makeNsisPath = Resolve-MakeNsis

# MUI LicenseData 要求 DOS (CRLF) 文本；UTF-16LE BOM 让 Unicode NSIS
# 不依赖机器的活动代码页，避免中文协议在安装界面中变成乱码。
$licenseText = [System.IO.File]::ReadAllText($licenseSourcePath, [System.Text.Encoding]::UTF8)
$licenseText = [regex]::Replace($licenseText, "\r\n|\r|\n", "`r`n")
if (-not $licenseText.EndsWith("`r`n", [StringComparison]::Ordinal)) {
    $licenseText += "`r`n"
}
[System.IO.File]::WriteAllText($licenseBuildPath, $licenseText, [System.Text.Encoding]::Unicode)
$licenseBytes = [System.IO.File]::ReadAllBytes($licenseBuildPath)
if ($licenseBytes.Length -lt 2 -or $licenseBytes[0] -ne 0xFF -or $licenseBytes[1] -ne 0xFE) {
    throw 'Installer license is missing its UTF-16LE BOM.'
}
if ([regex]::IsMatch($licenseText, "(?<!\r)\n")) {
    throw 'Installer license still contains a non-CRLF line ending.'
}
if ($licenseText.Length -lt 500) {
    # 防回归：曾因脚本被 Windows PowerShell 按 ANSI 误读，读取行被上一行中文注释吞掉，
    # 导致嵌入空协议、安装界面协议页空白。
    throw "Installer license text is suspiciously short ($($licenseText.Length) chars)."
}
Write-Host 'Installer license encoding verified: UTF-16LE BOM + CRLF.'

$nsisExitCode = -1
try {
    & $makeNsisPath `
        '/INPUTCHARSET' `
        'UTF8' `
        "/DAPP_VERSION=$Version" `
        "/DAPP_FILE_VERSION=$fileVersion" `
        "/DPUBLISH_DIR=$publishDir" `
        "/DOUTPUT_DIR=$releaseDir" `
        "/DAPP_ESTIMATED_SIZE_KB=$estimatedSizeKb" `
        "/DLICENSE_FILE=$licenseBuildPath" `
        $installerScript
    $nsisExitCode = $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $licenseBuildPath) {
        Remove-Item -LiteralPath $licenseBuildPath -Force
    }
}
if ($nsisExitCode -ne 0) { throw 'NSIS compilation failed.' }

$setupPath = Join-Path $releaseDir "VelvetTools-Setup-$Version-win-x64.exe"
if ($CertificateThumbprint) {
    Sign-File -Path $setupPath -SignTool $signTool
}

$portablePath = Join-Path $releaseDir "VelvetTools-Portable-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portablePath -CompressionLevel Optimal

$artifacts = @($setupPath, $portablePath)
$checksumLines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($artifact))"
}
$checksumPath = Join-Path $releaseDir 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines(
    $checksumPath,
    $checksumLines,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host ''
Write-Host "Release artifacts for Velvet Tools ${Version}:"
Get-ChildItem -LiteralPath $releaseDir -File |
    Select-Object Name, Length, LastWriteTime |
    Format-Table -AutoSize
