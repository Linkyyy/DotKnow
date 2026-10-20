# TypeLingo 官网

轻量静态官网，无后端、追踪脚本或访客 API Key 收集。沿用用户选择的 C 标志。阅读／写作／收藏演示是网页示意，不会连接翻译服务或保存个人词库。

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

浏览器测试优先使用 `/usr/bin/chromium`；其他电脑先运行 `npx playwright install chromium`，也可用 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 指定路径。构建产物在 `dist/`，无 JavaScript 时下载链接仍然可用。测试覆盖响应式宽度、场景标签键盘操作、收藏与复习示意、静态下载链接及 WCAG AA 自动检查。运行预览服务后可执行 `node scripts/capture.mjs` 重新生成官网实际渲染截图。

## Vercel 部署

[一键部署官网](https://vercel.com/new/clone?repository-url=https%3A%2F%2Fgithub.com%2FLinkyyy%2FTypeLingo%2Ftree%2Fa48091569a86f5259ddf306be08bfe8472612f66%2Fwebsite&project-name=typelingo&repository-name=typelingo-website)（在你的 Vercel 账号内导入；此链接不是已上线地址）。

导入 GitHub 仓库 `Linkyyy/TypeLingo`，选择含本目录的分支，Root Directory 设置为 **website**，Framework Preset 为 **Vite**。仓库内的 `vercel.json` 已包含构建与安全响应头设置。使用默认 `*.vercel.app` 域名即可上线，无需配置环境变量。

也可以在已经登录 Vercel CLI 的电脑中执行：

```bash
cd website
npx vercel@latest --prod
```

首次部署按提示选择自己的账号／团队并创建项目。部署授权属于项目所有者，仓库中不保存访问令牌。

## 更新下载版本

修改 `src/release.js` 中经过校验的版本、不可变 Git commit、ZIP 文件名、十进制 MB 大小与 **ZIP** SHA-256，然后构建、测试。Windows 按钮直连 GitHub 已发布二进制，避免每次官网部署复制 67 MB 安装包。`/download` 页面也提供跳转。若更换域名，可设置 `SITE_URL=https://你的域名 npm run build` 以输出绝对分享图片地址与 canonical URL。

内置词库宣传数量为 6,426 组／32,159 条查询记录，不能等同于 32,159 个独立单词；离线能力为精确查询，在线回退需要用户自己的服务和密钥。

字体：Space Grotesk（SIL Open Font License 1.1），授权见 `public/assets/space-grotesk-OFL.txt`。品牌素材沿用本项目已有资源。
