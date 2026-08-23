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
    $sourceRoot = Split-Path -Parent $SourceExe
    $sourceNpmCmd = Join-Path $sourceRoot "npm.cmd"
    $sourceNpxCmd = Join-Path $sourceRoot "npx.cmd"
    $sourceNpmPackage = Join-Path $sourceRoot "node_modules\npm"
    if (-not (Test-Path -LiteralPath $sourceNpmCmd -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $sourceNpmPackage "bin\npm-cli.js") -PathType Leaf)) {
        throw "The Node runtime at $sourceRoot does not include npm."
    }
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath $SourceExe -Destination (Join-Path $Destination "node.exe")
    Copy-Item -LiteralPath $sourceNpmCmd -Destination (Join-Path $Destination "npm.cmd")
    if (Test-Path -LiteralPath $sourceNpxCmd -PathType Leaf) {
        Copy-Item -LiteralPath $sourceNpxCmd -Destination (Join-Path $Destination "npx.cmd")
    }
    New-Item -ItemType Directory -Path (Join-Path $Destination "node_modules") -Force | Out-Null
    Copy-Item -LiteralPath $sourceNpmPackage -Destination (Join-Path $Destination "node_modules\npm") -Recurse
    Set-Content -LiteralPath (Join-Path $Destination "VERSION") -Value $Version -NoNewline
}

function Test-NpmRuntimeSource([string] $NodeExe) {
    if ([string]::IsNullOrWhiteSpace($NodeExe)) { return $false }
    $root = Split-Path -Parent $NodeExe
    return (Test-Path -LiteralPath (Join-Path $root "npm.cmd") -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $root "node_modules\npm\bin\npm-cli.js") -PathType Leaf)
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
New-Item -ItemType Directory -Path $Destination -Force | Out-Null

Write-Host "=== Bundle Node runtime ==="
Write-Host "Requested version: v$Version"
Write-Host "PreferredNodeExe:  $(if ([string]::IsNullOrWhiteSpace($PreferredNodeExe)) { '(not set)' } else { $PreferredNodeExe })"
Write-Host "Env preferred exe: $(if ([string]::IsNullOrWhiteSpace($env:MYTOOLS_PREFERRED_NODE_EXE)) { '(not set)' } else { $env:MYTOOLS_PREFERRED_NODE_EXE })"
Write-Host "Cache directory:   $CacheDirectory"
Write-Host "Destination:       $Destination"

$cachedFiles = @()
if (Test-Path -LiteralPath $CacheDirectory) {
    $cachedFiles = @(Get-ChildItem -LiteralPath $CacheDirectory -Force -File -ErrorAction SilentlyContinue)
}
if ($cachedFiles.Count -eq 0) {
    Write-Host "Zip cache contents: (empty)"
} else {
    Write-Host "Zip cache contents:"
    foreach ($file in $cachedFiles) {
        Write-Host ("  {0,12} bytes  {1}" -f $file.Length, $file.Name)
    }
}

$candidates = @(Get-PreferredNodeCandidates)
if ($candidates.Count -eq 0) {
    Write-Host "Local Node candidates: (none)"
} else {
    Write-Host "Local Node candidates:"
}

$matchedExe = $null
foreach ($candidate in $candidates) {
    $foundVersion = Get-NodeFileVersion $candidate
    $label = if ($null -eq $foundVersion) { 'unreadable' } else { "v$foundVersion" }
    if ($foundVersion -eq $Version -and (Test-NpmRuntimeSource $candidate)) {
        $matchedExe = (Resolve-Path -LiteralPath $candidate).Path
        Write-Host "  MATCH $label  $candidate"
        break
    }

    $reason = if ($foundVersion -eq $Version) { "npm missing" } else { $label }
    Write-Host "  skip  $reason  $candidate"
}

if ($null -ne $matchedExe) {
    Write-Host "Using existing Node v$Version at $matchedExe (official zip cache not needed)."
    Copy-NodeRuntime $matchedExe

    # Do not leave an empty cache dir for actions/cache to save.
    if (Test-Path -LiteralPath $CacheDirectory) {
        $remaining = @(Get-ChildItem -LiteralPath $CacheDirectory -Force -File -ErrorAction SilentlyContinue)
        if ($remaining.Count -eq 0) {
            Remove-Item -LiteralPath $CacheDirectory -Recurse -Force
            Write-Host "Removed empty zip cache directory so CI will not save a blank cache."
        }
    }
}
else {
    $zipName = "node-v$Version-win-x64.zip"
    $downloadUri = "https://nodejs.org/dist/v$Version/$zipName"
    $cacheZip = Join-Path $CacheDirectory $zipName
    $expected = $Sha256.ToLowerInvariant()

    New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null

    if (Test-Path $cacheZip -PathType Leaf) {
        $actual = Get-Sha256Hex $cacheZip
        if ($actual -ne $expected) {
            Write-Host "Zip cache hit but hash mismatch ($actual); downloading again."
            Remove-Item -Force $cacheZip
        } else {
            Write-Host "Zip cache hit: $cacheZip"
        }
    }

    if (-not (Test-Path $cacheZip -PathType Leaf)) {
        Write-Host "Zip cache miss; downloading $downloadUri"
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

$bundledNpm = Join-Path $Destination "npm.cmd"
$bundledNpmCli = Join-Path $Destination "node_modules\npm\bin\npm-cli.js"
if (-not (Test-Path $bundledNpm -PathType Leaf) -or -not (Test-Path $bundledNpmCli -PathType Leaf)) {
    throw "Bundled Node runtime is missing npm."
}

$bundledVersion = Get-NodeFileVersion $bundled
if ($bundledVersion -ne $Version) {
    throw "Bundled node.exe is v$bundledVersion, expected v$Version."
}

Write-Host "Bundled Node v$Version to $bundled"
