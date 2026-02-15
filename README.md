# xOpenTerm

参考 [xOpenTerm](https://github.com/your-org/xOpenTerm) 实现的 **C# WPF** 版本：Windows 下 SSH / RDP 批量管理工具。

## 功能

- **节点树**：分组、SSH、RDP；支持拖拽节点到其他分组；分组节点显示其下可连接节点（SSH/RDP）数量
- **连接管理**：双击或右键「连接」打开标签页，支持多标签
- **SSH**：全部使用内嵌 PuTTY/PuTTY NG（直连与多级跳板一致）；认证下拉（密码/私钥/同父节点/SSH Agent/登录凭证）、跳板机多选、节点/凭证/隧道内「测试连接」；多级跳板通过 Plink 的 -proxycmd 链实现，需与 PuTTY 同目录的 plink.exe；私钥为非 .ppk 时会优先使用同路径的「原路径.ppk」，若无则进程内转换为 .ppk（支持 RSA/DSA/ECDSA/Ed25519，无交互），不支持的格式再尝试 puttygen
- **SSH 状态栏**：标签页底部显示连接状态、CPU/内存占用率、网络流量、TCP/UDP 连接数；磁盘占用率按物理硬盘显示（格式：硬盘sda 45% 硬盘nvme0n1 78%，每 3 分钟远程拉取一次）；磁盘占用超过 90% 时以红色显示；磁盘区域右键「查找占用空间最大的文件/目录」可复制按远程系统生成的命令（优先 ncdu，否则 du），未安装时提示安装命令
- **维护 - 磁盘占用**：节点（分组或 SSH 主机）右键菜单 → 维护 → 磁盘占用；输入占用率阈值（默认 85%），对当前节点或分组下所有 SSH 子节点执行检查；超过阈值的节点会列出并采集根目录下占用最大的文件/目录
- **RDP**：内嵌 RDP 标签页使用系统 **MSTSCAX**（mstscax.dll），参考 mRemoteNG，不依赖 MsRdpEx；或通过 mstsc 启动；支持域、控制台会话、剪贴板重定向、智能缩放；临时 .rdp 与可选 cmdkey 凭据；默认端口 3389、用户名 administrator
- **顶栏菜单**：设置 → 登录凭证、隧道管理、恢复配置；帮助 → 关于、检查更新
- **配置备份与恢复**：配置文件修改时自动备份到 `%LocalAppData%\xOpenTerm\backup\YYMMDD-HHMMSS\`（60 秒防抖）；设置 → 恢复配置可打开备份列表，按时间与大小显示，支持打开备份目录或恢复（恢复前会先备份当前配置）
- **登录凭证**：独立管理窗口，可被多个节点引用
- **隧道管理**：跳板机增删改查与测试连接
- **腾讯云同步**：从腾讯云 API 导入服务器列表，支持增量更新；多地域并行拉取（CVM、轻量应用服务器）
- **阿里云同步**：从阿里云 ECS 与轻量应用服务器 API 导入服务器列表，按地域→服务器构建节点树，支持增量更新；多地域并行拉取
- **金山云同步**：从金山云 KEC API 导入服务器列表，按地域→服务器构建节点树，支持增量更新；多地域并行拉取
- **云服务器详情**：各云服务（腾讯云 CVM/轻量、阿里云 ECS/轻量、金山云 KEC）下的服务器子节点，右键菜单提供「详情」，用默认浏览器打开该实例在控制台的详情页
- **导入 MobaXterm**：从 MobaXterm.ini 按目录多选导入 SSH/RDP 会话；可选提供 MobaXterm 导出的密码文件，格式为 `配置名(用户名) = 密码`，若会话用户名为 `[配置名]` 则自动填充真实用户名与密码；若 SSH 节点用户名为非 `[xxx]` 且无密码、无密钥，则导入后自动设为 SSH Agent 认证
- **关于 / 更新**：版本号、作者、GitHub 链接；检查更新为直接更新（应用内下载安装包，确认后退出并由脚本覆盖安装目录并重启）
- **持久化**：节点、凭证、隧道保存为 YAML（`config/nodes.yaml`、`credentials.yaml`、`tunnels.yaml`，位于 exe 同目录下的 `config/`）；节点树的展开状态与选中项在关闭时写入 `config/settings.yaml`，下次启动时恢复
- **编辑框输入历史**：除密码与节点名外，各编辑窗口（SSH/RDP/隧道/凭证/分组/本地/云同步等）的文本框（如主机、端口、用户名、私钥路径、SecretId、AccessKeyId 等）会记住输入历史；输入时按文字过滤并以下拉列表展示，可点击历史项快速填入；保存时写入历史，持久化到 `config/input_history.yaml`，程序重启后有效
- **快捷键**：Ctrl+Q 聚焦节点树快速过滤框（无内容时显示提示「Ctrl+Q」），光标置于末尾

## 技术栈

- .NET 8 / WPF
- [SSH.NET](https://github.com/sshnet/SSH.NET)（SSH）
- [YamlDotNet](https://github.com/aaubry/YamlDotNet)（YAML）

## 项目结构

- `src/` — 源码（编译生成物在仓库根下 `.temp/`，如 `.temp/bin/Debug`、`.temp/obj/`）
- `test.ps1` — 构建并运行应用（支持 --release、--test-rdp、--test-ssh-status）
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

# 仅运行 SSH 状态获取单元测试（root@192.168.1.192，SSH Agent，连接超时 3s，无 UI，测试结束自动退出）
.\test.ps1 --test-ssh-status

# 初始化开发环境（还原依赖、创建 bin/config 等）
.\script\init_dev.ps1

# 发布到 dist（带版本号）
.\script\publish.ps1
```

或使用 Visual Studio 打开 `xOpenTerm.sln` 后 F5 运行。

### 内嵌 RDP 互操作程序集（开发/打包用）

内嵌 RDP 使用系统 mstscax.dll 的互操作（参考 mRemoteNG）。若从源码构建且 `src/References/` 下尚无 `AxInterop.MSTSCLib.dll`、`Interop.MSTSCLib.dll`，可运行（需已安装 Visual Studio 或 Windows SDK）：

```powershell
.\script\gen_mstsc_interop.ps1
```

脚本会将生成的两个 DLL 写入 `src/References/`，然后即可构建。GitHub Actions 构建时会自动执行该脚本；若 CI 环境无 aximp，可在本机运行脚本后执行 `git add -f src/References/*.dll` 并提交，即可在任意环境通过构建。

## 配置目录

首次运行后，在 exe 所在目录下会生成 `config/`，其中：

- `nodes.yaml`：服务器/分组树（与 xOpenTerm 数据结构兼容）
- `credentials.yaml`：登录凭证
- `tunnels.yaml`：SSH 隧道（跳板机）
- `input_history.yaml`：编辑框输入历史（主机、端口、用户名等非敏感字段，供下拉联想）

可从 xOpenTerm 复制上述 YAML 到本项目的 `config/` 使用。

### 配置中的密码加密与多机共用

- 节点/凭证/隧道 YAML 根节点包含 `version` 字段，不同版本使用不同的写死密钥与算法。
- 密码、密钥口令等敏感字段会加密后再写入 YAML（明文不再保存）。
- **主密码（可选）**：首次启动时会询问是否设置主密码；设置后，配置中的密码与 SecretKey 等将改用主密码派生的密钥加密（前缀 xot4）。首次需要输入主密码时，输入成功后会将派生密钥经 DPAPI 加密保存到 `%LocalAppData%\xOpenTerm\masterkey.dat`，以后启动将自动读取、无需再输入；若该文件不存在或校验失败则仍会提示输入主密码。未设置主密码时仍使用程序内固定密钥（同一份配置可在任意机器解密）。
- 未使用主密码时，各版本密钥在程序内按版本号派生（固定种子），同一份配置文件可在任意机器上解密，无需配置环境变量。
- 旧版曾用 Windows DPAPI 的密文（前缀 xot1）仍可解密（仅限原机器当前用户）。
- **导出**：导出 YAML 时节点与凭证以**解密后的明文**写出，便于迁移与备份。

## 其他特性

- RDP 支持内嵌标签页与 mstsc 两种方式；内嵌 RDP 在独立线程的 WinForms 消息循环中承载（通过 SetParent 嵌入），避免在 WPF 消息循环中触发 SEHException（参考 mRemoteNG）；节点可配置域、控制台会话、剪贴板重定向、智能缩放
- 远程文件面板：按节点复用 SFTP 长连接，同一节点下切换目录只发 SFTP 请求不重复建连；异步加载与按路径缓存，已访问目录即时显示；该节点所有连接 tab 关闭时自动释放长连接
- 无 `list_remote_dir` 等独立命令
- 终端为自定义绘制 VT100 终端（ANSI 颜色/SGR、仅绘制可见行，无 xterm.js）
- 隧道链配置与选择已支持，SSH 直连与多级跳板均走 PuTTY，多跳通过 Plink -proxycmd 链实现
- 腾讯云、阿里云、金山云同步功能

## 项目目录架构（my-project 技能）

### 技能描述

定义工程的目录结构，对整个项目的目录结构进行重新整理，确保每个目录的职责清晰，避免重复和混乱。

### 推荐目录架构

```
.gitignore
.cursorignore
README.md
.github/
  - publish.yml 当版本号变动时，打tag并发布版本到github的release
.dist/ 本地生成的发布版本存放在这里
script/ 脚本目录
  - publish.ps1 发布脚本 将release（带版本号）发布到dist目录
  - init_dev.ps1 初始化开发环境
  - build.ps1 构建脚本，用于构建前后端，默认是debug，若传参加上--release，则构建release
src/ 源码目录，若只有一个工程，则将代码放到该目录而不是再创建一个工程目录。src下面不应存在obj和bin等存放编译生成物的目录。
doc/ 文档目录
aidoc/ ai生成文档放到该目录
.temp/ 将编译过程所有中间文件和输出文件存放到该目录，同时修改编译相关脚本和工程
.run/ 运行时工作路径（test.ps1 与 dotnet test 单元测试均使用），其下 log/、config/
test.ps1
```

### test.ps1运行流程
1. 编译。调用build.ps1，默认是debug，若传参加上--release，则构建release
2. 强杀目标程序
3. 清除运行日志
5. 启动目标程序

### 关于.gitignore/vscode的files.exclude

#### 不进入.gitignore/vscode的files.exclude

- .vscode/
- .cursor/
- .claude/
- .trae/
- *.code-workspace

#### 入vscode的files.exclude

- .temp/
- obj/

#### 入gitignore

- obj/
- .run/

### .vscode

生成launch.json，用于调试，包含：

- 编译和运行debug版本
- 运行时若为web前端则自动打开浏览器访问
- 运行时若为普通exe则运行exe
- 运行时的工作目录为 .run 或项目根（按 launch 配置）

### 工程代码处理

#### 若工程代码已存在

- 将工程的工作目录设置为 .run（test.ps1）或项目根（或 launch 指定）
- 将配置文件（含旧内容和相关代码处理）放到工作目录下的 config/
- 项目根目录下的代码目录和工程文件都移到src/

#### log

- 修改log相关代码，将生成的log文件生成到工作目录/log/
- log分级为：DEBUG/INFO/WARN/ERR/FATAL，文件名：YYYY-MM-DD.log，log内容为：[YYYY-MM-DD HH:MM:SS] [LEVEL] [FILE:LINE] [MESSAGE]
- 崩溃或者异常产生log，文件名：YYYY-MM-DD_crash.log
- 除此外不要生成其他log文件
