# 测试端口预设管理功能
# 功能说明：编译并运行 xOpenTerm，手动测试端口预设管理窗口

Write-Host "开始测试端口预设管理功能..." -ForegroundColor Green

# 编译项目
Write-Host "编译项目..." -ForegroundColor Yellow
dotnet build src/xOpenTerm.csproj -c Debug -o .temp/bin/Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败" -ForegroundColor Red
    exit 1
}

Write-Host "编译成功" -ForegroundColor Green
Write-Host ""
Write-Host "请手动测试以下功能：" -ForegroundColor Cyan
Write-Host "1. 启动应用后，右键点击任意 SSH/RDP 节点 → 维护 → 端口扫描" -ForegroundColor White
Write-Host "2. 点击「管理预设...」按钮，打开预设管理窗口" -ForegroundColor White
Write-Host "3. 测试添加新预设：" -ForegroundColor White
Write-Host "   - 点击「添加」按钮" -ForegroundColor White
Write-Host "   - 输入预设名称（如「测试预设」）" -ForegroundColor White
Write-Host "   - 输入端口列表（如「22,80,443」）" -ForegroundColor White
Write-Host "   - 点击「保存」按钮" -ForegroundColor White
Write-Host "   - 验证 DataGrid 中显示新预设" -ForegroundColor White
Write-Host "4. 测试编辑预设：" -ForegroundColor White
Write-Host "   - 在 DataGrid 中选中一个预设" -ForegroundColor White
Write-Host "   - 修改名称或端口列表" -ForegroundColor White
Write-Host "   - 点击「保存」按钮" -ForegroundColor White
Write-Host "   - 验证 DataGrid 中更新成功" -ForegroundColor White
Write-Host "5. 测试删除预设：" -ForegroundColor White
Write-Host "   - 在 DataGrid 中选中一个预设" -ForegroundColor White
Write-Host "   - 点击「删除」按钮" -ForegroundColor White
Write-Host "   - 确认删除对话框" -ForegroundColor White
Write-Host "   - 验证 DataGrid 中移除该预设" -ForegroundColor White
Write-Host "6. 测试主窗口联动：" -ForegroundColor White
Write-Host "   - 关闭预设管理窗口" -ForegroundColor White
Write-Host "   - 验证主窗口下拉框已更新" -ForegroundColor White
Write-Host "   - 新增的预设应出现在下拉框中" -ForegroundColor White
Write-Host "7. 测试输入验证：" -ForegroundColor White
Write-Host "   - 尝试保存空名称（应提示错误）" -ForegroundColor White
Write-Host "   - 尝试保存空端口列表（应提示错误）" -ForegroundColor White
Write-Host "   - 尝试保存重复名称（应提示错误）" -ForegroundColor White
Write-Host "   - 尝试保存无效端口格式（应提示错误）" -ForegroundColor White
Write-Host ""
Write-Host "8. 测试数据持久化：" -ForegroundColor White
Write-Host "   - 关闭应用" -ForegroundColor White
Write-Host "   - 检查 .run\config\settings.yaml 中的 portScanSettings.portPresets" -ForegroundColor White
Write-Host "   - 重新启动应用" -ForegroundColor White
Write-Host "   - 验证自定义预设仍然存在" -ForegroundColor White
Write-Host ""
Write-Host "按任意键启动应用..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# 启动应用
.\0run.ps1
