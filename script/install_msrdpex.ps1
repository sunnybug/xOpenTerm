# 功能说明：从 GitHub Releases 下载并静默安装 MsRdpEx（内嵌 RDP 依赖），解决「MsRdpEx 接口不支持」时使用。
# 需管理员权限；安装后组件位于 %ProgramFiles%\Devolutions\MsRdpEx

$ErrorActionPreference = "Stop"
trap {
    Write-Host "错误: $_" -ForegroundColor Red
    exit 1
}

# 检查管理员
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "请以管理员身份运行此脚本，或在 PowerShell 中执行：" -ForegroundColor Yellow
    Write-Host "  Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"'"
    exit 1
}

$api = "https://api.github.com/repos/Devolutions/MsRdpEx/releases/latest"
Write-Host "正在获取最新版本..."
$release = Invoke-RestMethod -Uri $api -Headers @{ Accept = "application/vnd.github.v3+json" }
$msi = $release.assets | Where-Object { $_.name -match '\.msi$' } | Select-Object -First 1
if (-not $msi) {
    Write-Host "未找到 MSI 包，请手动从 https://github.com/Devolutions/MsRdpEx/releases 下载并安装。"
    exit 1
}

$tempDir = [System.IO.Path]::GetTempPath()
$msiPath = Join-Path $tempDir $msi.name
Write-Host "正在下载: $($msi.name) ..."
Invoke-WebRequest -Uri $msi.browser_download_url -OutFile $msiPath -UseBasicParsing

Write-Host "正在静默安装..."
$p = Start-Process -FilePath "msiexec.exe" -ArgumentList "/i", "`"$msiPath`"", "/qn", "/norestart" -Wait -PassThru
Remove-Item -Path $msiPath -Force -ErrorAction SilentlyContinue
if ($p.ExitCode -ne 0) {
    Write-Host "安装退出码: $($p.ExitCode)。可尝试手动运行: msiexec /i `"<下载的.msi路径>`" /qn"
    exit $p.ExitCode
}
Write-Host "MsRdpEx 安装完成。安装路径: $env:ProgramFiles\Devolutions\MsRdpEx" -ForegroundColor Green
