#!/usr/bin/env python3
"""Package the built static website and its source, excluding local credentials and caches."""
from pathlib import Path
import hashlib
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parents[1]
site = root / "website"
if not (site / "dist/index.html").is_file():
    raise RuntimeError("请先在 website 目录运行 npm run build。")
output = root / "downloads/DotKnow-Website.zip"
excluded = {"node_modules", "dist", ".vercel", "test-results", "playwright-report", ".git"}
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for file in sorted((site / "dist").rglob("*")):
        if file.is_file():
            archive.write(file, "static-site/" + file.relative_to(site / "dist").as_posix())
    for file in sorted(site.rglob("*")):
        parts = file.relative_to(site).parts
        if file.is_file() and not excluded.intersection(parts) and not file.name.startswith(".env"):
            archive.write(file, "website/" + file.relative_to(site).as_posix())
    archive.writestr("使用说明.txt", "点解官网部署包\n\nstatic-site：已构建的静态网站，可用于静态托管。\nwebsite：完整官网源码，包含 Vercel 配置。\n\nVercel 部署：在已登录 Vercel 的电脑进入 website，运行 npm ci，再运行 npx vercel --prod。\n也可从 GitHub 导入，Root Directory 选择 website，框架选择 Vite。\n部署需要项目所有者的 Vercel 账户授权，此包不含令牌、个人配置或 API 密钥。\n".encode("utf-8"))
with zipfile.ZipFile(output) as archive:
    if archive.testzip() is not None:
        raise RuntimeError("官网部署包完整性检查失败。")
digest = hashlib.sha256(output.read_bytes()).hexdigest()
output.with_suffix(".sha256").write_text(f"{digest}  {output.name}\n", encoding="utf-8")
print(f"已校验官网源码与静态部署包：{output.name}（{output.stat().st_size:,} 字节）")
