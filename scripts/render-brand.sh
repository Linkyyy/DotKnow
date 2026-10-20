#!/usr/bin/env bash
# 从同一份 SVG 生成 WPF 页面与 Windows EXE / 托盘使用的资源。
set -euo pipefail
cd "$(dirname "$0")/.."
export INKSCAPE_PROFILE_DIR="${TMPDIR:-/tmp}/typelingo-inkscape-profile"
export XDG_CACHE_HOME="${TMPDIR:-/tmp}/typelingo-brand-cache"
mkdir -p "$INKSCAPE_PROFILE_DIR" "$XDG_CACHE_HOME"
inkscape src/TypeLingo.App/Assets/typelingo.svg --export-filename=src/TypeLingo.App/Assets/typelingo.png --export-width=256 --export-height=256
convert src/TypeLingo.App/Assets/typelingo.png -define icon:auto-resize=256,128,64,48,32,24,16 src/TypeLingo.App/Assets/typelingo.ico
