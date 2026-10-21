# 点解 DotKnow · 宣传素材

**哪里不会，点哪里。** 像点读机一样，把屏幕上的疑问选出来。

| 文件 | 用途与来源 |
| --- | --- |
| hero.png | 1200×630 官网分享图，固定交互示意 |
| assistant-square.png | 640×640 百科交互示意，适合社交平台 |
| app-assistant.png | 真实 Windows WPF 回答面板，隔离验收固定数据 |
| app-prompts.png | 真实 Windows WPF 任务与提示词设置 |
| logo.svg / logo.png | 已选定的 C 双向对话主标 |
| app-icon.png / app-icon.ico | 深底白色 C 程序图标 |
| tray-black.ico / tray-white.ico | 浅色／深色任务栏使用的透明单色 C 图标 |
| copy.zh-CN.md | 主宣传语、短介绍、社交发布文案 |
| campaign.zh-CN.md | 品牌定位、推广重点、受众与发布方案 |
| generation-notes.md | 素材来源、演示与事实边界 |

运行 `python3 scripts/package-promotion.py` 生成 `artifacts/DotKnow-Promotion.zip` 和 SHA-256。网页示意和真实 WPF 截图均使用固定示例，不能用来宣传实时搜索或模型回答准确率。

翻译保留中英日离线词库；百科等任务需要用户选择的在线 AI 服务和 API Key。兼容与费用边界见 [宣传方案](campaign.zh-CN.md)。
