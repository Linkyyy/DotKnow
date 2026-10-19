# API 开通与模型选择

伴译支持五种服务商。设置 → AI → 选择服务商 →「打开官方 API 开通 / 购买平台」会直接打开下面的官方地址；不会使用自定义 API 地址拼接购买链接。

| 服务 | 官方平台 | 默认 API 地址 | 请求方式 |
| --- | --- | --- | --- |
| DeepSeek | https://platform.deepseek.com/ | `https://api.deepseek.com` | Chat Completions |
| OpenAI | https://platform.openai.com/ | `https://api.openai.com/v1` | Chat Completions |
| Claude / Anthropic | https://console.anthropic.com/ | `https://api.anthropic.com/v1` | 原生 Messages API |
| Gemini / Google | https://aistudio.google.com/apikey | `https://generativelanguage.googleapis.com/v1beta` | 原生 generateContent API |
| 通义千问 / 阿里云百炼 | https://bailian.console.aliyun.com/ | `https://dashscope.aliyuncs.com/compatible-mode/v1` | 百炼兼容接口 |

在官方平台开通 API、充值或获取可用额度后，创建自己的密钥。网页聊天产品会员和开发者 API 额度可能分开计费，以各平台账户说明为准。不要把密钥发给他人或填写到聊天中。

## 用户自己选择模型

1. 选择服务商，检查 API 地址，并输入密钥。
2. 点击「查询可用模型」，从下拉列表选择。列表直接来自该服务，不会用硬编码示例假装查询结果。
3. 模型框也允许直接输入 ID，适用于平台不支持列表接口、列表权限不足或自定义网关。
4. 优先选择支持文本聊天的低延迟模型。平台返回的列表可能包含图像、音频或嵌入模型，它们不适合此翻译接口。
5. 点击「测试翻译（固定短句）」，只发送 `Hello` 到所选模型，再保存。

默认模型名称仅作为可编辑起点，本次未使用真实账户验证其当前可用性。模型下架、权限不足、地区限制或余额不足时，会显示错误，不会静默更换模型或返回模拟译文。原生 OpenAI、Anthropic、Gemini 与阿里云平台的所有模型类型并非都能用于本文本翻译流程。

Claude 使用 `x-api-key` 和版本请求头；Gemini 使用 `x-goog-api-key`，不把密钥放进 URL。其他三个服务使用 Bearer 认证。所有服务都保留 HTTPS 校验，并处理取消、超时、认证错误和 429 / 5xx 有限重试。

## 密钥隔离与迁移

密钥按服务商和完整 API 基址分别保存为 DPAPI 密文。切换服务商或 API 地址后，需要填写对应密钥；不能误用上一家的密钥。切回完全相同的配置可继续使用已保存的密钥。旧版 `api-key.dpapi` 只兼容原来的 DeepSeek 官方基址，不会发送给其他平台。

自定义 API 地址必须是 HTTPS。先填写服务商与地址，再输入新密钥；更换地址会清空尚未保存的密钥输入。密钥文件不能跨 Windows 用户或电脑直接移植，应重新填写。
