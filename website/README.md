# 点解 DotKnow 官网

轻量静态官网，无后端、追踪脚本或访客 API Key 收集。沿用用户选择的 C 标志。百科／翻译／自由提问演示使用固定内容，不会连接 AI 或保存个人词库。

## 本地开发与验证

Node.js 22.12+（或 20.19+）：

```bash
cd website
npm ci
npm run dev
```

```bash
npm run build
npm test
```

浏览器测试优先使用 `/usr/bin/chromium`；其他电脑先运行 `npx playwright install chromium`，也可用 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 指定路径。构建产物在 `dist/`，无 JavaScript 时下载链接仍然可用。响应式检查同时验证较长问题与回答不遮住示意图底部操作。测试覆盖响应式宽度、场景标签键盘操作、收藏与复习示意、静态下载链接及 WCAG AA 自动检查。运行预览服务后可执行 `node scripts/capture.mjs` 重新生成官网实际渲染截图。

## Vercel 部署

程序与官网共用 [Linkyyy/DotKnow](https://github.com/Linkyyy/DotKnow) 仓库，本目录为官网源码。旧独立官网仓库作为迁移前的历史记录保留。

Vercel 项目为 `huangwenwang/dotknow`，已绑定 Linkyyy/DotKnow，Production Branch 为 main，Root Directory 为 `website`，Framework Preset 为 Vite。官网为 https://dotknow.vercel.app ，原 https://typelingo-website.vercel.app 地址继续保留。vercel.json 保留安全响应头。推送 main 后自动部署；实际线上结果以项目 Deployments 为准。

Vercel 提供生产域名用于分享图片、canonical URL 和 sitemap；也可以设置 SITE_URL 为自定义 HTTPS 域名。官网无后端，无需访客 API Key。

## 更新下载版本

修改 `src/release.js` 中经过校验的版本、不可变 Git commit、ZIP 文件名、十进制 MB 大小与 **ZIP** SHA-256，然后构建、测试。Windows 按钮直连 GitHub 已发布二进制，避免每次官网部署复制完整安装包。`/download` 页面也提供跳转。若更换域名，可设置 `SITE_URL=https://你的域名 npm run build` 以输出绝对分享图片地址与 canonical URL。

内置词库宣传数量为 6,426 组／32,159 条查询记录，不能等同于 32,159 个独立单词；离线能力为精确查询，在线回退需要用户自己的服务和密钥。

字体：Space Grotesk（SIL Open Font License 1.1），授权见 `public/assets/space-grotesk-OFL.txt`。品牌素材沿用本项目已有资源。

## 最新版本

v0.5.0-preview.2：简洁百科结果、纯白主题和微信兼容说明。真实截图来自 Windows 验收运行 39520278016，来源记录位于 public/assets/screens/source.json。

运行本目录下的构建命令后，可从程序仓库根目录运行 `python3 scripts/package-website.py` 生成官网部署包。
