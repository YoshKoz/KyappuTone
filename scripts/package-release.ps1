<#
.SYNOPSIS
Builds a Release MSIX package for KyappuTone and writes a SHA-256 checksum.

.DESCRIPTION
Supports three signing modes:
  * unsigned (default) - suitable only for local registration in Developer Mode;
  * a supplied PFX whose subject exactly matches the manifest Publisher;
  * a self-signed development certificate installed into CurrentUser\TrustedPeople.

The development certificate is for local testing only. It is not production-trusted
and will not install on another machine unless that machine trusts the certificate.

.PARAMETER Platform
Target architecture: x64 or ARM64.

.PARAMETER Version
Three-part product version, for example 0.4.0. The script writes this version into
Package.appxmanifest and KyappuTone.csproj before building.

.PARAMETER CertificatePath
Path to a password-protected PFX. The certificate subject must exactly equal the
Publisher value in Package.appxmanifest.

.PARAMETER CertificatePassword
Password for CertificatePath, or for the generated development PFX.

.PARAMETER CreateDevelopmentCertificate
Create or reuse a self-signed CN=KyappuTone Development certificate, trust it for the
current user, and sign the package with it. Never use this certificate for public builds.

.PARAMETER Unsigned
Force an unsigned package even if a development certificate is available.

.EXAMPLE
.\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 -Unsigned

.EXAMPLE
.\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 -CreateDevelopmentCertificate

