# xOpenTerm UI/UX Pro Max 审查报告

基于 UI/UX Pro Max 规则对界面**显示**与**交互**的审查结果与优化建议。适用技术栈为 WPF（桌面），规则中的「触控」对应桌面上的**点击目标大小**与**键盘/焦点**。

---

## 一、无障碍（CRITICAL）

### 1.1 色彩对比度 (color-contrast)

| 位置 | 前景 | 背景 | 说明 |
|------|------|------|------|
| 主窗口/设置/凭证列表 | TextPrimary #E5E7EB | BgDark #111827 / BgSidebar #1F2937 | 正文对比度充足 |
| 主窗口 | TextSecondary #9CA3AF | BgSidebar #1F2937 | 次要文字，建议保持 ≥4.5:1 |
| 节点编辑/关于/凭证编辑 | #e2e8f0 / #94a3b8 | #1a1a2e / #1e293b | 与全局主题不一致，见「一致性」 |

**建议**：全应用统一使用 `App.xaml` 中的 `TextPrimary` / `TextSecondary`，避免部分窗口硬编码颜色导致对比度不统一。

### 1.2 焦点状态 (focus-states)

- **现状**：未在 `App.xaml` 或窗口级设置 `FocusVisualStyle`，依赖 WPF 默认。深色主题下默认焦点框往往不明显。
- **建议**：在 `Application.Resources` 中为 `Control`/`Button` 等设置统一的 `FocusVisualStyle`，使用与 Accent 一致的边框或高亮，确保键盘 Tab 导航时焦点可见。

### 1.3 表单与标签 (form-labels)

- **现状**：输入框旁使用 `TextBlock` 作为标签，未使用 `Label` 的 `Target` 或 `AutomationProperties.LabeledBy`，对读屏与自动化不友好。
- **建议**：对关键表单（节点编辑、凭证编辑、设置）为重要输入绑定 `AutomationProperties.Name` 或使用 `Label`+`Target`，提升可访问性。

---

## 二、触控与交互（CRITICAL → 桌面等价）

### 2.1 点击目标大小 (touch-target-size → 最小可点区域)

| 元素 | 现状 | 建议 |
|------|------|------|
| 标签页关闭按钮 | 20×20 px | 至少 28×28 或 32×32，或增大 Padding 使可点区域 ≥28px |
| 菜单项 / 左侧 Tab | Padding 10,6 / 10,6 | 已接近 44px 高度，可接受 |
| 通用按钮 | Width 70–80, Padding 6–8 | 高度略小，建议 MinHeight≥28，Padding 保证视觉与可点一致 |

**建议**：标签页关闭按钮改为至少 28×28 逻辑像素，并增加 hover 背景，避免误点且反馈明确。

### 2.2 悬停与可点击反馈 (hover-vs-tap, cursor-pointer)

- **已具备**：左侧 Tab 的 `Cursor="Hand"`、关闭按钮代码中 `Cursor = Cursors.Hand`。
- **缺失**：标签页关闭按钮仅有文字「×」，无 hover 背景，易被忽略为可点击。
- **建议**：为关闭按钮增加 hover 时背景（如 `HoverBg` 或半透明高亮），过渡约 150–200ms。

### 2.3 异步操作时的按钮 (loading-buttons)

- **现状**：「测试连接」为同步调用，执行期间界面会阻塞，且按钮未禁用，用户可能多次点击。
- **建议**：将测试连接改为异步（如 `async/await`），在请求进行中设置 `TestConnectionBtn.IsEnabled = false` 并显示「测试中…」，结束后恢复并给出结果，符合 loading-buttons 规则。

### 2.4 错误反馈 (error-feedback)

- 已具备：连接失败、必填项为空等均有 MessageBox 或终端内红色文案，符合「错误信息靠近问题」的预期。

---

## 三、性能与布局（HIGH）

### 3.1 远程文件加载状态 (content-jumping)

- **现状**：`doc/UI设计原则检查.md` 已建议：加载中可增加「加载中…」或禁用列表，避免用户误以为无数据。
- **建议**：在 `LoadRemoteFileList` 请求期间显示加载指示（如列表顶部 TextBlock「加载中…」或禁用列表），数据返回后更新，减少内容跳动与误判。

### 3.2 主窗口尺寸与左侧栏

- **现状**：`MinWidth="180"`、默认 Width 260，可拆分调整，布局合理。
- **建议**：保持当前设置；若支持高 DPI，确认最小尺寸在缩放后仍便于操作。

---

## 四、一致性与视觉（MEDIUM）

### 4.1 主题与配色一致 (consistency)

