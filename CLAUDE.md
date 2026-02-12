# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 构建与运行

```powershell
# 编译并运行 Debug（常用）
.\test.ps1

# 仅构建
.\script\build.ps1           # Debug
.\script\build.ps1 --release # Release

# 编译并运行 Release
.\test.ps1 --release

# 初始化开发环境（首次克隆项目后）
.\script\init_dev.ps1

# 发布到 dist 目录（带版本号）
.\script\publish.ps1
```

**注意**：
- 构建输出目录为 `.temp/bin/`，中间文件为 `.temp/obj/`
- test.ps1 启动时**工作路径为 .run**，配置文件从 工作路径\config（即 `.run/config/`）读取，日志在 `.run/log/`
- test.ps1 会自动强杀现有 xOpenTerm 进程、清除日志后再启动
- crash log ：log/YYYY-MM-DD_crash.log
## 项目架构

### 目录结构
- `src/` — 源码（WPF XAML + C#）
- `script/` — 构建脚本（build.ps1, publish.ps1, init_dev.ps1）
- `bin/` — 工作目录（配置、日志、临时覆盖配置）
- `.run/` — 运行时工作目录（与 bin 类似，作为运行时配置路径）
- `.temp/` — 编译输出目录
- `dist/` — 发布目录
- `doc/`、`aidoc/` — 文档目录

### 核心代码组织

**Models/** — 数据模型
- `Node.cs` — 节点树模型（分组/SSH/本地终端/RDP/云同步节点）
- `ConnectionConfig.cs` — 连接配置（SSH/RDP/云 API 密钥等）
- `Credential.cs` — 登录凭证模型
- `Tunnel.cs`、`TunnelHop.cs` — 隧道/跳板机模型
- `AppSettings.cs` — 应用设置（窗口状态、树展开状态等）

**Services/** — 核心服务层
- `StorageService.cs` — YAML 持久化（节点/凭证/隧道/设置），自动加密/解密敏感字段
- `SecretService.cs` — 密码加密服务，支持主密码（xot4）和固定密钥（xot2/xot3）
- `MasterPasswordService.cs` — 主密码管理（DPAPI 存储派生密钥）
- `ConfigBackupService.cs` — 配置自动备份（60 秒防抖，备份到 `%LocalAppData%\xOpenTerm\backup\`）
- `SessionManager.cs` — SSH 会话管理
- `TencentCloudService.cs`、`AliCloudService.cs`、`KingsoftCloudService.cs` — 云平台 API 同步服务
- `SshTester.cs` — SSH 连接测试工具
- `RdpLauncher.cs` — RDP 启动服务（生成 .rdp 文件、可选 cmdkey 写入凭据）

**Controls/** — 自定义控件
- `TerminalControl.xaml.cs` — 终端控件容器
- `TerminalSurface.cs` — 自绘 VT100 终端表面（ANSI 颜色、仅绘制可见行）
- `TerminalBuffer.cs` — 终端缓冲区
- `Vt100Parser.cs` — VT100 转义序列解析器
- `SshPuttyHostControl.cs` — SSH 连接托管控件（PuTTY 集成）
- `RdpHostControl.cs` — RDP 连接托管控件
- `SshStatusBarControl.xaml.cs` — SSH 状态栏（CPU/内存/网络）

**MainWindow.cs** — 主窗口（分文件 partial class）
- `MainWindow.ServerTree.cs` — 服务器树操作（拖拽、右键菜单、多选）
- `MainWindow.Tabs.cs` — 标签页管理（SSH/RDP/本地终端）
- `MainWindow.RemoteFile.cs` — 远程文件面板（已废弃但代码仍存在）

**Window/*EditWindow.xaml.cs** — 各种节点编辑窗口
- `GroupNodeEditWindow` — 分组节点
- `SshNodeEditWindow` — SSH 节点
- `LocalNodeEditWindow` — 本地终端节点
- `RdpNodeEditWindow` — RDP 节点
- `TencentCloudNodeEditWindow`、`AliCloudNodeEditWindow`、`KingsoftCloudNodeEditWindow` — 云同步分组节点
- `TunnelEditWindow` — 隧道编辑
- `CredentialEditWindow` — 凭证编辑

### 配置系统

**配置目录解析顺序**（StorageService.GetConfigDir）：
1. `.run/config/`（运行时配置）
2. `config/`（工作目录配置）
3. `<exe所在目录>/config/`（默认配置）

**加密机制**（SecretService）：
- `xot1:` — 旧版 DPAPI 加密（仅限本机当前用户）
- `xot2:` — 版本 1 AES 加密（固定密钥，跨机器兼容）
- `xot3:` — 版本 2 AES 加密（固定密钥，跨机器兼容）
- `xot4:` — 主密码派生密钥加密（需要用户输入主密码）

**密码字段**（自动加密/解密）：
- `ConnectionConfig`: Password, KeyPassphrase, TencentSecretId/Key, AliAccessKeyId/Secret, KsyunAccessKeyId/Secret
- `Credential`: Password, KeyPassphrase
- `Tunnel`, `TunnelHop`: Password, KeyPassphrase

**导出功能**：导出 YAML 时所有敏感字段为**解密后的明文**，便于迁移。

### 全局类型别名（GlobalUsings.cs）

消除 WPF 与 WinForms 同名类型歧义，全局优先使用 WPF 类型：
- `Application = System.Windows.Application`
- `MessageBox = System.Windows.MessageBox`
- `Button = System.Windows.Controls.Button`
- 等等...

### 云同步服务架构

所有云同步服务（腾讯云、阿里云、金山云）遵循统一模式：
1. API 调用封装为独立的 `*CloudService` 类
2. 多地域并行拉取（使用 Task.WhenAll）
3. 按地域→服务器的层级构建节点树
4. 支持增量更新（不删除已存在的手动配置节点）
5. 同步窗口：`*CloudSyncWindow.xaml.cs`
6. 分组添加窗口：`*CloudGroupAddWindow.xaml.cs`
7. 节点编辑窗口：`*CloudNodeEditWindow.xaml.cs`

## 开发注意事项

### PowerShell 脚本规范
- 编码必须为 **UTF-8 with BOM**
- 首行必须包含功能说明注释（中文）
- 使用 `\` 路径分隔符时需用 `Join-Path` 或 `Split-Path`
- 错误处理：使用 `$ErrorActionPreference = "Stop"` 和 `trap` 捕获异常

### 日志系统
- 日志位置：`.run/log/`
- 文件格式：`YYYY-MM-DD.log`（常规）、`YYYY-MM-DD_crash.log`（崩溃）
- 日志级别：DEBUG/INFO/WARN/ERR/FATAL
- 使用 `ExceptionLog.Write(ex, "上下文")` 记录异常

### 版本管理
- 版本号定义在 `src/xOpenTerm.csproj` 的 `<Version>` 节点
- 发布时自动从 csproj 读取版本并创建 `dist/xOpenTerm-v<版本>/` 目录

### 不使用的功能（代码仍存在）
- 远程文件面板（`MainWindow.RemoteFile.cs`）已废弃但代码保留

### WPF 资源
- MaterialDesignThemes 主题
- 图标：`src/icons/icon.ico`

### 禁止
- 不允许使用写死的本地路径（必须使用相对路径或从配置/环境变量获取）
- 新增或修改功能时必须同步更新 README.md
