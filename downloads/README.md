# v0.4.0-preview.5 候选交付包

默认鼠标划词、离线题库优先。内置用户提供的 32,159 条中英日查询记录，未命中再按设置在线翻译；可仅查离线、自动积累在线结果和未来命中的缓存，并搜索编辑、导入导出题库。个人增量独立加密保存，没有有效期；清缓存不删题库。使用见 [离线题库](../docs/OFFLINE_VOCABULARY.md)，186 项核心测试及 [本轮 Windows 界面验收](https://github.com/Linkyyy/TypeLingo/actions/runs/39393587812) 全部通过，见 [本轮记录](../docs/VALIDATION.md)。

- [Windows x64 中文候选安装包](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Windows-x64.zip)
- [完整源码 ZIP](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Source.zip)
- [宣传图与中文文案 ZIP](https://github.com/Linkyyy/TypeLingo/raw/refs/heads/codex/typelingo-favorites-and-replacement/downloads/TypeLingo-Promotion.zip)
- [SHA-256 校验值](SHA256SUMS.txt)
- [本轮验证记录](../docs/VALIDATION.md)

安装包完整解压后，可直接运行 TypeLingo.exe，或双击「安装到当前用户.cmd」。源码与宣传包不包含密钥、个人收藏、缓存或构建中间文件。正式发布前仍需检查目标第三方应用与真实中日 IME；当前仓库原有 v0.3.0 Release 保持原状。
