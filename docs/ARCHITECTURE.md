# TypeLingo 实现说明

## 数据流

```text
Windows UIA (专用后台 MTA)
  → PrivacyGuard（授权 + 进程允许/排除 + 控件敏感性）
  → InputSnapshot（运行时控件 ID / 前台窗口 / 原文 / 坐标 / 写入能力）
  → TranslationCoordinator
      → 精确去重 / 取消旧版本 / 查询本地缓存
      → 未命中：防抖 → 请求限流 → 单请求信号量 → ITranslationProvider
      → 校验序号 / 快照 / 配置 → Preview
  → WPF Dispatcher → 不激活的 OverlayWindow

RegisterHotKey / 主动点击
  → ReplacementService.PrepareAsync
  → 读取最新快照，必要时等到最新翻译完成
  → 原文 + 译文确认票据（仅一次）
  → 用户确认 → 再读快照 / 校验配置
  → InputObserver.TryReplaceAsync 再次复检 → ValuePattern.SetValue
  → 抑制自身替换引发的重复翻译
```

## 边界

- UIA 的 COM 调用不进入 WPF UI 线程。焦点和文本事件在后台 MTA 注册/移除；300 ms 轮询用于事件不完整的提供程序、位置变化和 IME 状态。未授权或暂停时不获取焦点元素或文本，移除订阅。
- 快照 ID 组合进程 ID、前台窗口及 UIA RuntimeId。不能把不同输入框中相同原文视为同一个替换目标。
- 每次文本、焦点、写入能力或翻译配置变化都使旧版本失效。即使 Provider 忽略 CancellationToken，旧结果也不能显示，信号量保证不会同时启动第二个主翻译。
- 缓存键序列化为 JSON 字符串数组后做 SHA-256，避免字符串直接拼接的边界碰撞。原文不作 trim、换行或空格归一化。
- 内存缓存为受信号量保护的字典 + 双向链表 LRU；SQLite 与内存操作在同一门内串行。磁盘仅存哈希、DPAPI 密文与时间戳，过期及容量清理发生在访问时。普通查询不延长 TTL。
- HTTP 每次尝试独立限时，缓冲响应体的读取也在超时内。仅重试 429 / 5xx；Retry-After 与退避延迟有 10 秒上限，最多 4 次重试（默认 2）。取消用户请求不算失败。错误体不传播到 UI 或日志；不接受截断或空白译文。
- Provider 使用独立 system/user 消息，原文永远在 user 内容中，不拼成系统指令。提示词要求把输入视为待翻译数据；无法保证 AI 永远遵循提示，因此返回结果仅供预览，写入仍需确认。
- WPF Overlay 使用 WS_EX_NOACTIVATE、WM_MOUSEACTIVATE / MA_NOACTIVATE 与 SWP_NOACTIVATE；按钮可点但输入焦点留在目标窗口。确认不是抢焦点的 MessageBox。
- 没有剪贴板自动粘贴回退。TextPattern 无写入能力时仅显示/复制；不假设 SetValue 保留光标或撤销栈。

## 安全替换的实际边界

确认操作是可消费的一次性票据。准备后发生任何新快照/配置变更，票据失效；写入前再读当前快照并校验。Windows 适配器在自己的 MTA 上紧邻 SetValue 再次检查敏感性、焦点、进程、控件 ID、原文、ValuePattern 的只读属性与组合输入能力。

UIA 的跨进程调用无法提供原子的 compare-and-set；提供程序不可靠、目标 UI 线程卡住或最后检查后立即并发编辑是无法完全消除的限制。对于不能可靠判断组合状态的 TSF/虚拟控件，中/日/韩布局时禁止写入。对于未知的原生 provider，只能通过 Windows 验收决定是否适合加入允许列表。

## 文件与模块

| 模块 | 文件 |
| --- | --- |
| 领域协议 / 请求 / 快照 / 统计 | Core/Translation.cs |
| 隐私规则 / 非秘密配置 | Core/Settings.cs |
| 防抖 / 取消 / 版本 / 单请求 | Core/TranslationCoordinator.cs |
| 准备确认与一次性写入协议 | Core/ReplacementService.cs |
| 屏幕物理坐标定位 | Core/OverlayPlacement.cs |
| 五家 API / 原生协议 | Infrastructure/ProviderApi.cs |
| 内存 + 加密磁盘缓存 | Infrastructure/CacheService.cs |
| 原子配置与 Key 文件写入 | Infrastructure/SettingsService.cs |
| 焦点 / 文本观察 / UIA 写入 | App/Windows/InputObserver.cs |
| 注册热键 / Native / DPAPI | App/Windows/ |
| 托盘与生命周期 | App/App.xaml.cs |
| 设置与非激活预览 | App/SettingsWindow.*, App/OverlayWindow.* |

窗口代码仅负责编排 Windows UI，核心协调与替换规则可以跨平台测试。测试中的 Provider/HTTP handler/加密器仅用于隔离测试；生产路径不使用模拟译文。

## 本轮新增：界面、方向与五家服务

`UiCatalog.json` 保存中英日界面文本，中文是默认与回退语言；WPF 动态资源同步刷新按钮和说明。`AppSettings.UiLanguage` 独立于翻译请求，不进入缓存键，也不会更改目标语言。`TranslationPresets` 仅更新源语言与目标语言，保留用户选择的风格、防抖和限流。

`ProviderCatalog` 保存五家服务的官方入口与可编辑默认设置。`ProviderApi` 按请求中的 Provider 分派兼容聊天、Claude Messages 和 Gemini generateContent 协议，共用超时、取消和有限重试。模型列表可选择，模型 ID 也可手动输入。

密钥文件名包含服务商与完整 API 基址的哈希；查询、测试和实际翻译均按本次请求的配置读取对应密钥，不通过全局单一 Key 猜测认证信息。旧版 DeepSeek 密钥不能回退给其他服务。切换提供商会清空未保存的密钥输入。
