# xOpenTerm

参考 [xOpenTerm](https://github.com/your-org/xOpenTerm) 实现的 **C# WPF** 版本：Windows 下 SSH / 本地终端批量管理工具。

## 功能

- **节点树**：分组、SSH、本地终端（PowerShell/CMD）、RDP；支持拖拽节点到其他分组
- **连接管理**：双击或右键「连接」打开标签页，支持多标签
- **本地终端**：内置 PowerShell 或 CMD
- **SSH**：认证下拉（密码/私钥/同父节点/SSH Agent/登录凭证）、跳板机多选、节点/凭证/隧道内「测试连接」
- **RDP**：启动系统远程桌面（mstsc），临时 .rdp 文件与可选 cmdkey 写入凭据；默认端口 3389、用户名 administrator
- **顶栏菜单**：设置 → 登录凭证、隧道管理、恢复配置；帮助 → 关于、检查更新
- **配置备份与恢复**：配置文件修改时自动备份到 `%LocalAppData%\xOpenTerm\backup\YYMMDD-HHMMSS\`（60 秒防抖）；设置 → 恢复配置可打开备份列表，按时间与大小显示，支持打开备份目录或恢复（恢复前会先备份当前配置）
- **登录凭证**：独立管理窗口，可被多个节点引用
- **隧道管理**：跳板机增删改查与测试连接
- **腾讯云同步**：从腾讯云 API 导入服务器列表，支持增量更新；多地域并行拉取（CVM、轻量应用服务器）
- **阿里云同步**：从阿里云 ECS 与轻量应用服务器 API 导入服务器列表，按地域→服务器构建节点树，支持增量更新；多地域并行拉取
- **金山云同步**：从金山云 KEC（云服务器）API 导入服务器列表，按地域→服务器构建节点树，支持增量更新；多地域并行拉取
- **关于 / 更新**：版本号、作者、GitHub 链接；从 GitHub Releases 检查更新
- **持久化**：节点、凭证、隧道保存为 YAML（`config/nodes.yaml`、`credentials.yaml`、`tunnels.yaml`，位于 exe 同目录下的 `config/`）；节点树的展开状态与选中项在关闭时写入 `config/settings.yaml`，下次启动时恢复

## 技术栈

- .NET 8 / WPF
- [SSH.NET](https://github.com/sshnet/SSH.NET)（SSH）
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)（YAML）

## 项目结构

- `src/` — 源码（输出到仓库根下 `bin/Debug`、`bin/Release`，中间文件在 `temp/`）
- `test.ps1` — 构建并运行应用（支持 --release）
- `script/` — 脚本：`build.ps1`、`publish.ps1`、`init_dev.ps1`
- `bin/` — 工作目录：`config/` 配置、`log/` 日志、`var/` 临时覆盖配置
- `dist/` — 发布目录（由 `script/publish.ps1` 或 GitHub Actions 生成）
- `doc/`、`aidoc/` — 文档与 AI 生成文档

## 构建与运行

```powershell
# 编译并运行 Debug
.\test.ps1

# 仅构建
.\script\build.ps1           # Debug
.\script\build.ps1 --release # Release

# 构建并运行 Release
.\test.ps1 --release

# 初始化开发环境（还原依赖、创建 bin/config 等）
.\script\init_dev.ps1

# 发布到 dist（带版本号）
.\script\publish.ps1
```

或使用 Visual Studio 打开 `xOpenTerm.sln` 后 F5 运行。

## 配置目录

首次运行后，在 exe 所在目录下会生成 `config/`，其中：

- `nodes.yaml`：服务器/分组树（与 xOpenTerm 数据结构兼容）
- `credentials.yaml`：登录凭证
- `tunnels.yaml`：SSH 隧道（跳板机）

可从 xOpenTerm 复制上述 YAML 到本项目的 `config/` 使用。

### 配置中的密码加密与多机共用

- 节点/凭证/隧道 YAML 根节点包含 `version` 字段，不同版本使用不同的写死密钥与算法。
- 密码、密钥口令等敏感字段会加密后再写入 YAML（明文不再保存）。
- **主密码（可选）**：首次启动时会询问是否设置主密码；设置后，配置中的密码与 SecretKey 等将改用主密码派生的密钥加密（前缀 xot4）。首次需要输入主密码时，输入成功后会将派生密钥经 DPAPI 加密保存到 `%LocalAppData%\xOpenTerm\masterkey.dat`，以后启动将自动读取、无需再输入；若该文件不存在或校验失败则仍会提示输入主密码。未设置主密码时仍使用程序内固定密钥（同一份配置可在任意机器解密）。
- 未使用主密码时，各版本密钥在程序内按版本号派生（固定种子），同一份配置文件可在任意机器上解密，无需配置环境变量。
- 旧版曾用 Windows DPAPI 的密文（前缀 xot1）仍可解密（仅限原机器当前用户）。
- **导出**：导出 YAML 时节点与凭证以**解密后的明文**写出，便于迁移与备份。

## 其他特性

- 无内嵌 RDP 窗口（RDP 通过 mstsc 启动系统远程桌面）
- 无「远程文件」面板与 `list_remote_dir`
- 终端为自定义绘制 VT100 终端（ANSI 颜色/SGR、仅绘制可见行，无 xterm.js）
- 隧道链配置与选择已支持，SSH 支持直连与多跳（经跳板机链本地端口转发连接目标）
- 腾讯云同步功能
