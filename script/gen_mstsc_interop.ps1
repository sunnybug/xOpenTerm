# 功能说明：使用 aximp 从系统 mstscax.dll 生成 MSTSCLib 互操作程序集，供参考 mRemoteNG 使用系统 RDP 控件时引用。
# 需已安装 Visual Studio 或 Windows SDK（含 aximp.exe）。生成后请将 AxInterop.MSTSCLib.dll、Interop.MSTSCLib.dll 放入 src/References 并在 csproj 中引用。

$ErrorActionPreference = "Stop"
trap { Write-Host "错误: $_" -ForegroundColor Red; exit 1 }

$mstscax = Join-Path $env:SystemRoot "System32\mstscax.dll"
if (-not (Test-Path -LiteralPath $mstscax)) {
    Write-Host "未找到 mstscax.dll: $mstscax" -ForegroundColor Red
    exit 1
}

$aximpPaths = @(
    "C:\Program Files\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\aximp.exe",
    "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\aximp.exe",
    "C:\Program Files\Microsoft SDKs\Windows\v7.0A\Bin\aximp.exe"
)
$aximp = $null
foreach ($p in $aximpPaths) {
    if (Test-Path -LiteralPath $p) { $aximp = $p; break }
}
if (-not $aximp) {
    Write-Host "未找到 aximp.exe，请安装 Visual Studio 或 Windows SDK。" -ForegroundColor Yellow
    Write-Host "可手动在「开发人员命令提示」中执行: aximp $mstscax" -ForegroundColor Yellow
    exit 1
}

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "src\References"
if (-not (Test-Path -PathType Container $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
Push-Location $outDir
try {
    & $aximp $mstscax
    if ($LASTEXITCODE -ne 0) { throw "aximp 退出码: $LASTEXITCODE" }
    Get-ChildItem -Filter "*.dll" | ForEach-Object { Write-Host "已生成: $($_.FullName)" -ForegroundColor Green }
}
finally { Pop-Location }