.EXAMPLE
.\scripts\package-release.ps1 -Platform ARM64 -Version 0.4.0 `
    -CertificatePath C:\secure\KyappuTone.pfx -CertificatePassword $env:KYAPPUTONE_CERT_PASSWORD
#>

[CmdletBinding(DefaultParameterSetName = 'Unsigned')]
param(
    [Parameter(Mandatory)]
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d{1,5}\.\d{1,5}\.\d{1,5}$')]
    [string]$Version,

    [Parameter(Mandatory, ParameterSetName = 'Pfx')]
    [string]$CertificatePath,

    [Parameter(ParameterSetName = 'Pfx')]
    [Parameter(ParameterSetName = 'Development')]
    [string]$CertificatePassword,

    [Parameter(Mandatory, ParameterSetName = 'Development')]
    [switch]$CreateDevelopmentCertificate,

    [Parameter(ParameterSetName = 'Unsigned')]
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'KyappuTone.sln'
$project = Join-Path $repoRoot 'KyappuTone.csproj'
$manifest = Join-Path $repoRoot 'Package.appxmanifest'
$packageDir = Join-Path $repoRoot 'AppPackages'

foreach ($path in @($solution, $project, $manifest)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$versionParts = $Version.Split('.')
foreach ($part in $versionParts) {
    if ([int]$part -gt 65535) {
        throw "Version part '$part' exceeds the MSIX limit of 65535."
    }
}
$packageVersion = "$Version.0"

function Write-Utf8TextPreservingBom {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Text
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $encoding = [System.Text.UTF8Encoding]::new($hasBom)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Set-ReleaseVersion {
    param([Parameter(Mandatory)][string]$ProductVersion)

    $projectText = [System.IO.File]::ReadAllText($project)
    $projectText = [regex]::Replace(
        $projectText,
        '<Version>\d+\.\d+\.\d+</Version>',
        "<Version>$ProductVersion</Version>")
    $projectText = [regex]::Replace(
        $projectText,
        '<AssemblyVersion>\d+\.\d+\.\d+\.\d+</AssemblyVersion>',
        "<AssemblyVersion>$ProductVersion.0</AssemblyVersion>")
    $projectText = [regex]::Replace(
        $projectText,
        '<FileVersion>\d+\.\d+\.\d+\.\d+</FileVersion>',
        "<FileVersion>$ProductVersion.0</FileVersion>")
    Write-Utf8TextPreservingBom -Path $project -Text $projectText

    $manifestText = [System.IO.File]::ReadAllText($manifest)
    $versionReplacement = '${1}' + $ProductVersion + '.0' + '${2}'
    $manifestText = [regex]::Replace(
        $manifestText,
        '(<Identity\b[^>]*?\bVersion=")\d+\.\d+\.\d+\.\d+(")',
        $versionReplacement,
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    Write-Utf8TextPreservingBom -Path $manifest -Text $manifestText
}

function Read-ManifestPublisher {
    $text = [System.IO.File]::ReadAllText($manifest)
    $match = [regex]::Match(
        $text,
        '<Identity\b[^>]*?\bPublisher="([^"]+)"',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) {
        throw "Could not read Publisher from $manifest."
    }
    return $match.Groups[1].Value
}

function Import-TrustedCertificate {
    param([Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate)

    foreach ($storeName in @('TrustedPeople', 'Root')) {
        $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
            $storeName,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        try {
            $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $existing = $store.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $Certificate.Thumbprint,
                $false)
            if ($existing.Count -eq 0) {
                $store.Add($Certificate)
            }
        }
        finally {
            $store.Close()
        }
    }
}

function Get-DevelopmentCertificate {
    param(
        [Parameter(Mandatory)][string]$Publisher,
        [string]$Password
    )

    $signingDir = Join-Path $env:LOCALAPPDATA 'KyappuTone\Signing'
    New-Item -ItemType Directory -Force -Path $signingDir | Out-Null

    $pfxPath = Join-Path $signingDir 'KyappuTone-Development.pfx'
    $passwordPath = Join-Path $signingDir 'KyappuTone-Development.password.txt'
    $cerPath = Join-Path $signingDir 'KyappuTone-Development.cer'

    if (Test-Path -LiteralPath $pfxPath) {
        if ([string]::IsNullOrEmpty($Password)) {
            if (-not (Test-Path -LiteralPath $passwordPath)) {
                throw "A development PFX already exists at $pfxPath but its password file is missing. Pass -CertificatePassword or remove the PFX."
            }
            $Password = ([System.IO.File]::ReadAllText($passwordPath)).Trim()
        }

        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfxPath,
            $Password,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)
    }
    else {
        if ([string]::IsNullOrEmpty($Password)) {
            $Password = [Convert]::ToHexString(
                [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24)).ToLowerInvariant()
            [System.IO.File]::WriteAllText($passwordPath, $Password, [System.Text.UTF8Encoding]::new($false))
        }

        Write-Host "Creating development certificate for '$Publisher'..."
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $Publisher `
            -FriendlyName 'KyappuTone Development Signing' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy Exportable `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -NotAfter (Get-Date).AddYears(5) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')

        $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
        $certificate | Export-PfxCertificate -FilePath $pfxPath -Password $securePassword | Out-Null
        Export-Certificate -Cert $certificate -FilePath $cerPath -Type CERT | Out-Null
    }

    if ($certificate.Subject -ne $Publisher) {
        throw "Development certificate subject '$($certificate.Subject)' does not match manifest Publisher '$Publisher'."
    }

    Import-TrustedCertificate -Certificate $certificate
    return [pscustomobject]@{
        Certificate = $certificate
        PfxPath = $pfxPath
        Password = $Password
        CerPath = $cerPath
    }
}

Set-ReleaseVersion -ProductVersion $Version

$publisher = Read-ManifestPublisher
$signingMode = 'unsigned'
$signingArgs = @()
$certificateForRelease = $null

switch ($PSCmdlet.ParameterSetName) {
    'Pfx' {
        $resolvedPfx = (Resolve-Path -LiteralPath $CertificatePath).Path
        if ([string]::IsNullOrEmpty($CertificatePassword)) {
            throw 'CertificatePassword is required when CertificatePath is supplied.'
        }

        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedPfx,
            $CertificatePassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)

        if ($certificate.Subject -ne $publisher) {
            throw "Certificate subject '$($certificate.Subject)' does not exactly match manifest Publisher '$publisher'. Update Package.appxmanifest or use the correct certificate."
        }

        $signingMode = 'signed'
        $signingArgs = @(
            '-p:AppxPackageSigningEnabled=true',
            "-p:PackageCertificateKeyFile=$resolvedPfx",
            "-p:PackageCertificatePassword=$CertificatePassword",
            "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)")
        $certificateForRelease = $certificate
    }
    'Development' {
        $development = Get-DevelopmentCertificate -Publisher $publisher -Password $CertificatePassword
        $signingMode = 'dev-signed'
        $signingArgs = @(
            '-p:AppxPackageSigningEnabled=true',
            "-p:PackageCertificateKeyFile=$($development.PfxPath)",
            "-p:PackageCertificatePassword=$($development.Password)",
            "-p:PackageCertificateThumbprint=$($development.Certificate.Thumbprint)")
        $certificateForRelease = $development.Certificate
        $developmentCer = $development.CerPath

        Write-Warning 'The self-signed development certificate is not production-trusted. Other machines must trust the exported .cer before installing this package.'
    }
    default {
        $signingArgs = @('-p:AppxPackageSigningEnabled=false')
    }
}

Write-Host "Building KyappuTone $Version ($packageVersion) for $Platform ($signingMode)..."
Write-Host "  Manifest publisher: $publisher"

$started = Get-Date
$buildArgs = @(
    'build', $solution,
    "-p:Platform=$Platform",
    '-c', 'Release',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxSymbolPackageEnabled=false')

$msPdbCmf = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Microsoft Visual Studio" `
    -Filter mspdbcmf.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\Hostx64\\x64\\' } |
    Select-Object -First 1
