# TypeLingo（伴译）v0.2.0

## 本次更新

- 增加“监控所有应用”，授权后无需逐个添加进程；可随时切换回指定应用模式。密码框、敏感区域、排除的应用和伴译自身仍跳过。
- 全面重做为 Windows 11 设置风格：左侧导航与搜索、面包屑标题、分组卡片、蓝色选中标记、圆角输入与开关、固定保存区。
- 增加主页状态与请求统计，统一浅色与深色配色；模型选择、中英日界面、九种翻译方向与五家 API 接入继续保留。
- 增加 Windows 真实 WPF 窗口验证与设置页截图，检查导航搜索、监控范围切换、模型输入和设置持久化。

## 下载与安装

下载 TypeLingo-Windows-x64.zip 并完整解压，直接运行 TypeLingo.exe，或运行中文“安装到当前用户.cmd”。更新前先从系统托盘退出旧版。

TypeLingo-Source.zip 为完整源码；SHA256SUMS.txt 用于核对两个压缩包。settings-general.png、settings-ai.png、settings-privacy.png 为 Windows WPF 实际渲染的设置页截图，界面验证结果.txt 记录窗口检查结果。

## 验证范围

Linux 核心测试 106 项通过，Windows 交叉构建通过。发布流程还会运行真实 WPF 窗口验证，成功后才上传版本附件。UIA 第三方应用、真实 IME 及五家付费 API 账户调用仍需手动验收。
