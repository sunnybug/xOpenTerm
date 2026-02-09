# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

xOpenTerm 是一个 **C# WPF** 实现的 Windows SSH/本地终端批量管理工具，参考原版 xOpenTerm (Tauri) 实现。

核心功能：
- **节点树管理**：支持分组、SSH、本地终端（PowerShell/CMD）、RDP 节点
- **多标签连接**：支持同时打开多个终端/远程桌面会话
- **SSH 连接**：密码/私钥/SSH Agent/登录凭证认证，支持跳板机多跳
- **本地终端**：内置 PowerShell 和 CMD 终端
- **RDP 连接**：通过系统 mstsc 启动远程桌面
- **凭证管理**：独立的登录凭证系统，可被多个节点引用
- **腾讯云同步**：支持从腾讯云导入并同步服务器列表
- **阿里云同步**：支持从阿里云 ECS 与轻量应用服务器导入并同步服务器列表（地域→服务器）

## 构建和运行

```powershell
# 编译并运行（Debug 模式）
.\test.ps1

# 仅构建
.\script\build.ps1           # Debug
.\script\build.ps1 --release # Release

# 构建并运行（Release 模式）
.\test.ps1 --release

# 发布到 dist 目录
.\script\publish.ps1

# 初始化开发环境（还原依赖、创建 bin/config 等）
.\script\init_dev.ps1
```

或使用 Visual Studio 打开 `xOpenTerm.sln` 后 F5 运行。

## 项目结构

```
xOpenTerm/
├── src/                          # 源代码目录
│   ├── Models/                   # 数据模型（Node、Credential、Tunnel、NodeType 等）
│   ├── Services/                 # 业务逻辑服务
│   │   ├── SessionManager.cs     # SSH/本地会话管理
│   │   ├── StorageService.cs     # YAML 配置持久化
│   │   ├── SecretService.cs      # 密码加密/解密
│   │   ├── ConfigResolver.cs     # 配置解析（继承/凭证引用）
│   │   ├── TencentCloudService.cs # 腾讯云 API 集成
│   │   ├── AliCloudService.cs    # 阿里云 ECS API 集成
│   │   ├── RemoteFileService.cs  # SCP 远程文件操作
│   │   └── SshTester.cs          # SSH 连接测试
│   ├── Controls/                 # 自定义控件
│   │   ├── TerminalControl.xaml.cs     # 自定义 VT100 终端（ANSI 颜色/SGR）
│   │   ├── TerminalSurface.cs          # 终端绘制表面（自定义绘制）
│   │   ├── TerminalBuffer.cs           # 终端缓冲区
│   │   ├── Vt100Parser.cs              # VT100/ANSI 解析器
│   │   ├── SshPuttyHostControl.cs      # PuTTY 窗口嵌入控件
│   │   └── RdpHostControl.cs           # RDP 宿主控件
│   ├── MainWindow.xaml.cs        # 主窗口（核心逻辑）
│   ├── MainWindow.ServerTree.cs  # 节点树逻辑（拖拽、右键菜单）
│   ├── MainWindow.Tabs.cs        # 连接标签页管理
│   ├── MainWindow.RemoteFile.cs  # 远程文件面板
│   ├── App.xaml.cs               # 应用入口
│   └── *.xaml / *.xaml.cs        # 各种对话框和窗口
├── tests/                        # 单元测试
├── test.ps1                      # 构建并运行应用（支持 --release）
├── script/                       # PowerShell 脚本
│   ├── build.ps1                 # 构建项目（支持 --release）
│   ├── publish.ps1               # 发布到 dist 目录
│   └── init_dev.ps1              # 初始化开发环境
├── bin/                          # 工作目录（运行时）
│   └── config/                   # 配置文件目录
├── var/                          # 可变覆盖目录
└── dist/                         # 发布目录
```

## 架构要点

### 1. 主窗口职责分离

`MainWindow.xaml.cs` 是应用的核心，已按功能拆分为多个 partial class：
- `MainWindow.ServerTree.cs`：节点树操作（展开/折叠、右键菜单、拖拽、多选）
- `MainWindow.Tabs.cs`：连接标签页管理（创建/切换/关闭、重连/断开）
- `MainWindow.RemoteFile.cs`：远程文件面板（浏览、上传/下载、编辑）

**重要**：修改主窗口功能时，优先找到对应的 partial class 文件修改。

### 2. 会话管理 (SessionManager)

`SessionManager` 负责所有 SSH 和本地终端会话的生命周期：
- `CreateSshSession()`：创建 SSH 会话，支持直连或跳板机多跳
- `CreateLocalSession()`：创建本地 PowerShell/CMD 会话
- 通过事件向 UI 推送数据：`DataReceived`、`SessionClosed`、`SessionConnected`
- 使用 `ConcurrentDictionary` 管理会话（线程安全）

### 3. 终端实现

项目包含**两种**终端实现：

