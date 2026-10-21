# 点知 · DotKnow

**哪里不会，点哪里。**

像点读机一样的 Windows 桌面 AI 助手：在网页、文档和应用中划选文字，选择百科、翻译、解释、摘要、润色、代码解读，或输入自己的问题。用你选择的 AI 回答，随手复制、收藏，之后在本地复习。

点知由 TypeLingo · 伴译升级而来。翻译依然是重要功能：中英日互译、离线词库优先、在线译文自动积累和确认替换继续保留。离线词库只用于纯翻译，百科等任务使用在线模型，不会污染词库。

当前版本：**v0.5.0-preview.2**，Windows 10 / 11 x64。图形继续使用已选定的 C 标志；界面和官网统一为纯白、浅纸、深林、雾蓝、暮紫五套配色，并支持浅色、深色和跟随系统。

## v0.5.0-preview.2

- 新增纯白配色；下拉框宽度跟随当前选项文字，减少无用留白。
- 微信自绘文本无法通过辅助功能接口读取时，支持划选后临时复制所选文字，随后恢复原剪贴板。可在常规设置关闭；该路径仅可阅读、复制与收藏，不执行输入替换。
- 关闭或自动隐藏结果后，同一选区保持关闭，重新划选或按 Ctrl+Alt+T 可再次查询。
- 百科回答使用简洁阅读界面，原文和输入按需展开，支持继续追问。
- 解决方案、项目、命名空间、程序集和图标资源统一为 DotKnow，旧版数据保持兼容。

微信兼容已增加独立进程的自绘控件验收；实际微信版本与权限环境仍需在用户机器上确认。仓库远端改名需要 GitHub 管理 API，当前云环境尚未允许访问；现有仓库和下载地址继续有效。

验收：222 项核心测试、181 项 Windows 界面与跨进程检查、11 项官网浏览器检查通过。Windows 验收与安装包真实启动记录：[39520278016](https://github.com/Linkyyy/TypeLingo/actions/runs/39520278016)。

## 下载和开始使用

- [点知 Windows x64](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/DotKnow-Windows-x64.zip)
- [完整源码](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/DotKnow-Source.zip)
- [宣传素材与文案](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/DotKnow-Promotion.zip)
- [真实 Windows 界面](docs/UI_THEMES.md) · [验证记录](docs/VALIDATION.md)

完整解压后运行 **DotKnow.exe**。也可运行「安装到当前用户.cmd」，安装并创建点知快捷方式；不需要管理员权限或额外安装 .NET。更新前从系统托盘退出旧版点知或伴译。

1. 在「隐私与应用」授权读取选区，按需选择全部应用或应用允许列表。
2. 在「AI 服务与模型」选择 DeepSeek、OpenAI、Claude、Gemini 或通义千问，保存自己的 API Key 和可用模型。在线调用可能产生服务商费用。
3. 在「任务与提示词」选默认任务和回答语言。新用户默认百科、划选后先显示任务和问题面板，点击查询才发送；也能改为立即执行默认预设。
4. 在其他应用中划选文字。可直接使用预设，也可输入补充问题，按 **Ctrl+Enter** 查询。显示面板不会抢走原应用焦点，点击问题框后才开始输入，原选区会保留。
5. 有用的回答可复制、收藏，在「本地收藏与复习」按卡片温习。继续提问围绕当前选区独立查询，不会自动发送之前的整段对话。

只想离线翻译：选「翻译」预设，在「常规设置」选择自然风格和语言方向，在「离线题库」启用离线优先。选中“米饭”，目标日语可离线得到“ご飯”。不需要 API Key。内置 6,426 组中英日词条、32,159 条可用方向记录，不能代替任意句子的离线模型。

旧用户升级后保留即时翻译模式。设置、API 密钥、个人题库、缓存和收藏继续使用 `%LOCALAPPDATA%\TypeLingo`；当前用户安装目录也继续使用旧目录以便升级。无须导出再导入。

## 任务和提示词

| 预设 | 用途 |
| --- | --- |
| 百科 | 概念定义、背景和例子 |
| 翻译 | 中英日互译，离线优先与自动积累 |
| 通俗解释 | 用容易理解的话和类比讲清楚 |
| 提炼重点 | 概括选中文字的主旨和要点 |
| 写作润色 | 改进语法、行文和表达 |
| 代码解读 | 理解逻辑、输入输出和潜在问题 |
| 自由提问 | 围绕选区输入自己的问题 |

最多保存 20 套自定义提示词。只写任务指令，选区作为独立内容提供，无须占位符。内置预设只读；自定义版本可增删改，并可设为默认。提示词示例见 [中文预设](docs/PROMPT_PRESETS.md)，交互与兼容说明见 [划选助手](docs/ASSISTANT.md)。

纯翻译使用原有词库与缓存键；在翻译任务中添加问题会转为在线 AI 任务，不会把解释写入词库。自动模式下的纯翻译仍支持点击译文、复核目标并确认后替换选区或当前行。提问面板的结果使用复制与收藏。

## 适用范围与隐私

支持向 Windows UI Automation 提供选区的阅读区和输入框。图片、扫描件、部分自绘控件、未公开选区的应用暂不支持，不包含 OCR。百科依赖模型已有知识，**没有实时联网检索、来源核验或整篇文档读取**；AI 可能出错。

离线命中不发送网络请求。在线任务将选区、任务提示词与问题发送给你选择的服务商。内存缓存默认开启，磁盘缓存可选；个人词库与收藏使用当前 Windows 用户加密，清缓存不会删除它们。自定义提示词保存在本机配置文件中。导出的词库是明文。

密码框、常见敏感标签、排除项和软件自身跳过。托盘和 **Ctrl+Alt+Space** 可暂停全部读取；**Ctrl+Alt+T** 显示查询面板，**Ctrl+Alt+C** 复制当前结果，**Ctrl+Alt+Enter** 为即时翻译准备替换确认。不会自动发送消息或执行代码。

## 开发与验证

.NET 8 / C# / WPF，SDK 版本见 `global.json`。

```bash
source scripts/dotnet-env.sh # 本云环境
dotnet restore DotKnow.sln --locked-mode
dotnet build DotKnow.sln -c Release --no-restore
dotnet test tests/DotKnow.Tests -c Release --no-build
```

Windows 运行：`dotnet run --project src/DotKnow.App -c Release`。Linux 可交叉编译，实际 WPF 和 UIA 验收在 Windows CI 上运行。

```powershell
./scripts/publish-windows.ps1
python scripts/package-release.py
python scripts/package-promotion.py
```

生成 `artifacts/DotKnow-Windows-x64.zip`、`DotKnow-Source.zip` 和 `DotKnow-Promotion.zip`。解决方案、项目、命名空间、程序集与图标资源统一为 DotKnow；本地数据目录、加密上下文和单实例标识继续兼容旧版。自包含程序未签名，不含自动更新或 MSI。

官网源码在 [website](website/README.md)，支持 Vercel 部署，下载链接由版本清单统一生成，禁用 JavaScript 也可下载。当前环境尚未连接 Vercel 账户，部署状态见网站说明。品牌定位、推广文案和素材见 [宣传方案](docs/promotion/campaign.zh-CN.md)。
