# CLAUDE.md

## 构建与运行

```powershell
# 编译并运行 Debug（常用）
.\0run.ps1

# 仅构建
.\script\build.ps1           # Debug
.\script\build.ps1 --release # Release

# 编译并运行 Release
.\0run.ps1 --release

# 仅运行 SSH 状态获取单元测试（无 UI，自动退出）
.\0run.ps1 --test-ssh-status

# 连接测试：遍历名为 test 的节点下所有子节点进行连接，结果输出到命令行并自动退出
.\0run.ps1 --test-connect

# 运行所有单元测试
dotnet test

# 初始化开发环境（首次克隆项目后）
.\script\init_dev.ps1

# 发布到 .dist 目录（带版本号）
.\script\publish.ps1
```

### 工作目录说明
- **0run.ps1 运行时工作目录为 `.run/`**，配置文件从 `.run/config/` 读取，日志写入 `.run/log/`

### 构建输出
- 构建输出目录为 `.temp/bin/`，中间文件为 `.temp/obj/`
- crash log 格式：`log/YYYY-MM-DD_crash.log`
## 项目架构