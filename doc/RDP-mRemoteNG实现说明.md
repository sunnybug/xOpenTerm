# 内嵌 RDP：mRemoteNG 实现方式说明

## 当前实现（与 mRemoteNG 一致）

| 项目 | 内嵌 RDP 控件来源 | 是否依赖额外安装 |
|------|-------------------|------------------|
| **mRemoteNG** | 系统自带 **MSTSCAX**（mstscax.dll），通过 AxMSTSCLib / MSTSCLib 互操作 | 否，仅需 Windows |
| **xOpenTerm** | 同上：系统 **MSTSCAX**，通过 aximp 生成的 AxMSTSCLib / MSTSCLib 互操作（`RdpEmbeddedFormMstsc`） | 否，仅需 Windows；构建时需 `src/References/` 下已有互操作 DLL，见 README |

xOpenTerm 已改为与 mRemoteNG 相同方式，直接使用系统远程桌面 ActiveX 控件，不依赖 MsRdpEx。

## mRemoteNG 的实现要点（参考其源码）

- **控件类型**：`AxMsRdpClient6NotSafeForScripting`（基类）/ `AxMsRdpClient8NotSafeForScripting`（高版本），对应 COM 类 `MsRdpClient8NotSafeForScripting`，CLSID `54d38bf7-b1ef-4479-9674-1bd6ea465258`。
- **创建方式**：`Control = new AxMsRdpClient8NotSafeForScripting()`，加入窗体后 `Control.CreateControl()`，再通过 `(MsRdpClient8NotSafeForScripting)((AxHost)Control).GetOcx()` 取得底层 COM 对象。
- **连接前设置**（节选）：
  - `_rdpClient.Server = connectionInfo.Hostname`
  - `_rdpClient.AdvancedSettings2.RDPPort = port`
  - `_rdpClient.UserName` / `_rdpClient.Domain` / `_rdpClient.AdvancedSettings2.ClearTextPassword`
  - `_rdpClient.AdvancedSettings7.EnableCredSspSupport = true`（NLA）
  - `_rdpClient.AdvancedSettings2.SmartSizing`（智能缩放）
  - `_rdpClient.AdvancedSettings6.RedirectClipboard`
  - 控制台会话：`_rdpClient.AdvancedSettings7.ConnectToAdministerServer = true`
- **连接**：`_rdpClient.Connect()`（异步），通过事件 `OnConnecting`、`OnConnected`、`OnLoginComplete`、`OnFatalError`、`OnDisconnected` 处理状态与错误。
- **承载方式**：与 xOpenTerm 类似，在**独立 STA 线程**的 WinForms 窗体中承载 RDP 控件，再通过 SetParent 嵌入到 WPF，避免在 WPF 消息循环中触发 SEHException。

详见：<https://github.com/mRemoteNG/mRemoteNG> 中 `mRemoteNG/Connection/Protocol/RDP/RdpProtocol.cs`、`RdpProtocol8.cs`。

## 在 xOpenTerm 中改用系统 MSTSCAX 的步骤（可选）

.NET 8 SDK 不支持在 csproj 里直接使用 `COMReference` 生成互操作，需要**手动生成**互操作程序集后引用：

1. **生成互操作 DLL**（需本机已安装 Visual Studio 或 Windows SDK，带 `aximp.exe`）：
   ```powershell
   # 执行 script\gen_mstsc_interop.ps1，或在有 aximp 的机器上手动执行：
   aximp.exe C:\Windows\System32\mstscax.dll
   ```
   会得到 `AxInterop.MSTSCLib.dll` 与 `Interop.MSTSCLib.dll`。

2. **放入项目并引用**：将两个 DLL 放到例如 `src/References/`（或 `lib/MSTSCLib/`），在 `xOpenTerm.csproj` 中增加：
   ```xml
   <ItemGroup>
     <Reference Include="AxInterop.MSTSCLib">
       <HintPath>References\AxInterop.MSTSCLib.dll</HintPath>
     </Reference>
     <Reference Include="Interop.MSTSCLib">
       <HintPath>References\Interop.MSTSCLib.dll</HintPath>
     </Reference>
   </ItemGroup>
   ```

3. **实现基于系统控件的 Form**：新建例如 `RdpEmbeddedFormMstsc.cs`，参考 mRemoteNG 的 `RdpProtocol` / `RdpProtocol8`：
   - 使用 `AxMsRdpClient8NotSafeForScripting` 作为控件，加入 Form；
   - 在 Connect 前设置 Server、Port、UserName、Domain、ClearTextPassword、SmartSizing、RedirectClipboard、UseConsoleSession 等；
   - 订阅 OnConnected、OnDisconnected、OnFatalError 等，并转发到现有 `RdpEmbeddedSession` 的 Connected/Disconnected/ErrorOccurred。

4. **切换入口**：在 `RdpEmbeddedSession.RdpThreadProc` 中改为 `new RdpEmbeddedFormMstsc(...)` 并移除对 RoyalApps `RdpControl` 的依赖；可同时从 csproj 去掉 `RoyalApps.Community.Rdp.WinForms` 包引用。

按上述方式即可在 xOpenTerm 中实现与 mRemoteNG 一致、仅依赖系统 mstscax 的内嵌 RDP，无需安装 MsRdpEx。
