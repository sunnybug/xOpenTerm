# 功能说明：调用 build.ps1 构建后运行 xOpenTerm 应用（支持 --release）

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

$BuildScript = Join-Path $PSScriptRoot "script\build.ps1"
$BuildArgs = @()
if ($Release) { $BuildArgs += "-Release" }

& $BuildScript @BuildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$Root = $PSScriptRoot
$ProjectPath = Join-Path $Root "src\xOpenTerm.csproj"
$Config = if ($Release) { "Release" } else { "Debug" }
Write-Host "启动应用..." -ForegroundColor Cyan
dotnet run --project $ProjectPath -c $Config --no-build
