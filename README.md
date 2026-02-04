# xOpenTerm2

参考 [xOpenTerm](https://github.com/your-org/xOpenTerm) 实现的 **C# WPF** 版本：Windows 下 SSH / 本地终端批量管理工具。

## 功能

- **节点树**：分组、SSH、本地终端（PowerShell/CMD）、RDP；支持拖拽节点到其他分组
- **连接管理**：双击或右键「连接」打开标签页，支持多标签
- **本地终端**：内置 PowerShell 或 CMD
- **SSH**：密码或私钥认证，认证来源（本节点/登录凭证/同父节点）、跳板机多选、节点/凭证/隧道内「测试连接」
- **RDP**：启动系统远程桌面（mstsc），临时 .rdp 文件与可选 cmdkey 写入凭据；默认端口 3389、用户名 administrator
- **顶栏菜单**：设置 → 登录凭证、隧道管理；帮助 → 关于、检查更新
- **登录凭证**：独立管理窗口，可被多个节点引用
- **隧道管理**：跳板机增删改查与测试连接
- **关于 / 更新**：版本号、作者、GitHub 链接；从 GitHub Releases 检查更新
- **持久化**：节点、凭证、隧道保存为 YAML（`config/nodes.yaml`、`credentials.yaml`、`tunnels.yaml`，位于 exe 同目录下的 `config/`）

## 技术栈

- .NET 8 / WPF
- [SSH.NET](https://github.com/sshnet/SSH.NET)（SSH）
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)（YAML）

## 构建与运行

```bash
cd d:\xsw\code_auto_push\xOpenTerm2
dotnet build src\xOpenTerm2\xOpenTerm2.csproj
dotnet run --project src\xOpenTerm2\xOpenTerm2.csproj
```

或使用 Visual Studio 打开 `xOpenTerm2.sln` 后 F5 运行。

## 配置目录

首次运行后，在 exe 所在目录下会生成 `config/`，其中：

- `nodes.yaml`：服务器/分组树（与 xOpenTerm 数据结构兼容）
- `credentials.yaml`：登录凭证
- `tunnels.yaml`：SSH 隧道（跳板机）

可从 xOpenTerm 复制上述 YAML 到本项目的 `config/` 使用。

## 与 xOpenTerm 的差异

- 无内嵌 RDP 窗口（RDP 通过 mstsc 启动系统远程桌面）
- 无「远程文件」面板与 `list_remote_dir`
- 终端为自定义绘制 VT100 终端（ANSI 颜色/SGR、仅绘制可见行，无 xterm.js）
- 隧道链配置与选择已支持，SSH 支持直连与多跳（经跳板机链本地端口转发连接目标）
