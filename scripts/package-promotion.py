#!/usr/bin/env python3
"""将经过检查的宣传图和配套文案打包，不读取用户配置或密钥。"""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
source = root / "docs" / "promotion"
required = ["hero.png", "review-square.png", "logo.svg", "logo.png", "app-icon.png", "app-icon.ico", "tray-black.ico", "tray-white.ico", "README.md", "copy.zh-CN.md", "generation-notes.md"]
for name in required:
    if not (source / name).is_file():
        raise RuntimeError(f"缺少宣传素材：{name}")
output = root / "artifacts"
output.mkdir(exist_ok=True)
archive = output / "TypeLingo-Promotion.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
    for name in required:
        package.write(source / name, "TypeLingo-Promotion/" + name)
with zipfile.ZipFile(archive) as package:
    if package.testzip() is not None:
        raise RuntimeError("宣传素材压缩包校验失败")
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(output / "TypeLingo-Promotion.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
print(f"已打包并校验 {archive.name}（{archive.stat().st_size:,} 字节）")
