#!/usr/bin/env bash
# 从 C 方案透明单色主标生成 WPF、EXE 与适配任务栏的黑白托盘资源。
set -euo pipefail
cd "$(dirname "$0")/.."
export INKSCAPE_PROFILE_DIR="${TMPDIR:-/tmp}/dotknow-inkscape-profile"
export XDG_CACHE_HOME="${TMPDIR:-/tmp}/dotknow-brand-cache"
mkdir -p "$INKSCAPE_PROFILE_DIR" "$XDG_CACHE_HOME"
python3 - <<'PY'
from pathlib import Path
from copy import deepcopy
import xml.etree.ElementTree as E
E.register_namespace('', 'http://www.w3.org/2000/svg')
ns = '{http://www.w3.org/2000/svg}'
assets = Path('src/DotKnow.App/Assets')
master = E.parse(assets / 'dotknow-mark.svg').getroot()
for variant in ('app', 'black', 'white'):
    root = E.Element(ns + 'svg', {'viewBox': '0 0 1024 1024', 'width': '1024', 'height': '1024'})
    if variant == 'app':
        E.SubElement(root, ns + 'rect', {'x': '24', 'y': '24', 'width': '976', 'height': '976', 'rx': '208', 'fill': '#17191d'})
    group = E.SubElement(root, ns + 'g', {'transform': 'translate(100 100) scale(.8046875)' if variant == 'app' else ''})
    for child in master:
        if child.tag == ns + 'g':
            shape = deepcopy(child)
            shape.set('fill', '#ffffff' if variant in ('app', 'white') else '#17191d')
            group.append(shape)
    name = 'dotknow.svg' if variant == 'app' else f'dotknow-tray-{variant}.svg'
    E.ElementTree(root).write(assets / name, encoding='unicode')
PY
for name in dotknow dotknow-tray-black dotknow-tray-white; do
  inkscape "src/DotKnow.App/Assets/$name.svg" --export-filename="src/DotKnow.App/Assets/$name.png" --export-width=512 --export-height=512
  convert "src/DotKnow.App/Assets/$name.png" -define icon:auto-resize=256,128,64,48,32,24,20,16 "src/DotKnow.App/Assets/$name.ico"
done
inkscape src/DotKnow.App/Assets/dotknow-mark.svg --export-filename=docs/promotion/logo.png --export-width=1024 --export-height=1024
cp src/DotKnow.App/Assets/dotknow-mark.svg docs/promotion/logo.svg
cp src/DotKnow.App/Assets/dotknow.png docs/promotion/app-icon.png
cp src/DotKnow.App/Assets/dotknow.ico docs/promotion/app-icon.ico
cp src/DotKnow.App/Assets/dotknow-tray-black.ico docs/promotion/tray-black.ico
cp src/DotKnow.App/Assets/dotknow-tray-white.ico docs/promotion/tray-white.ico
