<#
.SYNOPSIS
在推送发布标签前执行与 GitHub Actions 对齐的本地预检。

.PARAMETER Tag
待发布标签，例如 v1.4.4.2。

.PARAMETER Proxy
可选代理地址，例如 socks5h://127.0.0.1:10808。

.EXAMPLE
.\scripts\check-release.ps1 v1.4.4.2

.EXAMPLE
.\scripts\check-release.ps1 v1.4.4.2 -Proxy socks5h://127.0.0.1:10808
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidatePattern('^[vV][0-9]+(?:\.[0-9]+){1,3}$')]
    [string]$Tag,

    [string]$Proxy
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("YunXiReleaseCheck-" + [guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $tempRoot 'source'
$dotnetRoot = Join-Path $tempRoot 'dotnet'
$dotnetHome = Join-Path $tempRoot 'dotnet-home'
$nugetPackages = Join-Path $tempRoot 'nuget-packages'
$npmCache = Join-Path $tempRoot 'npm-cache'
$linuxStage = Join-Path $tempRoot 'linux-extension'
$linuxOutput = Join-Path $tempRoot 'linux-release'
$windowsOutput = Join-Path $tempRoot 'windows-release'
$linuxFiles = @('changelog.txt', 'extension.js', 'INSTALL.txt', 'main.js', 'metadata.json', 'stylesheet.css')
$tagVersion = $Tag.Substring(1)

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter()]
        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败（退出码 $LASTEXITCODE）：$FilePath $($Arguments -join ' ')"
    }
}

function Get-DotnetPath {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed) {
        $sdkVersions = & $installed.Source --list-sdks
        if ($sdkVersions | Where-Object { $_ -match '^10\.' }) {
            return $installed.Source
        }
    }

    Write-Host '未找到 .NET 10 SDK，正在安装到临时目录...'
    $installerPath = Join-Path $tempRoot 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerPath
    Invoke-Checked -FilePath 'powershell.exe' -Arguments @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $installerPath,
        '-Channel', '10.0',
        '-InstallDir', $dotnetRoot,
        '-NoPath'
    ) | Out-Host
    return Join-Path $dotnetRoot 'dotnet.exe'
}

