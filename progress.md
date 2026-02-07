# xOpenTerm

A high-performance, modern Windows SSH/Terminal batch management tool built with Tauri v2.

## Tech Stack

- **Core Framework**: Tauri v2 (Rust + Webview)
- **Frontend**: React + TypeScript + Vite
- **UI Library**: Tailwind CSS + Lucide Icons
- **Terminal Component**: Xterm.js
- **State Management**: Zustand
- **Backend**: Rust with tokio, russh, portable-pty, aes-gcm

## Prerequisites

### Required
- Node.js 18+
- Rust (latest stable)

### Installation

1. Clone the repository
2. Install frontend dependencies:
   ```bash
   npm install
   ```

3. Install Rust (if not already installed):
   - Windows: Download from [rust-lang.org](https://www.rust-lang.org/tools/install)

## Development

### VS Code 调试配置

项目已配置 `.vscode/launch.json`，支持 F5 快速启动：

- **启动开发服务器**: 仅启动 Vite 前端开发服务器
- **启动 Tauri 开发**: 启动完整的 Tauri 应用（推荐）
- **在 Chrome 中调试**: 在浏览器中调试前端

按 `F5` 即可快速启动调试。

### 命令行启动

```bash
npm run tauri dev
```

这将：
- 启动 Vite 开发服务器
- 构建并运行 Tauri 应用
- 启用前端和后端热重载

### Build for Production

```bash
npm run tauri build
```

### Release / 发布（GitHub Actions）

项目已配置 GitHub Actions，推送 tag 或手动触发即可自动构建并发布到 GitHub Releases。

**触发方式：**

1. **推送 tag**（推荐）：先升级版本号，再打 tag 并推送，CI 会从 tag 同步版本号并构建上传。
   ```bash
   npm run version:patch   # 或 version:minor / version:major（会同步 package.json、tauri.conf.json、Cargo.toml）
   git add .
   git commit -m "Release vX.Y.Z"
   git tag vX.Y.Z
   git push && git push --tags
   ```
2. **手动触发**：在 GitHub 仓库 Actions 页选择 “Build and Release” → “Run workflow”，可选填写版本号（留空则使用当前 package.json 版本）。

**流程说明：**

- 从 tag（如 `v0.1.2`）或输入解析版本号，并同步到 `package.json`、`src-tauri/tauri.conf.json`、`src-tauri/Cargo.toml`。
- 使用 `tauri-action` 在 Windows 上构建，生成安装包并上传到对应 Release 的 Assets。

**仓库设置：** 若出现 “Resource not accessible by integration”，请在仓库 Settings → Actions → General → Workflow permissions 中勾选 “Read and write permissions”。

## Project Structure

```
xOpenTerm/
├── src/                    # Frontend source
│   ├── components/        # React components
│   ├── store/            # Zustand state management
│   ├── types/            # TypeScript type definitions
│   ├── App.tsx           # Main application
│   ├── main.tsx          # Entry point
│   └── index.css         # Global styles
├── src-tauri/             # Rust backend
│   ├── src/
│   │   ├── main.rs       # Tauri commands and data structures
│   │   └── lib.rs        # Library exports
│   ├── Cargo.toml        # Rust dependencies
│   └── tauri.conf.json   # Tauri configuration
└── package.json          # Frontend dependencies
```

## Phase 1 Completed ✅

- [x] Initialize Tauri v2 project structure
- [x] Install frontend dependencies
- [x] Define Rust data structures (Node, AppConfig, etc.)
- [x] Define frontend Zustand Store for state management
- [x] Set up basic project configuration files

## Phase 2 Completed ✅

- [x] Sidebar Tree View component
- [x] Main Tabs Bar component
- [x] Xterm.js basic integration
- [x] Basic layout structure

## Phase 3 Completed ✅

Rust 后端核心实现：
- [x] PTY 管理器 (`pty_manager.rs`) - 管理所有活跃的终端会话
- [x] SSH 会话模块 (`ssh_session.rs`) - 处理 SSH 协议连接
- [x] 本地终端模块 (`local_session.rs`) - 处理 PowerShell/CMD 本地终端
- [x] Tauri Commands 实现：
  - `connect_session` - 连接到 SSH 或本地终端
  - `write_to_session` - 写入数据到会话
  - `resize_session` - 调整终端 PTY 大小
  - `close_session` - 关闭会话
  - `test_connection` - 测试 SSH 连接
  - `get_nodes` / `save_node` / `delete_node` - 节点管理

### 新增文件

```
src-tauri/src/
├── main.rs          # 更新：集成所有模块和 Tauri commands
├── pty_manager.rs   # 新增：PTY 会话管理器
├── ssh_session.rs   # 新增：SSH 连接处理
└── local_session.rs # 新增：本地终端处理
```

## Phase 4 Completed ✅

前后端 IPC 通信实现：
- [x] 后端事件发送机制 - PtyManager 通过 AppHandle 发送事件到前端
- [x] SSH 会话数据回调 - 实现 SSH 数据接收并转发到前端
- [x] 本地终端数据回调 - 实现本地终端数据接收并转发到前端
- [x] 前端 Terminal 组件 IPC 通信：
  - 监听 `session_data_received` 事件并写入 Xterm
  - 监听 `session_closed` 事件处理会话关闭
  - `onData` 回调将用户输入发送到后端
- [x] 终端 Resize 功能：
  - 监听窗口大小变化
  - Tab 切换时自动 fit
  - 调用 `resize_session` 同步 PTY 大小
- [x] Store 连接状态管理：
  - 添加 ConnectionStatus 类型
  - Tab 状态跟踪
  - 事件监听器初始化
- [x] TabsBar 状态显示：
  - 连接中图标（旋转加载）
  - 已连接图标（绿色对勾）
  - 错误/断开图标（警告）

### 修改文件

```
src-tauri/src/
├── main.rs          # 更新：使用 setup hook 初始化 PtyManager
├── pty_manager.rs   # 更新：添加 AppHandle 支持和事件发送
├── ssh_session.rs   # 更新：添加数据回调支持
└── local_session.rs # 更新：添加数据回调和 resize 支持

src/
├── components/
│   ├── Terminal.tsx # 更新：实现完整的 IPC 通信
│   └── TabsBar.tsx  # 更新：显示连接状态
├── store/
│   └── index.ts     # 更新：添加连接状态管理
└── App.tsx          # 更新：初始化事件监听器
```

## Phase 5 Completed ✅

右键菜单逻辑和批量连接功能：
- [x] 右键菜单组件 (`ContextMenu.tsx`)
- [x] 节点编辑对话框 (`NodeDialog.tsx`)
- [x] 新建主机/组功能
- [x] Connect All 批量连接功能 - 递归遍历分组下所有子孙节点并并发连接
- [x] 编辑节点配置
- [x] 复制节点功能
- [x] 删除节点功能 - 分组支持递归删除所有子节点
- [x] TreeView 右键菜单集成

### 新增文件

```
src/components/
├── ContextMenu.tsx  # 新增：右键菜单组件
└── NodeDialog.tsx   # 新增：节点编辑对话框
```

### 修改文件

```
src/
├── components/
│   └── TreeView.tsx # 更新：集成右键菜单和对话框
├── store/
│   └── index.ts     # 更新：添加批量连接和复制节点功能
└── App.tsx          # 更新：添加连接处理函数
```

### 功能说明

#### 右键菜单
- **分组节点**：新建分组、新建主机、连接全部、删除
- **SSH/本地节点**：连接、复制、编辑、删除

#### 批量连接 (Connect All)
- 递归遍历分组下所有子孙节点
- 并发启动所有非分组节点的连接
- 自动打开对应的 Tab

#### 节点编辑对话框
- 支持创建/编辑分组、SSH连接、本地终端
- SSH配置：主机、端口、用户名、认证方式（密码/密钥）、Agent 转发（复选框）
- **测试连接**：SSH 节点配置区提供「测试连接」按钮，可验证当前主机/端口/认证是否可用（当前仅支持密码认证）
- 本地终端配置：PowerShell/CMD 选择

## 完成进度

- [x] Phase 1: 项目初始化和数据结构定义
- [x] Phase 2: 前端 UI 骨架（Tree + Tabs + Terminal）
- [x] Phase 3: Rust 后端核心（PTY 管理器与 SSH 连接）
- [x] Phase 4: 前后端 IPC 通信（打通 Xterm I/O 流）
- [x] Phase 5: 右键菜单逻辑（CRUD）和批量连接功能
- [x] Phase 6: 代码质量检查和修复

## Phase 6 代码质量修复 ✅

### 修复内容

1. **TypeScript 编译错误修复**：
   - 修复 `ContextMenu.tsx` 中的 Node 类型冲突（自定义 Node 类型与 DOM Node 类型冲突）
   - 添加了 LucideIcon 类型导入
   - 修复了 Icon 组件和 action 函数可能为 undefined 的问题

2. **ESLint 配置**：
   - 创建了 `eslint.config.js` 文件（适配 ESLint v9）
   - 安装了必要的依赖：`@eslint/js`、`typescript-eslint`、`eslint-plugin-react` 等
   - 配置了 React 17+ 规则（禁用了 `react-in-jsx-scope`）

3. **React Hook 依赖警告修复**：
   - 使用 `useCallback` 包装了 `initializeConnection` 和 `updateTerminalSize` 函数
   - 优化了函数声明顺序，将 useCallback 定义的函数移到 useEffect 之前
   - 更新了 useEffect 的依赖数组，消除所有警告

### 代码质量检查命令

```bash
npm run lint    # ESLint 代码检查
npm run build   # TypeScript 编译 + Vite 构建
```

**检查结果**：✅ 所有检查通过，无错误、无警告

## Phase 7: SSH Agent 支持 ✅

### 新增功能

xOpenTerm 现在支持三种 SSH 认证方式：

1. **密码认证** - 使用用户名和密码进行认证
2. **密钥文件认证** - 使用私钥文件进行认证
   - 支持加密的私钥（需要输入密钥密码）
   - 支持多种密钥格式（RSA、Ed25519 等）
3. **SSH Agent 认证** - 使用 SSH Agent 进行认证
   - Unix/Linux/macOS: 通过 `SSH_AUTH_SOCK`（Unix Domain Socket）完全支持
   - Windows: 通过 OpenSSH 身份验证代理服务（命名管道 `\\.\pipe\openssh-ssh-agent`）完全支持

### 修改文件

```
src-tauri/src/
├── main.rs          # 更新：添加 Agent 认证类型和检测命令（含 Windows 命名管道）
├── ssh_session.rs   # 更新：实现密钥认证和 Agent 认证（Unix UDS + Windows 命名管道）
└── pty_manager.rs   # 更新：支持带认证类型的 SSH 会话创建

src/
├── types/index.ts   # 更新：添加 Agent 认证类型
├── components/NodeDialog.tsx  # 更新：UI 支持 Agent 选择和密钥文件浏览
└── package.json     # 更新：添加 @tauri-apps/plugin-dialog 依赖
```

### 使用方法

#### 密钥文件认证

1. 在节点编辑对话框中选择"密钥文件"认证方式
2. 点击"浏览"按钮选择私钥文件
3. 如果私钥有加密，输入密钥密码
4. 保存配置并连接

#### SSH Agent 认证（Unix/Linux/macOS）

1. 确保 ssh-agent 正在运行：
   ```bash
   # Linux/macOS
   eval "$(ssh-agent -s)"
   ```

2. 添加密钥到 Agent：
   ```bash
   ssh-add ~/.ssh/id_rsa
   ```

3. 在节点编辑对话框中选择"SSH Agent"认证方式
4. 保存配置并连接

#### SSH Agent 认证（Windows）

1. 启动 **OpenSSH 身份验证代理** 服务：
   - 设置 → 应用 → 可选功能 → 确保已安装「OpenSSH 客户端」
   - 服务（`services.msc`）→ 找到「OpenSSH Authentication Agent」→ 启动类型设为「手动」或「自动」→ 启动服务
   - 或在管理员 PowerShell 中：`Set-Service ssh-agent -StartupType Manual; Start-Service ssh-agent`

2. 添加密钥到 Agent：
   ```powershell
   ssh-add $env:USERPROFILE\.ssh\id_rsa
   ```

3. 在节点编辑对话框中选择"SSH Agent"认证方式
4. 保存配置并连接

### Tauri Commands

新增两个命令用于 Agent 管理：

- `check_agent_available()` - 检测 SSH Agent 是否可用
- `get_agent_identities()` - 获取 Agent 中的密钥列表

### 平台差异

| 功能 | Unix/Linux/macOS | Windows |
|------|------------------|---------|
| 密码认证 | ✅ | ✅ |
| 密钥文件认证 | ✅ | ✅ |
| SSH Agent 认证 | ✅ | ✅ |

### 安全性

- 密码和密钥密码使用 AES-GCM 加密存储
- 私钥文件直接从磁盘读取，不缓存私钥内容
- SSH Agent 认证通过系统 Agent 服务处理，不直接访问私钥

## 已实现：顶栏菜单与登录凭证

### 顶栏菜单

- 窗口顶部顶栏菜单（文件、编辑、设置）。
- 顶栏 **设置** → **登录凭证** 打开登录凭证管理；侧栏底部「设置」按钮也可打开同一弹窗。

### SSH 认证的两种配置方式

SSH 节点认证支持二选一：

1. **本节点单独配置**
   - 在节点编辑对话框中填写 host、端口、用户名、认证方式（密码/密钥/Agent）等，仅作用于当前节点。

2. **登录凭证**
   - 登录凭证为可复用的认证配置（名称、用户名、密码/密钥/Agent），可被多个节点引用。
   - 节点编辑时选择「认证来源」→「使用登录凭证」并选择已保存的凭证；主机与端口仍由节点配置。
   - 凭证存储于 `credentials.yaml`（与 `nodes.yaml` 同目录），通过 **设置** → **登录凭证** 增删改查。
   - **测试连接**：在新建/编辑凭证时，若认证方式为密码，可填写「测试主机」「端口」并点击「测试连接」验证该凭证是否可用。

### Tauri Commands（登录凭证）

- `get_credentials()` - 获取所有登录凭证
- `save_credential(credential)` - 新增或更新凭证
- `delete_credential(id)` - 删除凭证

## 已实现：SSH 隧道

- **凭证 / 节点均可配置隧道**：在登录凭证或 SSH 节点配置中可添加「SSH 隧道」链（可选）。
- **多级隧道**：隧道链按顺序配置多跳（跳板机），连接时依次经各跳到达目标。
- **每跳认证**：每跳支持密码 / 密钥 / Agent；每跳可单独填写认证或引用凭证（`credentialId`）。
- **后端实现**：`russh` 的 `connect_stream` + `channel_open_direct_tcpip`，首跳 TCP 直连，后续经上一跳的 direct-tcpip 通道再 `connect_stream` 到下一跳或目标。
- **代码结构**：隧道逻辑已抽取到独立模块 `src-tauri/src/ssh_tunnel.rs`，对外提供 `with_tunnel_connection`，在隧道流上完成最终连接后将 `(Handle, Channel)` 交给调用方；`ssh_session` 仅负责驱动会话与 IO 转发。

### SSH 隧道管理界面

- **每条隧道 = 一个跳板机**：添加隧道界面每次只添加一个跳板机（名称、主机、端口、用户名、认证方式等）；跳板机跳到哪里由编辑节点时填写的「主机地址」「端口」决定。
- **隧道存储**：隧道列表持久化到 `tunnels.yaml`（与 `nodes.yaml` 同目录），通过「管理跳板机」弹窗增删改查。
- **节点使用跳板机**：编辑 SSH 节点时，「跳板机」处按顺序选择多个已保存的跳板机（下拉「添加跳板机…」可追加），连接时依次经所选跳板机转发，最终到达节点填写的 host:port；目标由节点界面的「主机地址」「端口」填写。
- **向后兼容**：节点支持 `tunnelIds`（有序）、旧版 `tunnelId`（单条）、内联 `tunnel` 数组。

### Tauri Commands（隧道）

- `get_tunnels()` - 获取所有隧道（每条为单跳跳板机）
- `save_tunnel(tunnel)` - 新增或更新隧道（含 id、name、host、port、username、authType 等单跳字段）
- `delete_tunnel(id)` - 删除隧道

## 待实现特性

### ~~SSH隧道 允许登录凭证中配置SSH隧道，通过SSH隧道进行该节点的SSH登录~~
### ~~SSH隧道支持多级~~
### ~~SSH隧道支持登录凭证~~（每跳可填 credentialId，后端已支持）
### 菜单栏放到节点数和终端tab的顶部
### ~~ssh隧道增加专门的管理界面，节点使用隧道时只是选择，或者点击按钮打开管理界面，进行隧道的增删改查。~~
### ~~ssh隧道管理，添加隧道界面，每次只添加一个跳板机。至于跳板机会跳到哪里，由编辑节点界面填写。~~
### 顶栏菜单-设置，加：隧道界面（用于管理隧道）
### ssh跳板机管理要加一个测试连接按钮，测试跳板机是否能连通。
### 顶栏菜单加一个帮助，里面放关于（显示版本号和作者信息/github信息）
### 实现github actions自动构建和发布，自动更新版本号，自动生成发布包，自动上传到github releases。
### 顶栏菜单帮助->更新，实现更新功能，最新版本从github的releases中检查
### ~~节点数列表上的节点允许拖拽到其他节点下~~
### 节点的隧道/登录凭证，加一个：同父节点。通过此配置继承了父节点的隧道/登录凭证等设置。父节点的加上设置选项，用于设置隧道和登录凭证


ssh节点和rdp节点的图标改一下。父节点有展开和未展开的图标改一下。
rdp默认端口3389 用户名默认administrator 不用域 名称未填写时用主机地址
### 支持rdp windows远程桌面连接
### 节点树顶上加一行tab（服务器/远程文件），服务器tab下显示节点树。 当SSH tab连接成功后，切换到远程文件tab，显示远程文件列表。用SCP。
节点设置界面，去掉认证来源，认证的下拉框增加：同父节点/SSH Agent/登录凭证
顶栏菜单加个设置，可配置界面的字体和大小，还有ssh终端的字体和大小，并可预览
远程文件中，鼠标右键可以对文件进行编辑（编辑后若发现文件内容变化，则提示是否上传）/删除/下载/上传。

节点数支持按ctrl/shift多选，拖拽某个选中的节点则可批量拖拽
父节点右键菜单中增加导入。导入为子菜单，包含导入Mobaxterm，用于读取MobaXterm.ini中的节点配置。当触发导入Mobaxterm时，读取MobaXterm.ini中的节点配置，显示其中配置的节点结构，选择（可多选，支持shift/ctrl多选）后点击确定，则将选中的节点导入到当前父节点下。选择时以目录为单位,导入后按目录名和子目录名创建父节点，按照在mobaxterm中的结构来导入,导入后要保持在mobaxterm中的结构和条数.写个单元测试，测试导入d:\xsw\Dropbox\tool\net\MobaXterm\MobaXterm.ini。注意ini可能为GBK。
导入Mobaxterm时，若为密码认证，则改为SSH Agent认证。
服务器tab下加置顶快速搜索edit，会快速过滤节点数中所有节点名称，主机地址，用户名
ctrl/shift下多选后，右键菜单应当是多选右键菜单：删除/连接
shift多选时，只对当前同一级节点有效，其他层级的节点取消多选，若同级有父节点，则其子节点也被选中
保存窗口/tab大小，重启有效
跳板机配置中的认证加一个登录凭证
所有的登录测试，都需要给出具体失败原因：端口不通？Please login as the user "ubuntu" rather than the user "root".？密码错误？
父节点的设置中，登录凭证要区分SSH/RDP。子节点的登录凭证-同父节点，在向父节点获取时，要区分SSH/RDP。
rdp子节点的登录凭证也要有：同父节点
新增父节点后，为什么会自动展开其他一个父节点？
弹出的对话框中系统栏上不要额外显示对话框标题
新增一种特殊的父节点：腾讯云。1.节点数的普通右键菜单增加：腾讯云组，新增时需要填入腾讯云的密钥 2.新增腾讯云组节点时，将腾讯云上所有服务器导入到到该父节点中，节点树结构：一级是机房（香港/广州），二级是项目，三级是服务器节点（区分linux和windows） 3.服务器节点的登录凭证默认是同父节点 4. 腾讯云组类型的父节点的右键菜单相对于普通父节点多一个：同步（执行腾讯云节点同步功能）

