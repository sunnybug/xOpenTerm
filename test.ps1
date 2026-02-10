# 功能说明：调用 build.ps1 构建后运行 xOpenTerm 应用（支持 --release）

param(
    [switch]$Release
)

# PowerShell 不会把 --release 绑定到 -Release，显式识别
if ($args -contains "--release") { $Release = $true }

$ErrorActionPreference = "Stop"
trap {
    Write-Host "命令行被中止: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    break
}

# 保留日志目录，以便查看测试过程中的日志信息
Write-Host "保留日志目录，以便查看测试过程中的日志信息" -ForegroundColor Gray

$BuildScript = Join-Path $PSScriptRoot "script\build.ps1"
if ($Release) {
    & $BuildScript -Release
} else {
    & $BuildScript
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$Root = $PSScriptRoot
$BinDir = Join-Path $Root "var\bin"
$ProjectPath = Join-Path $Root "src\xOpenTerm.csproj"
$Config = if ($Release) { "Release" } else { "Debug" }
if (-not (Test-Path $BinDir)) { New-Item -ItemType Directory -Path $BinDir -Force | Out-Null }
Write-Host "启动应用（工作目录: $BinDir）..." -ForegroundColor Cyan
Push-Location $BinDir
try {
    dotnet run --project $ProjectPath -c $Config --no-build
} finally {
    Pop-Location
}
