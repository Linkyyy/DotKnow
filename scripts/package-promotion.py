#!/usr/bin/env python3
"""将经过检查的宣传图和配套文案打包，不读取用户配置或密钥。"""
from pathlib import Path
import hashlib
import sys
import zipfile

sys.stdout.reconfigure(encoding="utf-8")
sys.stderr.reconfigure(encoding="utf-8")

root = Path(__file__).resolve().parents[1]
source = root / "docs" / "promotion"
required = ["hero.png", "assistant-square.png", "app-assistant.png", "app-prompts.png", "logo.svg", "logo.png", "app-icon.png", "app-icon.ico", "tray-black.ico", "tray-white.ico", "README.md", "copy.zh-CN.md", "campaign.zh-CN.md", "generation-notes.md"]
for name in required:
    if not (source / name).is_file():
        raise RuntimeError(f"缺少宣传素材：{name}")
output = root / "artifacts"
output.mkdir(exist_ok=True)
archive = output / "DotKnow-Promotion.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
    for name in sorted(p.name for p in source.iterdir() if p.is_file()):
        package.write(source / name, "DotKnow-Promotion/" + name)
with zipfile.ZipFile(archive) as package:
    if package.testzip() is not None:
        raise RuntimeError("宣传素材压缩包校验失败")
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(output / "DotKnow-Promotion.sha256").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
print(f"已打包并校验 {archive.name}（{archive.stat().st_size:,} 字节）")
