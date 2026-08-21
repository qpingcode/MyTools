<#
.SYNOPSIS
    Downloads the official Node.js Windows x64 zip and copies node.exe into a publish directory.

.DESCRIPTION
    Used by the Desktop Velopack publish so installed MyTools can start Node plugins without a
    system-wide Node install. The zip is cached under artifacts/node-runtime and checksummed
    against the official SHASUMS256.txt entry.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $Destination,

    [Parameter(Mandatory = $true)]
    [string] $Sha256,

    [Parameter(Mandatory = $true)]
    [string] $CacheDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Version = $Version.TrimStart("v")
$zipName = "node-v$Version-win-x64.zip"
$downloadUri = "https://nodejs.org/dist/v$Version/$zipName"
$cacheZip = Join-Path $CacheDirectory $zipName
$expected = $Sha256.ToLowerInvariant()

New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

if (Test-Path $cacheZip -PathType Leaf) {
    $actual = (Get-FileHash -Algorithm SHA256 -Path $cacheZip).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Write-Host "Cached Node zip hash mismatch; downloading again."
        Remove-Item -Force $cacheZip
    }
}

if (-not (Test-Path $cacheZip -PathType Leaf)) {
    Write-Host "Downloading $downloadUri"
    Invoke-WebRequest -Uri $downloadUri -OutFile $cacheZip -UseBasicParsing
}

$actual = (Get-FileHash -Algorithm SHA256 -Path $cacheZip).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    throw "Node zip SHA256 mismatch for $zipName. Expected $expected, got $actual."
}

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mytools-node-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $extractRoot | Out-Null
try {
    Expand-Archive -LiteralPath $cacheZip -DestinationPath $extractRoot -Force
    $extractedNode = Get-ChildItem -Path $extractRoot -Recurse -Filter "node.exe" |
        Where-Object { $_.Directory.Name -like "node-v$Version-win-x64" } |
        Select-Object -First 1
    if ($null -eq $extractedNode) {
        $extractedNode = Get-ChildItem -Path $extractRoot -Recurse -Filter "node.exe" | Select-Object -First 1
    }
    if ($null -eq $extractedNode) {
        throw "node.exe was not found inside $zipName."
    }

    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath $extractedNode.FullName -Destination (Join-Path $Destination "node.exe")
    Set-Content -LiteralPath (Join-Path $Destination "VERSION") -Value $Version -NoNewline
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$bundled = Join-Path $Destination "node.exe"
if (-not (Test-Path $bundled -PathType Leaf)) {
    throw "Failed to bundle Node runtime to $bundled"
}

Write-Host "Bundled Node v$Version to $bundled"