- **问题**：`NodeEditWindow.xaml`、`CredentialEditWindow.xaml`、`AboutWindow.xaml` 使用硬编码颜色（如 `#1a1a2e`、`#e2e8f0`、`#94a3b8`、`#1e293b`），与 `App.xaml` 的 `BgDark`、`TextPrimary`、`TextSecondary`、`BgInput` 等不一致，且与主窗口 HandyControl 深色皮肤脱节。
- **建议**：上述窗口改为引用 `{StaticResource BgDark}`、`{StaticResource TextPrimary}`、`{StaticResource BgInput}` 等，保证全应用一套深色主题，便于后续支持浅色或主题切换。

### 4.2 动效与过渡 (duration-timing)

- **现状**：未使用 WPF 的 `Trigger`/动画做 hover 过渡。
- **建议**：对关闭按钮、列表行等可点击区域增加 150–300ms 的背景/前景过渡，提升「稳定悬停」感受。

### 4.3 图标与品牌 (no-emoji-icons)

- **已具备**：未使用 emoji 作为功能图标，远程文件列表使用 `IconImage` 绑定，符合规则。

---

## 五、已符合的要点

- **Esc/Enter**：各对话框 `IsCancel="True"`、主操作 `IsDefault="True"`（设置、节点编辑已设）。
- **约束与容错**：端口 `MaxLength="5"`、删除前确认、未选时编辑/删除禁用。
- **反馈**：保存后关闭并刷新、测试连接结果明确、错误有 MessageBox。
- **映射**：双击连接、右键菜单、标签关闭 × 符合常见心智模型。

---

## 六、优化项汇总（建议实施顺序）

| 优先级 | 项 | 类型 | 说明 |
|--------|----|------|------|
| P0 | 焦点样式 | 无障碍 | 在 App.xaml 增加全局 FocusVisualStyle |
| P0 | 主题一致 | 一致性 | NodeEdit / CredentialEdit / About 使用 App 资源 |
| P1 | 标签页关闭按钮 | 交互 | 增大可点区域 + hover 背景 |
| P1 | 凭证编辑默认键 | 交互 | CredentialEditWindow 保存按钮 IsDefault="True" |
| P2 | 测试连接 | 交互 | 异步 + 按钮禁用 + 「测试中…」 |
| P2 | 远程文件加载 | 反馈 | 加载中显示「加载中…」或禁用列表 |
| P3 | 表单可访问性 | 无障碍 | AutomationProperties 或 Label.Target |

---

## 七、现代化风格更新（UI/UX Pro Max 设计系统）

基于设计系统 **Developer Tool / IDE**（Code dark + run green）对整体界面做了现代化风格调整：

| 项 | 说明 |
|----|------|
| **配色** | BgDark #0F172A、BgSidebar #1E293B、BgInput #334155、Accent #22C55E（绿色强调）、TextPrimary #F8FAFC |
| **圆角** | 新增 RadiusSm/RadiusMd/RadiusLg（4/6/8），侧栏右侧 8px 圆角、Tab 与卡片使用 4–6px |
| **字体** | 全局 Window 默认 Segoe UI、14px |
| **间距** | 菜单与左侧 Tab 内边距略增，设置/凭证窗口边距与按钮 Padding 增大 |
| **分割线** | 主窗左右分割条 6px 宽，更易拖拽 |

涉及文件：`App.xaml`、`MainWindow.xaml`、`SettingsWindow.xaml`、`CredentialsWindow.xaml`。

---

## 八、本次已实施的优化（代码变更）

| 项 | 文件 | 说明 |
|----|------|------|
| 焦点样式 | `App.xaml` | 新增 `FocusVisual` 样式（Accent 色 2px 边框）；全局 `Button` 应用该焦点样式；新增 `TabCloseButtonStyle` |
| 标签页关闭按钮 | `App.xaml` + `MainWindow.Tabs.cs` | 关闭按钮使用 `TabCloseButtonStyle`：MinWidth/MinHeight 28、hover 使用 `HoverBg`、手型光标、焦点环 |
| 主题一致 | `NodeEditWindow.xaml` | 背景/前景/输入框/次要文字均改为 `StaticResource BgDark/TextPrimary/BgInput/TextSecondary` |
| 主题一致 | `CredentialEditWindow.xaml` | 同上，并设保存按钮 `IsDefault="True"` |
| 主题一致 | `AboutWindow.xaml` | 同上；GitHub 链接使用 `Accent` |

未改动的建议（可后续做）：测试连接改为异步并禁用按钮、远程文件加载中提示、表单 AutomationProperties。

---

## 九、与现有《UI设计原则检查》的关系

- 本报告在**同一产品**上，按 **UI/UX Pro Max** 的规则（无障碍、触控/点击、性能、一致性、动效等）做补充审查。
- 原有七条原则（可见性、反馈、约束、一致性、映射、容错、恢复）仍成立；本报告侧重**显示与交互**的细节（对比度、焦点、点击区域、主题统一、加载状态），两者可合并为一份总检查清单使用。
