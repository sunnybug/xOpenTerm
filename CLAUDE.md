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

# 仅运行 SSH 状态获取单元测试（无 UI，自动退出）
.\test.ps1 --test-ssh-status

# 运行所有单元测试
dotnet test

# 初始化开发环境（首次克隆项目后）
.\script\init_dev.ps1

# 发布到 .dist 目录（带版本号）
.\script\publish.ps1
```

### 工作目录说明
- **test.ps1 运行时工作目录为 `.run/`**，配置文件从 `.run/config/` 读取，日志写入 `.run/log/`
- **单元测试**通过 `GlobalRunDirectorySetup.cs`（NUnit SetUpFixture）自动将工作目录设为 `.run/`
- test.ps1 会自动强杀现有 xOpenTerm 进程、清除日志后再启动

### 依赖说明
- **WebView2 Runtime**：SSH 终端依赖，Windows 10/11 多数已预装，若未安装会提示或从 [Microsoft 官网](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) 安装
- **RDP 互操作程序集**：若从源码构建且 `src/References/` 下尚无 `AxInterop.MSTSCLib.dll`、`Interop.MSTSCLib.dll`，运行 `.\script\gen_mstsc_interop.ps1` 生成（需 Visual Studio 或 Windows SDK）

### 构建输出
- 构建输出目录为 `.temp/bin/`，中间文件为 `.temp/obj/`
- crash log 格式：`log/YYYY-MM-DD_crash.log`
## 项目架构

### 目录结构
- `src/` — 源码（WPF XAML + C#）
- `script/` — 构建脚本（build.ps1, publish.ps1, init_dev.ps1）
- `bin/` — 工作目录（配置、日志、临时覆盖配置）
- `.run/` — test.ps1 运行时工作目录（.run\config、.run\log）
- `.temp/` — 编译输出目录
- `.dist/` — 发布目录
- `doc/`、`aidoc/` — 文档目录

### 核心代码组织

**Models/** — 数据模型
- `Node.cs` — 节点树模型（分组/SSH/RDP/云同步节点）
- `ConnectionConfig.cs` — 连接配置（SSH/RDP/云 API 密钥等）
- `Credential.cs` — 登录凭证模型
- `Tunnel.cs`、`TunnelHop.cs` — 隧道/跳板机模型
- `AppSettings.cs` — 应用设置（窗口状态、树展开状态等）

**Services/** — 核心服务层
- `IStorageService.cs` — 存储抽象接口；`StorageService.cs` 实现 YAML 持久化（节点/凭证/隧道/设置），自动加密/解密敏感字段
- `INodeEditContext.cs`、`NodeEditContext.cs` — 编辑上下文接口与实现，供节点/凭证/隧道编辑窗口统一入参并委托保存
- `SecretService.cs` — 密码加密服务，支持主密码（xot4）和固定密钥（xot2/xot3）
- `MasterPasswordService.cs` — 主密码管理（DPAPI 存储派生密钥）
- `ConfigBackupService.cs` — 配置自动备份（60 秒防抖，备份到 `%LocalAppData%\xOpenTerm\backup\`）
- `SessionManager.cs` — SSH 会话管理；`SshTerminalBridge.cs` — SSH Shell 与 WebView2 桥接（直连与跳板、密码/私钥/Agent）
- `TencentCloudService.cs`、`AliCloudService.cs`、`KingsoftCloudService.cs` — 云平台 API 同步服务
- `SshTester.cs` — SSH 连接测试工具
- `RdpLauncher.cs` — RDP 启动服务（生成 .rdp 文件、可选 cmdkey 写入凭据）

**Controls/** — 自定义控件
- `SshWebViewHostControl.xaml(.cs)` — SSH 连接托管控件（WebView2 + xterm.js）
- `RdpHostControl.cs` — RDP 连接托管控件
- `SshStatusBarControl.xaml.cs` — SSH 状态栏（CPU/内存/网络）

**MainWindow** — 主窗口（分文件 partial class）
- `MainWindow.xaml.cs` — 入口与字段
- `MainWindow.ServerTree.cs` — 服务器树 CRUD 与云同步
- `MainWindow.ServerTree.Build.cs` — 树构建、筛选、展开/多选附加属性
- `MainWindow.ServerTree.ContextMenu.cs` — 右键菜单与命令（连接/删除/同步等）
- `MainWindow.ServerTree.Selection.cs` — 多选与键盘/鼠标交互
- `MainWindow.Tabs.cs` — 标签页管理（SSH/RDP）
- `MainWindow.RemoteFile.cs` — 远程文件面板（已废弃但代码仍存在）

**架构模式**：
- **Partial Class**：MainWindow 按 功能模块拆分为多个文件，避免单文件过长
- **服务层分离**：业务逻辑集中在 `Services/`，UI 层仅负责交互与展示
- **接口抽象**：`IStorageService`、`INodeEditContext` 便于单测与解耦
- **DI 容器**：使用 Microsoft.Extensions.DependencyInjection 管理单例服务

**Window/*EditWindow.xaml.cs** — 各种节点编辑窗口
- `GroupNodeEditWindow` — 分组节点
- `SshNodeEditWindow` — SSH 节点
- `RdpNodeEditWindow` — RDP 节点
- `TencentCloudNodeEditWindow`、`AliCloudNodeEditWindow`、`KingsoftCloudNodeEditWindow` — 云同步分组节点
- `TunnelEditWindow` — 隧道编辑
- `CredentialEditWindow` — 凭证编辑

### 配置系统

**配置目录解析顺序**（StorageService.GetConfigDir）：
1. `<当前工作目录>/config/`
2. `<exe所在目录>/config/`（工作目录下无 config 时）

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

**重要**：新增代码中如果使用 WPF 类型，无需添加 `System.Windows.` 前缀；如果需要 WinForms 类型（如 RDP 控件），必须使用完全限定名 `System.Windows.Forms.xxx`。

### 云同步服务架构

所有云同步服务（腾讯云、阿里云、金山云）遵循统一模式：
1. API 调用封装为独立的 `*CloudService` 类
2. 多地域并行拉取（使用 `Task.WhenAll`）
3. 按地域→服务器的层级构建节点树
4. 支持增量更新（不删除已存在的手动配置节点）
5. 同步进度窗口：`CloudSyncProgressWindow.xaml.cs`（腾讯/阿里/金山通用）
6. 分组添加窗口：`*CloudGroupAddWindow.xaml.cs`
7. 节点编辑窗口：`*CloudNodeEditWindow.xaml.cs`

### SSH 终端桥接架构
- **SshTerminalBridge**：SSH.NET ShellStream 与 WebView2 xterm.js 的桥接层
- **直连与跳板统一**：多级跳板通过 SSH.NET 本地端口转发链实现，无需外置 PuTTY/Plink
- **认证方式**：密码、私钥（支持 OpenSSH 格式）、SSH Agent、同父节点凭证、登录凭证
- **会话管理**：SessionManager 管理 SSH 会话生命周期

## 开发注意事项

### PowerShell 脚本规范
- 编码必须为 **UTF-8 with BOM**
- 首行必须包含功能说明注释（中文）
- 使用 `\` 路径分隔符时需用 `Join-Path` 或 `Split-Path`
- 错误处理：使用 `$ErrorActionPreference = "Stop"` 和 `trap` 捕获异常

### 日志系统
- 日志位置：`<当前工作目录>/log/`（test.ps1 下即 .run/log/）
- 文件格式：`YYYY-MM-DD.log`（常规）、`YYYY-MM-DD_crash.log`（崩溃）
- 日志级别：DEBUG/INFO/WARN/ERR/FATAL
- 使用 `ExceptionLog.Write(ex, "上下文")` 记录异常

### 单元测试
- 测试框架：NUnit
- 测试目录：`tests/`
- 工作目录：通过 `GlobalRunDirectorySetup.cs` 自动设为 `.run/`
- 运行方式：`dotnet test` 或 `.\test.ps1 --test-ssh-status`
- 环境变量：`XOPENTERM_UNIT_TEST=1` 在测试时自动设置，可用于代码中判断是否在测试环境

### 版本管理
- 版本号定义在 `src/xOpenTerm.csproj` 的 `<Version>` 节点
- 发布时自动从 csproj 读取版本并创建 `.dist/xOpenTerm-v<版本>/` 目录

### 不使用的功能（代码仍存在）
- 远程文件面板（`MainWindow.RemoteFile.cs`）已废弃但代码保留

### WPF 资源
- MaterialDesignThemes 主题
- 图标：`src/icons/icon.ico`

### 禁止
- 不允许使用写死的本地路径（必须使用相对路径或从配置/环境变量获取）
- 新增或修改功能时必须同步更新 README.md
- 不允许在 UI 线程执行耗时操作（使用 `await Task.Run()` 或后台线程）
- 不允许在代码中硬编码密码、API 密钥等敏感信息

### Agent 结束后自动执行 test.ps1
Agent 执行结束后会自动运行 `.\test.ps1` 进行构建和测试

## 常见开发任务

### 添加新的 SSH/RDP 节点类型
1. 在 `Models/Node.cs` 中添加新的 `NodeType` 枚举值
2. 在 `ConnectionConfig` 中添加该类型特有的配置字段
3. 在 `MainWindow.ServerTree.cs` 中添加该类型节点的创建逻辑
4. 创建对应的编辑窗口（继承 `NodeEditWindow`）
5. 更新右键菜单（`MainWindow.ServerTree.ContextMenu.cs`）

### 修改加密算法
- 修改 `Services/SecretService.cs`
- 添加新的加密前缀（如 `xot5:`）
- 更新 `GetDecrypted` 和 `GetEncrypted` 方法
- 确保向后兼容旧版本密文

### 添加新的云平台同步
1. 创建 `Services/*CloudService.cs`（参考现有实现）
2. 实现多地域并行拉取逻辑
3. 创建 `*CloudGroupAddWindow.xaml.cs` 和 `*CloudNodeEditWindow.xaml.cs`
4. 在 `MainWindow.ServerTree.cs` 中添加同步菜单项
5. 在 `StorageService.cs` 中注册新的节点类型处理