if ($null -ne $msPdbCmf) {
    $buildArgs += "-p:MsPdbCmfExeFullpath=$($msPdbCmf.FullName)"
}

$buildArgs += $signingArgs

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$packages = @(Get-ChildItem -Path $packageDir -Filter *.msix -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object {
        $_.LastWriteTime -ge $started.AddMinutes(-1) -and
        $_.Name -match [regex]::Escape($Platform)
    } |
    Sort-Object LastWriteTime -Descending)

if ($packages.Count -eq 0) {
    throw "No .msix produced under $packageDir. Check that GenerateAppxPackageOnBuild=true is supported by the installed SDK."
}

$sourcePackage = $packages[0]
$releaseDir = Join-Path $packageDir $Version
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$suffix = switch ($signingMode) {
    'unsigned' { 'unsigned' }
    'dev-signed' { 'dev-signed' }
    default { 'signed' }
}
$releaseName = "KyappuTone-$Version-$Platform-$suffix.msix"
$releasePath = Join-Path $releaseDir $releaseName
Copy-Item -LiteralPath $sourcePackage.FullName -Destination $releasePath -Force

$hash = (Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
$checksumPath = "$releasePath.sha256"
$checksumLine = "$hash  $releaseName"
[System.IO.File]::WriteAllText($checksumPath, $checksumLine + "`r`n", [System.Text.UTF8Encoding]::new($false))

$signature = Get-AuthenticodeSignature -LiteralPath $releasePath
if ($signingMode -eq 'unsigned') {
    if ($signature.Status -ne 'NotSigned') {
        throw "Expected an unsigned package but signature status is '$($signature.Status)'."
    }
}
elseif ($signature.Status -ne 'Valid') {
    throw "Package signature is not valid: $($signature.Status) - $($signature.StatusMessage)"
}

if ($null -ne $certificateForRelease -and $signingMode -eq 'dev-signed') {
    $releaseCer = Join-Path $releaseDir "KyappuTone-$Version-$Platform-development.cer"
    Export-Certificate -Cert $certificateForRelease -FilePath $releaseCer -Type CERT | Out-Null
}

Write-Host ''
Write-Host "Package:  $releasePath"
Write-Host "SHA-256:  $hash"
Write-Host "Checksum: $checksumPath"
Write-Host "Signature: $($signature.Status)"
if ($signingMode -eq 'dev-signed') {
    Write-Warning 'This is a development-signed package, not a production release. It is trusted only where the development certificate is installed.'
}
