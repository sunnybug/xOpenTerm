# 功能说明：构建 xOpenTerm 项目，默认 Debug，传参 --release 时构建 Release

param(
    [switch]$Release
)

if ($args -contains "--release") { $Release = $true }

$ErrorActionPreference = "Stop"
trap {
    Write-Host "命令行被中止: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    break
}

$Root = Join-Path $PSScriptRoot ".."
$ProjectPath = Join-Path $Root "src\xOpenTerm.csproj"
$Config = if ($Release) { "Release" } else { "Debug" }
$TempDir = Join-Path $Root ".temp"
$OutputDir = Join-Path $TempDir "$Config"
$IntermediateDir = Join-Path $TempDir "obj"

# 确保中间目录路径以尾部斜杠结尾
if (-not $IntermediateDir.EndsWith('\')) {
    $IntermediateDir += '\'
}

# 确保临时目录存在
if (! (Test-Path -Path $TempDir -PathType Container)) {
    New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
}

Write-Host "构建配置: $Config" -ForegroundColor Cyan
Write-Host "输出目录: $OutputDir" -ForegroundColor Cyan
Write-Host "中间目录: $IntermediateDir" -ForegroundColor Cyan

dotnet build $ProjectPath -c $Config -o $OutputDir -p:IntermediateOutputPath=$IntermediateDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "构建完成." -ForegroundColor Green
