<#
.SYNOPSIS
    以交互方式发布 MyTools。直接运行 .\publish.ps1 即可按默认规则将 patch 版本加一。

.DESCRIPTION
    如需绕过本脚本手动发布，可使用下面的 dotnet publish 命令。

    1. 普通 .NET 发布（只生成应用目录，不调用 Velopack）：

       dotnet publish .\MyTools.Desktop\MyTools.Desktop.csproj `
           --configuration Release

    2. 手动生成正式 Velopack 发布包：

       dotnet publish .\MyTools.Desktop\MyTools.Desktop.csproj `
           --configuration Release `
           --runtime win-x64 `
           --property:SelfContained=true `
           --property:CreateVelopackRelease=true `
           --property:Version=1.0.2 `
           --property:VelopackChannel=stable

    主要参数：
      --configuration Release
          使用 Release 配置构建。

      --runtime win-x64
          发布目标运行时。启用 CreateVelopackRelease 且未指定时，默认也是 win-x64。

      --property:SelfContained=true|false
          true 表示发布包自带 .NET Runtime；false 表示目标电脑必须安装 .NET Desktop Runtime。
          启用 CreateVelopackRelease 且未指定时，默认值为 false。

      --property:CreateVelopackRelease=true
          显式启用项目中的 Velopack MSBuild Target。没有该参数时只执行普通 dotnet publish，
          不会生成 Setup.exe、完整包、增量包和更新清单。

      --property:Version=<SemVer>
          指定本次发布版本，例如 1.0.2 或 1.0.2-beta.1。未指定时读取 version.txt，
          但手动 dotnet publish 不会自动执行 patch + 1。

      --property:VelopackChannel=<channel>
          指定更新通道，例如 stable 或 beta；默认值为 stable。客户端的 UpdateChannel 必须与之匹配。

    可选 MSBuild 属性：
      --property:VelopackDeltaMode=BestSpeed
          指定增量包模式，默认值为 BestSpeed。

      --property:VelopackReleaseDirectory=<path>
          覆盖 Velopack 产物目录，默认是仓库根目录的 Releases。不要删除其中的旧完整包，
          它们用于生成后续 delta 包。

      --property:VelopackPublishDirectory=<path>
          覆盖 VPK 输入的临时 publish 目录。

    重要：手动 dotnet publish 不会更新 version.txt。确认完整发布成功后，应把 version.txt
    手动改成本次发布版本；否则下次运行 .\publish.ps1 时会从旧版本计算默认版本。
#>

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$projectPath = Join-Path $root "MyTools.Desktop\MyTools.Desktop.csproj"
$versionFile = Join-Path $root "version.txt"
$semanticVersionPattern = '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Get-NextPatchVersion {
    param([Parameter(Mandatory = $true)][string]$CurrentVersion)

    $match = [regex]::Match($CurrentVersion, $semanticVersionPattern)
    if (-not $match.Success) {
        throw "version.txt contains an invalid semantic version: '$CurrentVersion'."
    }

    $patch = [System.Numerics.BigInteger]::Parse($match.Groups["patch"].Value)
    return "{0}.{1}.{2}" -f `
        $match.Groups["major"].Value, `
        $match.Groups["minor"].Value, `
        ($patch + [System.Numerics.BigInteger]::One)
}

function Read-Version {
    param([Parameter(Mandatory = $true)][string]$DefaultVersion)

    while ($true) {
        $value = Read-Host "发布版本 [$DefaultVersion]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $DefaultVersion
        }

        $value = $value.Trim()
        if ([regex]::IsMatch($value, $semanticVersionPattern)) {
            return $value
        }

        Write-Warning "版本必须是有效的 SemVer，例如 1.0.2 或 1.0.2-beta.1。"
    }
}

function Read-Channel {
    param([Parameter(Mandatory = $true)][string]$DefaultChannel)

    while ($true) {
        $value = Read-Host "更新通道 [$DefaultChannel]"
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $DefaultChannel
        }

        $value = $value.Trim()
        if ($value -match '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
            return $value
        }

        Write-Warning "更新通道只能包含字母、数字、点、下划线和连字符。"
    }
}

function Read-YesNo {
    param(
        [Parameter(Mandatory = $true)][string]$Prompt,
        [bool]$Default = $false
    )

    $suffix = if ($Default) { "[Y/n]" } else { "[y/N]" }
    while ($true) {
        $value = (Read-Host "$Prompt $suffix").Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($value)) {
            return $Default
        }

        if ($value -in @("y", "yes")) {
            return $true
        }

        if ($value -in @("n", "no")) {
            return $false
        }

        Write-Warning "请输入 y 或 n；直接按 Enter 使用默认值。"
    }
}

if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Version file was not found: '$versionFile'."
}

$currentVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
$releaseVersion = Get-NextPatchVersion -CurrentVersion $currentVersion
$channel = "stable"
$frameworkDependent = $true
$selfContained = if ($frameworkDependent) { "false" } else { "true" }

Write-Host "MyTools 交互式发布"
Write-Host "最近发布版本: $currentVersion"
Write-Host "默认发布版本: $releaseVersion（patch + 1）"
Write-Host "默认设置: channel=$channel, runtime=win-x64, self-contained=$selfContained"

if (Read-YesNo -Prompt "是否修改默认发布设置？") {
    $releaseVersion = Read-Version -DefaultVersion $releaseVersion
    $channel = Read-Channel -DefaultChannel $channel
    $frameworkDependent = Read-YesNo -Prompt "是否发布为 framework-dependent（目标机需要安装 .NET Desktop Runtime）？"
}


$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--property:SelfContained=$selfContained",
    "--property:CreateVelopackRelease=true",
    "--property:VelopackChannel=$channel",
    "--property:Version=$releaseVersion"
)

Write-Host ""
Write-Host "开始发布 MyTools $releaseVersion（channel=$channel, self-contained=$selfContained）..."
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$temporaryVersionFile = "$versionFile.tmp"
$backupVersionFile = "$versionFile.bak"
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
try {
    Remove-Item -LiteralPath $temporaryVersionFile, $backupVersionFile -Force -ErrorAction SilentlyContinue
    [System.IO.File]::WriteAllText(
        $temporaryVersionFile,
        "$releaseVersion$([Environment]::NewLine)",
        $utf8WithoutBom)
    [System.IO.File]::Replace($temporaryVersionFile, $versionFile, $backupVersionFile)
    Remove-Item -LiteralPath $backupVersionFile -Force -ErrorAction SilentlyContinue
}
finally {
    Remove-Item -LiteralPath $temporaryVersionFile -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "发布成功。version.txt 已从 $currentVersion 更新为 $releaseVersion。"
Write-Host "Velopack 产物: $(Join-Path $root 'Releases')"
