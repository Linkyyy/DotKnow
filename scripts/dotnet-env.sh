#!/usr/bin/env bash
# 供托管 Linux 任务载入，不修改 HOME，也不绕过代理或证书信任配置。
export DOTNET_ROOT=/workspace/.dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.dotnet-home
export NUGET_PACKAGES=/workspace/.nuget/packages
export XDG_DATA_HOME=/workspace/.local/share
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
