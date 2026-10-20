#!/usr/bin/env python3
"""生成中文 Windows 安装包与源码包，不包含密钥、缓存或构建中间文件。"""
from pathlib import Path
import hashlib
import sys
import zipfile


def main():
    # Windows 重定向输出时可能采用本地代码页，统一为 UTF-8 以保留中文日志。
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    root = Path(__file__).resolve().parents[1]
    output = root / "artifacts"
    exe = output / "win-x64" / "TypeLingo.exe"
    if not exe.is_file() or exe.read_bytes()[:2] != b"MZ":
        raise RuntimeError("请先发布 Windows x64 程序，再生成中文安装包。")
    digest = hashlib.sha256(exe.read_bytes()).hexdigest()
    manifest = f"{digest}  TypeLingo.exe\r\n"
    (output / "win-x64" / "文件校验.txt").write_text(manifest, encoding="utf-8")
    windows_zip = output / "TypeLingo-Windows-x64.zip"
    with zipfile.ZipFile(windows_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        archive.write(exe, "TypeLingo.exe")
        archive.writestr("文件校验.txt", manifest.encode("utf-8"))
        for file in sorted((root / "packaging").iterdir()):
            if file.is_file():
                archive.write(file, file.name)
        archive.write(root / "README.md", "README.md")
        for file in sorted((root / "docs").iterdir()):
            if file.is_file():
                archive.write(file, file.relative_to(root).as_posix())
        for directory in ["vocabulary", "ui-preview"]:
            for file in sorted((root / "docs" / directory).rglob("*")):
                if file.is_file():
                    archive.write(file, file.relative_to(root).as_posix())
    source_zip = output / "TypeLingo-Source.zip"
    excluded = {"bin", "obj", ".git", "TestResults", "__pycache__", "node_modules", "dist", ".vercel", "test-results", "playwright-report"}
    with zipfile.ZipFile(source_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for directory in ["src", "tests", "tools", "docs", "data", "scripts", "packaging", ".github", "website"]:
            for file in sorted((root / directory).rglob("*")):
                if file.is_file() and not excluded.intersection(file.relative_to(root).parts) and file.name != ".env" and not file.name.startswith(".env."):
                    archive.write(file, "TypeLingo/" + file.relative_to(root).as_posix())
        for name in [".gitignore", "README.md", "TypeLingo.sln", "global.json", "Directory.Build.props"]:
            archive.write(root / name, "TypeLingo/" + name)
    for path in [windows_zip, source_zip]:
        with zipfile.ZipFile(path) as archive:
            if archive.testzip() is not None:
                raise RuntimeError(f"压缩包完整性检查失败：{path.name}")
        print(f"已生成并校验：{path.name}（{path.stat().st_size:,} 字节）")
    print(f"程序 SHA-256：{digest}")


if __name__ == "__main__":
    main()
