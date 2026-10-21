# 素材来源与边界

图形沿用用户明确选定的 **C 双向对话**：两块相向的几何对话图形，中间折线留白。选稿见 docs/branding/logo-concepts.png。此前使用图像生成工具提取透明主标，再由 VTracer 转换与标准化为 SVG；唯一主标仍为 src/DotKnow.App/Assets/dotknow-mark.svg。

本次更新品牌名为「点解 · DotKnow」，主宣传语为「哪里不会，点哪里」。C 图形不重新绘制，程序及托盘图标继续由相同轮廓生成，托盘按系统任务栏明暗选择黑／白。

- hero.png：新版官网的实际浏览器分享截图，1200×630。图内产品面板是固定内容的网页交互示意，非 Windows 截图，也没有调用 AI。
- assistant-square.png：官网“百科”交互示意的 640×640 截图，固定示例，非 Windows 截图。
- app-assistant.png、app-prompts.png：真实 Windows WPF 验收界面，隔离测试数据；回答是验收固定示例，并非在线模型能力评测。来源记录见 docs/ui-preview/source.json。
- logo.svg / logo.png、app-icon.*、tray-*.ico：保留已选定 C 主标和图标资源，不包含旧名称文案。

新主图与方图来自本次网页渲染，没有在旧宣传图上覆盖文字或贴新面板。字体 Space Grotesk 使用 SIL OFL，中文采用系统字体。

宣传不得将百科描述为实时联网检索，不宣传已实现 OCR、离线大模型、权威事实核验或所有应用均兼容。离线词库仍只用于翻译，在线功能需要用户自己的 API，可能产生费用。

本次原生截图源码为 `284f072f1b86b90a1ed45e90a70e84281c541706`，Windows 验收运行 `39520278016`，181 项通过。新增纯白配色与简洁百科结果，使用固定隔离数据。
