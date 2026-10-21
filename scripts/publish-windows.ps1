param([string]$Output = "artifacts/win-x64")
$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    # 把常规开发锁文件与自包含发布依赖分开，避免发布修改日常开发的锁定结果。
    dotnet publish src/DotKnow.App -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:NuGetLockFilePath=packages.win-x64.lock.json -p:RestoreLockedMode=true -o $Output
    if ($LASTEXITCODE -ne 0) { throw "dotnet 发布失败： $LASTEXITCODE" }
} finally { Pop-Location }