#### 自定义 VT100 终端（默认）
- `Vt100Parser`：解析 ANSI/VT100 转义序列（颜色、光标、擦除）
- `TerminalBuffer`：终端缓冲区（存储文本段、样式）
- `TerminalSurface`：自定义绘制表面（仅绘制可见行）
- **优点**：轻量、无需外部依赖、支持 ANSI 颜色和 SGR
- **缺点**：功能有限，不支持 xterm.js 全部特性

#### PuTTY 嵌入终端（可选）
- `SshPuttyHostControl`：嵌入 PuTTY/PuTTYNG 窗口
- 使用命名管道传密码、SetParent/-hwndparent 嵌入窗口
- **优点**：完整 PuTTY 功能、稳定可靠
- **缺点**：依赖外部 PuTTY 可执行文件

### 4. 配置解析与继承 (ConfigResolver)

节点配置支持复杂的继承链：
- **同父节点**：子节点可继承父节点的登录凭证、隧道配置
- **登录凭证引用**：节点可通过 `credentialId` 引用凭证
- **隧道引用**：节点可通过 `tunnelIds` 引用隧道列表
- `ConfigResolver.ResolveFinalConfig()` 递归解析最终配置

### 5. 密码加密 (SecretService)

配置文件中的密码使用 AES 加密存储：
- **版本 1**：AES-256（前缀 `xot2:`）
- **版本 2**：AES-256-GCM（前缀 `xot3:`）
- **旧版 DPAPI**：兼容旧配置（前缀 `xot1:`，仅限原机器）
- **跨机器兼容**：密钥从固定种子派生，同一版本在所有机器上可解密同一配置

### 6. 持久化 (StorageService)

配置保存在 `config/` 目录：
- `nodes.yaml`：节点树结构（与 xOpenTerm Tauri 版本兼容）
- `credentials.yaml`：登录凭证
- `tunnels.yaml`：SSH 隧道（跳板机）
- `settings.yaml`：应用设置（窗口位置、面板宽度等）

配置目录优先级：
1. 工作目录下的 `config/`（用于开发和覆盖测试）
2. exe 所在目录下的 `config/`（生产环境）

### 7. 腾讯云集成 (TencentCloudService)

- 支持创建"腾讯云组"节点，从腾讯云 API 导入服务器列表
- 同步功能：增量更新节点树，删除云上已不存在的服务器
- 节点结构：机房 → 项目 → 服务器（SSH/RDP）

## 核心依赖

- **SSH.NET** (`Renci.SshNet`)：SSH 协议实现
- **YamlDotNet**：YAML 序列化/反序列化
- **MaterialDesignThemes**：Material Design WPF 主题库
- **RoyalApps.Community.Rdp.WinForms**：RDP 连接封装
- **TencentCloudSDK**：腾讯云 API SDK
- **AlibabaCloud.SDK.Ecs20140526**：阿里云 ECS API SDK

## 常见开发任务

### 添加新的节点类型

1. 在 `Models/NodeType.cs` 添加枚举值
2. 在 `Models/ConnectionConfig.cs` 添加对应配置类
3. 在 `MainWindow.ServerTree.cs` 更新节点图标和右键菜单
4. 在 `MainWindow.Tabs.cs` 添加标签页创建逻辑

### 修改终端行为

- 自定义终端：修改 `Controls/Vt100Parser.cs`（解析器）、`Controls/TerminalSurface.cs`（绘制）
- PuTTY 终端：修改 `Controls/SshPuttyHostControl.cs`（启动参数、窗口嵌入）

### 添加新的认证方式

1. 在 `Models/AuthType.cs` 添加枚举值
2. 在 `Services/SessionManager.cs` 的 `CreateSshSession()` 中添加认证逻辑
3. 在对话框 UI（如 `NodeEditWindow.xaml.cs`）添加对应控件

### 修改配置文件结构

1. 修改 `Models/` 中的数据模型
2. 更新 `Services/StorageService.cs` 的序列化/反序列化逻辑
3. 考虑向后兼容（`SecretService.CurrentConfigVersion`）

## 重要约定

- **中文注释**：所有新增代码必须使用中文注释
- **UTF-8 BOM**：PowerShell 脚本必须使用 UTF-8 BOM 编码
- **UTF-8**：BAT 文件必须使用 UTF-8 编码，开头添加 `chcp 65001`
- **路径规范**：不允许使用写死的本地路径，使用相对路径或通过 `StorageService.GetConfigDir()` 获取
- **README 更新**：新增或修改功能时，必须更新 README.md

## 调试技巧

- SSH 连接问题：检查 `Services/SshTester.cs` 的错误消息输出
- 终端显示问题：在 `Controls/Vt100Parser.cs` 中添加日志查看转义序列
- 配置加载问题：检查 `Services/StorageService.cs` 的异常处理
- PuTTY 嵌入问题：检查 `Controls/SshPuttyHostControl.cs` 的窗口句柄和命名管道

## 其他特性

- 无内嵌 RDP 窗口（通过 mstsc 启动系统远程桌面）
- 终端为自定义绘制 VT100（非 xterm.js）
- 腾讯云、阿里云同步功能
- MobaXterm 配置导入

## log位置
%LocalAppData%\xOpenTerm\logs\info_yyyy-MM-dd.log
