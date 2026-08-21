<#
.SYNOPSIS
    Copies a matching node.exe into a publish directory, downloading the official zip if needed.

.DESCRIPTION
    Used by the Desktop Velopack publish so installed MyTools can start Node plugins without a
    system-wide Node install.

    Prefers an already-installed Node of the exact requested version (CI setup-node, PATH, or
    -PreferredNodeExe). Falls back to the official Windows x64 zip, cached under
    artifacts/node-runtime and checksummed against the official SHASUMS256.txt entry.
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
    [string] $CacheDirectory,

    [Parameter(Mandatory = $false)]
    [string] $PreferredNodeExe = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Sha256Hex([string] $Path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return ([System.BitConverter]::ToString($algorithm.ComputeHash($stream)) -replace '-', '').ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-NodeFileVersion([string] $Exe) {
    if ([string]::IsNullOrWhiteSpace($Exe) -or -not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
        return $null
    }

    try {
        $raw = & $Exe -v
        if ($LASTEXITCODE -ne 0) {
            return $null
        }

        $text = "$raw".Trim()
        if ($text -match '^v?(\d+\.\d+\.\d+)') {
            return $Matches[1]
        }

        return $null
    }
    catch {
        return $null
    }
}

function Copy-NodeRuntime([string] $SourceExe) {
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath $SourceExe -Destination (Join-Path $Destination "node.exe")
    Set-Content -LiteralPath (Join-Path $Destination "VERSION") -Value $Version -NoNewline
}

function Get-PreferredNodeCandidates {
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($candidate in @($PreferredNodeExe, $env:MYTOOLS_PREFERRED_NODE_EXE)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $candidates.Add($candidate.Trim())
        }
    }

    $command = Get-Command node -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $source = $command.Source
        if (-not [string]::IsNullOrWhiteSpace($source)) {
            $candidates.Add($source)
        }
    }

    return $candidates | Select-Object -Unique
}

$Version = $Version.TrimStart("v")
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

$matchedExe = $null
foreach ($candidate in Get-PreferredNodeCandidates) {
    $foundVersion = Get-NodeFileVersion $candidate
    if ($foundVersion -eq $Version) {
        $matchedExe = (Resolve-Path -LiteralPath $candidate).Path
        Write-Host "Using existing Node v$Version at $matchedExe"
        break
    }

    if ($null -eq $foundVersion) {
        Write-Host "Skipping Node candidate (unreadable): $candidate"
    }
    else {
        Write-Host "Skipping Node candidate v$foundVersion (want v$Version): $candidate"
    }
}

if ($null -ne $matchedExe) {
    Copy-NodeRuntime $matchedExe
}
else {
    $zipName = "node-v$Version-win-x64.zip"
    $downloadUri = "https://nodejs.org/dist/v$Version/$zipName"
    $cacheZip = Join-Path $CacheDirectory $zipName
    $expected = $Sha256.ToLowerInvariant()

    if (Test-Path $cacheZip -PathType Leaf) {
        $actual = Get-Sha256Hex $cacheZip
        if ($actual -ne $expected) {
            Write-Host "Cached Node zip hash mismatch; downloading again."
            Remove-Item -Force $cacheZip
        }
    }

    if (-not (Test-Path $cacheZip -PathType Leaf)) {
        Write-Host "No matching local Node v$Version; downloading $downloadUri"
        Invoke-WebRequest -Uri $downloadUri -OutFile $cacheZip -UseBasicParsing
    }

    $actual = Get-Sha256Hex $cacheZip
    if ($actual -ne $expected) {
        throw "Node zip SHA256 mismatch for $zipName. Expected $expected, got $actual."
    }

    $extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mytools-node-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($cacheZip, $extractRoot)
        $extractedNode = Get-ChildItem -Path $extractRoot -Recurse -Filter "node.exe" |
            Where-Object { $_.Directory.Name -like "node-v$Version-win-x64" } |
            Select-Object -First 1
        if ($null -eq $extractedNode) {
            $extractedNode = Get-ChildItem -Path $extractRoot -Recurse -Filter "node.exe" | Select-Object -First 1
        }
        if ($null -eq $extractedNode) {
            throw "node.exe was not found inside $zipName."
        }

        Copy-NodeRuntime $extractedNode.FullName
    }
    finally {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$bundled = Join-Path $Destination "node.exe"
if (-not (Test-Path $bundled -PathType Leaf)) {
    throw "Failed to bundle Node runtime to $bundled"
}

$bundledVersion = Get-NodeFileVersion $bundled
if ($bundledVersion -ne $Version) {
    throw "Bundled node.exe is v$bundledVersion, expected v$Version."
}

Write-Host "Bundled Node v$Version to $bundled"
