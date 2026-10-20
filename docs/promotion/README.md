# TypeLingo 宣传素材

本目录包含可交付的中文宣传图与文案，基于本轮 v0.4.0-preview.1 开发功能整理。宣传图明确标注为产品交互示意；Windows 实机截图应在 Windows UiSmoke 通过后，从 Actions 附件获取。发布前请确认候选版本的 Windows 验收结果。

| 文件 | 用途 |
| --- | --- |
| hero.png | 横版主图，1536 × 1024，适合项目介绍与文章头图 |
| review-square.png | 方形复习主题图，适合社交平台 |
| logo.svg / logo.png | 仓库现有 Logo 的矢量与页面资源；新独立 Logo 尚未提供 |
| copy.zh-CN.md | 标题、短介绍、功能说明与发布文案 |
| generation-notes.md | 视觉来源与事实边界 |

下载打包：运行 `python3 scripts/package-promotion.py`，生成 `artifacts/TypeLingo-Promotion.zip` 与 SHA-256 校验文件。文件名不含版本号，文案中明确区分候选版与已发布稳定版。

宣传图、文案和功能代码上传同一开发分支，供审核。不会自动创建正式版本标签或替换已发布的 GitHub Release。
