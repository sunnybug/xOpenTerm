# 功能说明：构建 xOpenTerm 项目，默认 Debug，传参 --release 时构建 Release

param(
    [switch]$Release
)

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

Write-Host "构建配置: $Config" -ForegroundColor Cyan
dotnet build $ProjectPath -c $Config
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "构建完成." -ForegroundColor Green
