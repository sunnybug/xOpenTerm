# 端口扫描测试脚本 - 测试本地和公网 IP 的端口扫描功能
# 编码: UTF-8 with BOM

param(
    [string]$Host = "149.129.223.30",
    [int[]]$Ports = @(22, 80, 443, 3306, 3389, 65432),
    [int]$Timeout = 3000
)

Write-Host "=== 端口扫描测试 ===" -ForegroundColor Cyan
Write-Host "目标主机: $Host"
Write-Host "测试端口: $($Ports -join ', ')"
Write-Host "超时时间: ${Timeout}ms"
Write-Host ""

# 运行端口扫描单元测试
Write-Host "运行端口扫描单元测试..." -ForegroundColor Yellow
dotnet test --filter "FullyQualifiedName~PortScanTests" --logger "console;verbosity=quiet"

if ($LASTEXITCODE -ne 0) {
    Write-Host "单元测试失败！" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "单元测试完成！" -ForegroundColor Green
Write-Host ""

# 特别测试公网 IP 的端口 80
Write-Host "特别测试: $Host`:80 (HTTP)" -ForegroundColor Cyan
dotnet test --filter "FullyQualifiedName~LocalPortScan_PublicIP_CanDetectOpenPort" --logger "console;verbosity=detailed"

Write-Host ""
Write-Host "=== 测试完成 ===" -ForegroundColor Green
Write-Host "请查看 .run/log/ 目录中的日志文件获取详细信息"
