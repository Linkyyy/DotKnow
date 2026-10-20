# TypeLingo 宣传素材

本目录包含可交付的中文宣传图与文案，基于本轮 v0.4.0-preview.3 开发功能整理。宣传图明确标注为产品交互示意；本轮 C 图标检查进度与 Windows 运行链接见 [验证记录](../VALIDATION.md)，真实截图从相应 Actions 附件获取。正式发布前请继续检查目标第三方应用与真实中日 IME。

| 文件 | 用途 |
| --- | --- |
| hero.png | 横版主图，1536 × 1024，适合项目介绍与文章头图 |
| review-square.png | 方形复习主题图，适合社交平台 |
| logo.svg / logo.png | 选定的 C 双向对话主标，透明 SVG 与 PNG |
| app-icon.png / app-icon.ico | 深底白色 C 程序图标，多尺寸 ICO 包含 16–256 px |
| tray-black.ico / tray-white.ico | 浅色／深色任务栏使用的透明单色 C 图标 |
| copy.zh-CN.md | 标题、短介绍、功能说明与发布文案 |
| generation-notes.md | 视觉来源与事实边界 |

下载打包：运行 `python3 scripts/package-promotion.py`，生成 `artifacts/TypeLingo-Promotion.zip` 与 SHA-256 校验文件。文件名不含版本号，文案中明确区分候选版与已发布稳定版。

宣传图、文案和功能代码上传同一开发分支，供审核。不会自动创建正式版本标签或替换已发布的 GitHub Release。
