# 功能说明：初始化开发环境（还原依赖、创建 bin/config 等目录）

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

Write-Host "还原 NuGet 包..." -ForegroundColor Cyan
dotnet restore $ProjectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$Dirs = @(
    (Join-Path $Root "bin\config"),
    (Join-Path $Root "bin\log"),
    (Join-Path $Root "bin\var\config")
)
foreach ($d in $Dirs) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        Write-Host "已创建: $d" -ForegroundColor Green
    }
}
Write-Host "开发环境初始化完成." -ForegroundColor Green
