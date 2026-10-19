# TypeLingo · 伴译

**Where you type, translation follows.**

面向 Windows 10/11 的 C# / .NET 8 / WPF 桌面翻译助手。输入停顿后，通过所选 AI 服务翻译当前已授权输入框中的文本，在旁边显示译文。**自动操作只预览；原文仅在你主动请求并确认替换后才写入。**

当前版本为 v0.2.0，提供 Windows 11 设置风格界面与所有应用监控模式。已完成 Windows x64 自包含打包，Linux、Windows 核心测试各 106 项通过，真实 Windows WPF 窗口检查 12 项通过。中文安装向导、五家真实账户翻译、跨应用与输入法兼容性仍需按下文验收。

## 下载安装包

- [Windows x64 中文安装包](https://github.com/Linkyyy/TypeLingo/releases/download/v0.2.0/TypeLingo-Windows-x64.zip)
- [完整源码 ZIP](https://github.com/Linkyyy/TypeLingo/releases/download/v0.2.0/TypeLingo-Source.zip)
- [版本发布页面](https://github.com/Linkyyy/TypeLingo/releases)
- [安装包备用下载](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/main/downloads/TypeLingo-Windows-x64.zip)

完整解压后直接运行 TypeLingo.exe，或运行「安装到当前用户.cmd」安装中文快捷方式。安装包内附中文说明。

## 快速启动（Windows）

开发需 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，版本见 `global.json`。普通用户运行，无需管理员权限。

```powershell
dotnet restore TypeLingo.sln --locked-mode
dotnet build TypeLingo.sln -c Release --no-restore
dotnet test tests/TypeLingo.Tests -c Release --no-build
dotnet run --project src/TypeLingo.App -c Release --no-build
```

1. 首次运行打开设置，到「AI」选择 DeepSeek、OpenAI、Claude、Gemini 或通义千问。可点击「打开官方 API 开通 / 购买平台」获取对应的密钥；每家服务与 API 地址分别保存密钥。
2. 输入密钥后，点击「查询可用模型」，从列表选择，或手动填写模型 ID。点击「测试翻译（固定短句）」发送 Hello 测试（可能产生少量 API 费用）。默认模型仅为可编辑起点，不会静默换成其他模型。
3. 在「隐私与应用」明确勾选授权。默认开启「监控所有应用」，无需逐个填写进程名；密码框、敏感控件、排除的应用和伴译自身仍跳过。关闭该开关可改用指定进程允许列表，排除列表始终优先。
4. 在「常规设置」设置界面语言（简体中文、英语、日语）与翻译方向：中译英、英译中、中译日、日译中、英译日、日译英，或自动识别后译成中/英/日。界面语言和翻译方向独立；还可选择自然、直译、商务、敬语风格。保存后关闭窗口，程序保留在系统托盘。
5. 在允许应用中输入，停顿 500 ms 后显示预览。按 `Ctrl+Alt+Enter` 或点击「替换…」查看确认，再点击「确认替换」。取消、内容变化、焦点变化或控件不支持安全写入时不替换。
6. 托盘「暂停全部读取」或 `Ctrl+Alt+Space` 暂停整个读取与翻译流程；`Ctrl+Alt+C` 复制当前译文。快捷键可以修改，冲突会提示。

所有应用模式或将浏览器加入允许列表，都会覆盖该进程的可访问输入框，无法按网站区分。密码字段与常见敏感标签会被排除，但无法识别任意敏感内容；处理敏感内容前可随时暂停。

## 中文安装包与独立 EXE

```powershell
./scripts/publish-windows.ps1
python scripts/package-release.py
# artifacts/TypeLingo-Windows-x64.zip
# artifacts/TypeLingo-Source.zip
```

解压 Windows 包后可直接运行 TypeLingo.exe，也可运行「安装到当前用户.cmd」校验并安装到当前用户的程序目录、生成中文快捷方式。安装向导不需要管理员权限，不修改安全策略，也不设置开机自启。安装与卸载说明均为中文。源码注释统一为中文，模型协议字段和多语言界面的实际文案按其用途保留。

这是包含 .NET 运行时的 Windows x64 单文件包，不要求目标电脑安装 SDK。首次运行会由 .NET 解出原生组件，包括 SQLite。未启用裁剪，避免破坏 WPF / UIA。调试 PDB 不必随 EXE 分发。此构建未签名，包含中文当前用户安装/卸载脚本，尚无单独的 MSI 安装器或自动更新器。

Linux 也能交叉发布：

```bash
dotnet publish src/TypeLingo.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:NuGetLockFilePath=packages.win-x64.lock.json -p:RestoreLockedMode=true \
  -o artifacts/win-x64
```

普通构建与单文件发布使用不同的依赖锁文件，防止发布过程改变常规开发依赖图。GitHub Actions 已执行 Linux / Windows 构建和核心测试；发布流程在真实 Windows WPF 中验证设置窗口，并提供截图与测试报告。

## 已实现

| 能力 | 实现 |
| --- | --- |
| 设置界面 | Windows 11 风格左侧导航、设置搜索、面包屑标题、分组卡片、开关与深浅主题 |
| 应用范围 | 默认覆盖全部可访问应用，可切换回指定进程；密码、敏感区域与排除项始终跳过 |
| 输入监视 | 后台 MTA 上的 UI Automation 焦点与文本事件，加 300 ms 节流轮询；不记录全局按键 |
| 实时请求 | 500 ms 防抖、700 ms 最小间隔、精确去重、取消、单个主请求、序号与配置校验 |
| AI | 五家真实 API 协议，Claude / Gemini 使用原生接口，其余使用兼容聊天接口；非流式、模型查询与手填、固定短句测试、官方开通入口、超时和有限 429/5xx 重试 |
| 多语言 | 简体中文 / English / 日本語即时切换并保存；九种翻译方向与翻译风格独立设置 |
| 缓存 | 有界内存 LRU / TTL；可选 SQLite、默认关闭、DPAPI 加密、容量与过期清理、一键清除 |
| 悬浮窗 | 不激活、不移走输入焦点、右/下/左/上定位、屏幕工作区约束、PerMonitorV2 DPI、深浅主题 |
| 主动替换 | 一次性确认票据，写入前再次检查目标、原文、焦点、授权、配置与 IME；仅使用 ValuePattern.SetValue |
| 设置与统计 | 语言、风格、限流、防抖、API、缓存、悬浮窗、快捷键、应用允许/排除、授权、请求与缓存统计 |
| 生命周期 | 托盘运行、暂停、清缓存、退出；释放 UIA 订阅；自身替换后的回声抑制 |

对无法读取、只读或失效的输入框跳过操作。仅有 TextPattern 的可编辑控件可预览，但不写入。没有剪贴板自动粘贴后备、SendInput、模拟 Enter、自动提交、注入、驱动或提权逻辑。复制按钮仅在主动点击/按快捷键时覆盖剪贴板。

## API 与配置

支持 DeepSeek、OpenAI、Claude（Anthropic）、Gemini（Google）和通义千问（阿里云百炼）。详见 [API 开通与模型选择](docs/API开通与模型选择.md)。模型列表使用真实服务响应，允许手动输入模型 ID；列表权限不足不妨碍使用正确的模型 ID 进行固定短句测试。

每个服务有固定的官方开通入口，不会根据自定义 API 地址构造购买链接。密钥按服务商和 API 基址隔离。Claude 使用原生 Messages API，Gemini 使用原生 generateContent API，其他三个使用 Chat Completions；所有服务保留 HTTPS 校验并禁止自动跟随重定向。密钥不会放入 URL 或日志。

本次环境未提供任何真实 API Key，五家服务均未进行真实账户付费调用。默认模型 ID 仅作初始示例，需以账户实际可用模型为准。生产代码调用真实接口，自动测试使用隔离 HTTP handler 验证序列化、请求头、响应与错误分支。

设置保存在 `%LOCALAPPDATA%\TypeLingo`：

- `settings.json`：不含 API Key 的普通配置；示例见 [docs/settings.example.json](docs/settings.example.json)。通常通过 UI 修改。
- `api-key.<配置哈希>.dpapi`：按服务商与 API 基址分别保存的当前 Windows 用户 DPAPI 密钥。旧版 `api-key.dpapi` 只兼容原来的 DeepSeek 官方基址。迁移电脑/用户后应重新填写，不能把加密文件当成可移植凭证。
- `translations.db`：主动开启磁盘缓存后创建；键为 SHA-256，译文 JSON 使用 DPAPI 加密，不保存原文。关闭缓存不会删除旧数据，需点击「清除全部缓存」。

缓存键保留原文的空格、换行、标点，包含 Provider、Model、源语言、目标语言、风格、PromptVersion、原文及 Base URL。切换模型/语言/服务地址不会复用错误译文。本地缓存命中时完全不请求 API，与 DeepSeek 服务端上下文缓存独立。

不写入原文、译文、API Key 或服务错误响应体日志。当前统计仅在进程内保留，退出清除内存；托盘暂停是本次会话的状态，跨重启需要在设置中关闭实时翻译并保存。

## 设置界面

左侧分为主页、常规设置、AI 服务与模型、翻译缓存、外观与悬浮窗、快捷键、隐私与应用。顶部可搜索设置名称，例如「密钥」「字体」「缓存」，支持当前界面语言。右侧使用 Windows 11 风格卡片和控件，底部保存按钮始终可见。默认浅色主题，也可保存为深色主题。

[查看实际 Windows 设置界面](https://github.com/Linkyyy/TypeLingo/releases/download/v0.2.0/settings-general.png) · [查看所有应用设置](https://github.com/Linkyyy/TypeLingo/releases/download/v0.2.0/settings-privacy.png)

## 测试与手工验收

```powershell
dotnet test tests/TypeLingo.Tests -c Release --logger trx
dotnet run --project tools/TypeLingo.UiSmoke -c Release -- artifacts/ui-smoke
dotnet run --project tools/TypeLingo.InputLab
```

UiSmoke 在真实 Windows WPF 中启动设置窗口，检查所有应用开关、导航搜索、多语言、预设、可编辑模型框和设置保存，并生成 PNG 截图。它不调用付费 API，也不读取其他应用内容。

InputLab 提供 A/B 多行输入、密码与只读控件，用真实 UIA 路径验收；它不是翻译模拟器。默认所有应用模式下无需单独添加 InputLab；也可关闭所有应用模式并只允许 **`TypeLingo.InputLab`**，然后，按 [Windows 验收清单](docs/ACCEPTANCE.md) 执行 A–H。自动测试包含防抖、缓存、取消、过时响应、置换确认、并发编辑、隐私、HTTP 错误与屏幕布局；[验证记录](docs/VALIDATION.md) 区分已执行与未执行项目。

## 云开发

当前云环境为 Linux，WPF/UIA 不能运行；可运行 Core / Infrastructure 测试并交叉构建 Windows 产物。

```bash
bash scripts/setup-cloud.sh
source scripts/dotnet-env.sh
dotnet test tests/TypeLingo.Tests -c Release --no-build
```

脚本使用 `/workspace/.dotnet` 和工作区 NuGet 缓存，通过微软官方 SHA-512 验证 SDK。`.NET 8` 的命名互斥锁还需要操作系统用户的 `/home/agent/.dotnet` 可写；受限任务应通过正常权限机制授予此目录访问，而不更改 HOME、证书或代理。每个任务已隔离，使用现有 checkout，不需要创建 Git worktree。

云网络需软件包预设、`builds.dotnet.microsoft.com`、`api.nuget.org` 等下载域名。DeepSeek、OpenAI、Anthropic、Gemini 与通义千问所需 API 域名已加入环境配置草稿，需在环境设置中保存并发布后才可用于新配置。桌面密钥在 Windows 设置界面输入，不写到仓库，也不需要在聊天里发送。

## 架构与兼容性

`src/TypeLingo.Core` 保存独立于 Windows 的协调、快照、隐私、定位与替换协议；`Infrastructure` 实现 HTTP、缓存及设置；`App` 实现 WPF、托盘、UIA、IMM、DPAPI 与热键。详见 [架构说明](docs/ARCHITECTURE.md)。

UIA 并不覆盖所有 Chrome/Edge、Electron、聊天和富文本控件。只操作同权限、当前焦点的可编辑控件；不尝试突破其他程序的安全边界。多窗口、混合 DPI、现代记事本与具体 IME 需要逐项在真实 Windows 上验收。

传统 IMM 组合输入可检测；现代 TSF/虚拟控件不保证公开组合状态。检测不到可靠组合状态时，中/日/韩输入布局下的非原生控件只预览/复制，不进行替换；原生 Edit/RichEdit 也必须取得可验证的 IMM 状态。预览依靠稳定停顿，个别 TSF 控件仍可能短暂预览未提交文字。UIA 无法提供跨进程原子比较并写入，因此仍有极小并发竞态；代码在 SetValue 前尽量贴近写入点复检，不能宣称对任意第三方控件具备事务保证。不可验证时请使用复制。

尚未实现 Phase 3 的纠错、离线模型、选中文本翻译等功能，也未完成所有 Phase 2 的跨应用兼容性认证与动画。扩展 Provider 可实现 `ITranslationProvider`，不用更改主要协调逻辑。
