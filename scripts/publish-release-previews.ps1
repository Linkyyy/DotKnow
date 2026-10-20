# Publish the exact Windows-tested ZIPs through normal checkout Git authentication.
# A source-keyed branch is created once; existing results are never overwritten.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_SHA -notmatch '^[0-9a-f]{40}$' -or !$env:RUNNER_TEMP) { throw '仅供 GitHub Actions 发布验收包。' }
$branch = "refs/heads/codex/release-preview-$($env:GITHUB_SHA)"
$existing = git ls-remote --heads origin $branch
if ($LASTEXITCODE -ne 0) { throw '无法检查验收包分支。' }
if ($existing) { Write-Host '此源码版本已有验收包，不覆盖既有结果。'; return }
$worktree = Join-Path $env:RUNNER_TEMP ('DotKnow-release-' + [guid]::NewGuid().ToString('N'))
git worktree add --detach $worktree $env:GITHUB_SHA
if ($LASTEXITCODE -ne 0) { throw '无法创建发布工作区。' }
try {
    $destination = Join-Path $worktree 'downloads'
    New-Item -ItemType Directory -Force $destination | Out-Null
    $sums = @()
    foreach ($name in @('DotKnow-Windows-x64.zip', 'DotKnow-Source.zip', 'DotKnow-Promotion.zip')) {
        $file = Join-Path 'artifacts' $name
        if (!(Test-Path $file)) { throw "缺少已验证的包：$name" }
        Copy-Item -LiteralPath $file -Destination $destination
        $sums += (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
    }
    $sums | Set-Content (Join-Path $destination 'SHA256SUMS.txt') -Encoding utf8
    @{ source_commit = $env:GITHUB_SHA; workflow_run = $env:GITHUB_RUN_ID; windows_package_validated = $true } |
        ConvertTo-Json | Set-Content (Join-Path $destination 'BUILD.json') -Encoding utf8
    git -C $worktree add -- downloads/DotKnow-Windows-x64.zip downloads/DotKnow-Source.zip downloads/DotKnow-Promotion.zip downloads/SHA256SUMS.txt downloads/BUILD.json
    git -C $worktree -c 'user.name=github-actions[bot]' -c 'user.email=41898282+github-actions[bot]@users.noreply.github.com' commit -m 'Publish Windows-validated DotKnow preview packages'
    if ($LASTEXITCODE -ne 0) { throw '无法保存验收包。' }
    git -C $worktree push origin "HEAD:$branch"
    if ($LASTEXITCODE -ne 0) { throw '无法上传验收包。' }
} finally { git worktree remove --force $worktree }
