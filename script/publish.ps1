# 功能说明：将 Release（带版本号）发布到 dist 目录

param()

$ErrorActionPreference = "Stop"
trap {
    Write-Host "命令行被中止: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    break
}

$Root = Join-Path $PSScriptRoot ".."
$ProjectPath = Join-Path $Root "src\xOpenTerm.csproj"
$DistDir = Join-Path $Root ".dist"

# 构建 Release
& (Join-Path $PSScriptRoot "build.ps1") -Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 从 csproj 读取版本
$Version = (Select-String -Path $ProjectPath -Pattern '<Version>(.+?)</Version>').Matches.Groups[1].Value.Trim()
if (-not $Version) { $Version = "0.0.0" }

$ReleaseName = "xOpenTerm-v$Version"
$ReleaseDir = Join-Path $DistDir $ReleaseName
# 与 build.ps1 一致：Release 输出到 .temp\Release
$BinRelease = Join-Path $Root ".temp\Release"

if (-not (Test-Path $BinRelease)) {
    Write-Host "未找到 .temp\Release 输出目录" -ForegroundColor Red
    exit 1
}

if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

Copy-Item (Join-Path $BinRelease "*") -Destination $ReleaseDir -Recurse -Force
Write-Host "已发布到: $ReleaseDir" -ForegroundColor Green
