# 仅在真实 Windows 验收成功后，将测试截图发布到独立、按源码版本命名的预览分支。
# 使用 checkout 配置的 Git 身份；不读取、不输出、不提交任何凭据。
[CmdletBinding()]
param([string]$SourceDirectory = 'artifacts/ui-smoke')
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_SHA -notmatch '^[0-9a-f]{40}$' -or !$env:RUNNER_TEMP) { throw '仅供 GitHub Actions Windows 验收使用。' }
$source = (Resolve-Path $SourceDirectory).Path
if (!(Test-Path (Join-Path $source 'ui-validation.txt'))) { throw '缺少成功验收报告，拒绝发布预览。' }
$branch = "refs/heads/codex/ui-preview-$($env:GITHUB_SHA)"
$existing = git ls-remote --heads origin $branch
if ($LASTEXITCODE -ne 0) { throw '无法检查预览分支。' }
if ($existing) { Write-Host '此源码版本已有预览，不覆盖既有结果。'; return }
$preview = Join-Path $env:RUNNER_TEMP ('TypeLingo-preview-' + [guid]::NewGuid().ToString('N'))
git worktree add --detach $preview $env:GITHUB_SHA
if ($LASTEXITCODE -ne 0) { throw '无法创建独立预览工作区。' }
try {
    $destination = Join-Path $preview 'docs/ui-preview'
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem $source -Filter '*.png' | Copy-Item -Destination $destination
    Copy-Item (Join-Path $source 'ui-validation.txt') $destination
    @{ source_commit = $env:GITHUB_SHA; workflow_run = $env:GITHUB_RUN_ID; captured_on = 'Windows GitHub Actions'; data = 'isolated UI fixtures; no user data' } |
        ConvertTo-Json | Set-Content (Join-Path $destination 'source.json') -Encoding utf8
    git -C $preview add -- docs/ui-preview
    git -C $preview -c 'user.name=github-actions[bot]' -c 'user.email=41898282+github-actions[bot]@users.noreply.github.com' commit -m 'Publish verified TypeLingo Windows UI previews'
    if ($LASTEXITCODE -ne 0) { throw '无法保存界面预览。' }
    git -C $preview push origin "HEAD:$branch"
    if ($LASTEXITCODE -ne 0) { throw '无法上传界面预览。' }
} finally {
    git worktree remove --force $preview
}