try {
    if ($Proxy) {
        $compatibleProxy = $Proxy -replace '^socks5h://', 'socks5://'
        $env:HTTPS_PROXY = $compatibleProxy
        $env:HTTP_PROXY = $compatibleProxy
    }
    $env:DOTNET_CLI_HOME = $dotnetHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:NUGET_PACKAGES = $nugetPackages
    $env:npm_config_cache = $npmCache

    New-Item -ItemType Directory -Path $sourceRoot, $dotnetRoot, $dotnetHome,
        $nugetPackages, $npmCache, $linuxStage, $linuxOutput, $windowsOutput | Out-Null

    Write-Host '复制源码到临时目录...'
    & robocopy.exe @(
        $projectRoot,
        $sourceRoot,
        '/E',
        '/XD', '.git', 'bin', 'obj', 'publish', 'data',
        '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
    )
    if ($LASTEXITCODE -ge 8) {
        throw "复制源码失败（robocopy 退出码 $LASTEXITCODE）"
    }

    $appProjectPath = Join-Path $sourceRoot 'outputs\DesktopCompanionMonitor\PcCompanionMonitor.csproj'
    $installerProjectPath = Join-Path $sourceRoot 'outputs\InstallerSource\CloudXiInstaller.csproj'
    $linuxSource = Join-Path $sourceRoot 'outputs\DesktopCompanionMonitor.Linux'
    $linuxMainPath = Join-Path $linuxSource 'main.js'
    $windowsChangelogPath = Join-Path $sourceRoot 'outputs\DesktopCompanionMonitor\Changelog.cs'
    $linuxChangelogPath = Join-Path $linuxSource 'changelog.txt'
    $workflowPath = Join-Path $sourceRoot '.github\workflows\release.yml'

    Write-Host '校对版本和更新日志...'
    [xml]$appProject = Get-Content -Raw -LiteralPath $appProjectPath
    [xml]$installerProject = Get-Content -Raw -LiteralPath $installerProjectPath
    $appVersion = $appProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $installerVersion = $installerProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $linuxVersionMatch = [regex]::Match(
        (Get-Content -Raw -Encoding UTF8 -LiteralPath $linuxMainPath),
        "(?m)^const APP_VERSION = '([^']+)';$")
    if (-not $linuxVersionMatch.Success) {
        throw '无法读取 Linux APP_VERSION'
    }
    $linuxVersion = $linuxVersionMatch.Groups[1].Value
    if ($appVersion -ne $tagVersion -or $installerVersion -ne $tagVersion -or $linuxVersion -ne $tagVersion) {
        throw "标签版本 $tagVersion 与项目版本不一致：Windows=$appVersion，安装程序=$installerVersion，Linux=$linuxVersion"
    }

    $windowsLines = foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $windowsChangelogPath) {
        if ($line -match '^\s*"(?<text>(?:[^"\\]|\\.)*)",?\s*$') {
            [regex]::Unescape($Matches.text)
        }
    }
    $windowsChangelog = ($windowsLines -join "`n").TrimEnd()
    $linuxChangelog = (Get-Content -Raw -Encoding UTF8 -LiteralPath $linuxChangelogPath).
        Replace("`r`n", "`n").TrimEnd()
    if ($windowsChangelog -cne $linuxChangelog) {
        throw 'Windows Changelog.cs 与 Linux changelog.txt 内容不一致'
    }
    $changelogMatch = [regex]::Match($windowsChangelog, '(?m)^版本\s+([0-9.]+)（')
    if (-not $changelogMatch.Success -or $changelogMatch.Groups[1].Value -ne $tagVersion) {
        throw "标签版本 $tagVersion 与更新日志版本不一致"
    }

    Write-Host '检查 Linux 扩展和发布工作流...'
    foreach ($file in $linuxFiles) {
        $path = Join-Path $linuxSource $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Linux 发布文件不存在：$file"
        }
        Copy-Item -LiteralPath $path -Destination (Join-Path $linuxStage $file)
    }
    Invoke-Checked -FilePath 'node.exe' -Arguments @('--check', (Join-Path $linuxStage 'extension.js'))
    Invoke-Checked -FilePath 'node.exe' -Arguments @('--check', (Join-Path $linuxStage 'main.js'))
    $null = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $linuxStage 'metadata.json') |
        ConvertFrom-Json
    Invoke-Checked -FilePath 'npx.cmd' -Arguments @('--yes', 'yaml-lint', $workflowPath)

    $linuxZipPath = Join-Path $linuxOutput 'YunXiStatistician-Linux-GNOME.zip'
    Compress-Archive -LiteralPath ($linuxFiles | ForEach-Object { Join-Path $linuxStage $_ }) `
        -DestinationPath $linuxZipPath -CompressionLevel Optimal
    $archive = [IO.Compression.ZipFile]::OpenRead($linuxZipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName | Sort-Object)
    }
    finally {
        $archive.Dispose()
    }
    $expectedEntries = @($linuxFiles | Sort-Object)
    if (($entries -join "`n") -cne ($expectedEntries -join "`n")) {
        throw "Linux ZIP 文件清单不正确：$($entries -join ', ')"
    }

    Write-Host '还原并发布 Windows 安装程序...'
    $dotnet = Get-DotnetPath
    Invoke-Checked -FilePath $dotnet -Arguments @('restore', '-r', 'win-x64', $appProjectPath)
    Invoke-Checked -FilePath $dotnet -Arguments @('restore', '-r', 'win-x64', $installerProjectPath)
    Invoke-Checked -FilePath $dotnet -Arguments @(
        'publish',
        $installerProjectPath,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-o', $windowsOutput
    )
    $windowsAsset = Join-Path $windowsOutput 'YunXiStatistician.exe'
    if (-not (Test-Path -LiteralPath $windowsAsset -PathType Leaf)) {
        throw '未生成 YunXiStatistician.exe'
    }
    $embeddedApplication = Join-Path $sourceRoot `
        'outputs\InstallerSource\obj\embedded\PcCompanionMonitor.exe'
    if (-not (Test-Path -LiteralPath $embeddedApplication -PathType Leaf)) {
        throw '安装程序中未生成内嵌主程序'
    }
    foreach ($versionedFile in @($windowsAsset, $embeddedApplication)) {
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($versionedFile)
        if ($versionInfo.FileVersion -ne $tagVersion -or
            $versionInfo.ProductVersion -ne $tagVersion) {
            throw "Windows 成品版本信息不正确：$versionedFile，FileVersion=$($versionInfo.FileVersion)，ProductVersion=$($versionInfo.ProductVersion)"
        }
    }

    Write-Host "发布预检通过：$Tag"
    Write-Host "Windows：$((Get-Item -LiteralPath $windowsAsset).Length) 字节"
    Write-Host "Linux：$((Get-Item -LiteralPath $linuxZipPath).Length) 字节"
}
finally {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ExecutablePath -and
            $_.ExecutablePath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $tempRoot) {
        Get-ChildItem -File -Recurse -Force -LiteralPath $tempRoot -ErrorAction SilentlyContinue |
            ForEach-Object { [IO.File]::SetAttributes($_.FullName, [IO.FileAttributes]::Normal) }
        Start-Sleep -Milliseconds 300
        try {
            [IO.Directory]::Delete($tempRoot, $true)
        }
        catch {
            Start-Sleep -Seconds 2
            [IO.Directory]::Delete($tempRoot, $true)
        }
    }
}
