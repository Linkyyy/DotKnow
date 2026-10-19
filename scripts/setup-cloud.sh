#!/usr/bin/env bash
set -euo pipefail
cd /workspace/TypeLingo
source scripts/dotnet-env.sh
mkdir -p /workspace/shared/downloads
if [[ ! -x "$DOTNET_ROOT/dotnet" ]] || [[ "$(dotnet --version 2>/dev/null || true)" != "8.0.426" ]]; then
  python3 - <<'PY'
import hashlib,tarfile,urllib.request
from pathlib import Path
url='https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.426/dotnet-sdk-8.0.426-linux-x64.tar.gz'
expected='8141d01bdc53f604aed3bb661b604707a64209f8a947cd81bf95b4f518951593d77864069191f54ea5e84362a409da6b0f69074bf893b283d7e0cf93e2bf20a8'
archive=Path('/workspace/shared/downloads/dotnet-sdk.tar.gz')
if not archive.exists() or hashlib.sha512(archive.read_bytes()).hexdigest()!=expected:
    urllib.request.urlretrieve(url,archive)
if hashlib.sha512(archive.read_bytes()).hexdigest()!=expected:
    raise RuntimeError('SDK 校验不匹配，安装已停止。')
Path('/workspace/.dotnet').mkdir(exist_ok=True)
with tarfile.open(archive) as package:
    package.extractall('/workspace/.dotnet',filter='data')
PY
fi
# .NET 8 命名互斥锁使用操作系统用户目录，不受 DOTNET_CLI_HOME 配置影响。
# 托管环境必须允许访问这个标准的用户 SDK 目录。
mkdir -p /home/agent/.dotnet
dotnet restore TypeLingo.sln --locked-mode
dotnet build TypeLingo.sln -c Release --no-restore -m:2
dotnet test tests/TypeLingo.Tests -c Release --no-build --logger 'trx;LogFileName=core.trx' --results-directory TestResults
