# v0.4.0-preview.3 候选交付包

本包修正设置 Tab 抖动和搜索光标，新增明确的翻译窗口显示时机选择，并修正新版 Teams／WebView2 的嵌入进程与富文本读取路径。C 图标与收藏复习保持可用。151 项核心测试与 [Windows WPF／WebView2／UIA 检查](https://github.com/Linkyyy/TypeLingo/actions/runs/39359244576) 已通过；实际 Teams 账户与企业版本仍需在用户机器确认。

- [Windows x64 中文候选安装包](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Windows-x64.zip)
- [完整源码 ZIP](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Source.zip)
- [宣传图与中文文案 ZIP](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Promotion.zip)
- [SHA-256 校验值](SHA256SUMS.txt)
- [本轮验证记录](../docs/VALIDATION.md)

安装包完整解压后，可直接运行 TypeLingo.exe，或双击「安装到当前用户.cmd」。源码与宣传包不包含密钥、个人收藏、缓存或构建中间文件。正式发布前仍需检查目标第三方应用与真实中日 IME；当前仓库原有 v0.3.0 Release 保持原状。
