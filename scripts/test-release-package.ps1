# Validate the renamed single-file app, Chinese installer syntax and the actual ZIP hash on Windows.
$ErrorActionPreference = 'Stop'
$package = Join-Path $PSScriptRoot '../artifacts/DotKnow-Windows-x64.zip'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('DotKnow-package-check-' + [guid]::NewGuid().ToString('N'))
$launched = $null
try {
    Expand-Archive -LiteralPath $package -DestinationPath $temporary
    $exe = Join-Path $temporary 'DotKnow.exe'
    $manifest = (Get-Content (Join-Path $temporary '文件校验.txt') -Encoding utf8).Trim()
    if ($manifest -notmatch '^([a-f0-9]{64})\s+DotKnow\.exe$') { throw '安装包缺少 DotKnow 校验信息。' }
    if ((Get-FileHash $exe -Algorithm SHA256).Hash -ne $Matches[1]) { throw 'DotKnow.exe 与安装包校验信息不一致。' }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductName -ne '点知 · DotKnow') { throw '程序产品名称未更新。' }
    foreach ($script in Get-ChildItem $temporary -Filter '*.cmd') {
        $content = Get-Content -LiteralPath $script.FullName -Raw -Encoding utf8
        $command = [regex]::Match($content, 'powershell\.exe -NoProfile -Command "([^\r\n]+)"')
        if (!$command.Success) { throw "缺少安装命令：$($script.Name)" }
        $errors = $null; $tokens = $null
        $null = [Management.Automation.Language.Parser]::ParseInput($command.Groups[1].Value, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw "安装脚本语法错误：$($errors -join ', ')" }
    }
    $launched = Start-Process -FilePath $exe -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200; $launched.Refresh()
        if ($launched.HasExited) { throw "重命名后的程序启动失败：$($launched.ExitCode)" }
    } while (!$launched.MainWindowHandle -and [DateTime]::UtcNow -lt $deadline)
    if (!$launched.MainWindowHandle) { throw 'DotKnow.exe 没有显示首次设置界面。' }
    Write-Host 'DotKnow 安装包校验、中文脚本解析、产品名称和重命名后的真实程序启动均通过。'
} finally {
    if ($launched -and !$launched.HasExited) { Stop-Process -Id $launched.Id -Force; $launched.WaitForExit() }
    if (Test-Path $temporary) { Remove-Item $temporary -Recurse -Force }
}
