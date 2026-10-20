@echo off
chcp 65001 >nul
setlocal
title 点知 - 中文安装向导
rem 仅安装到当前用户目录，不申请管理员权限，不修改系统安全策略。
echo 欢迎安装 点知（DotKnow）。
echo 请先从系统托盘退出旧版点知或伴译，然后继续。
echo 安装位置：%LOCALAPPDATA%\Programs\TypeLingo
pause
set "TYPELINGO_PACKAGE_DIR=%~dp0"
set "TYPELINGO_INSTALL_DIR=%LOCALAPPDATA%\Programs\TypeLingo"
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; try { $source=Join-Path $env:TYPELINGO_PACKAGE_DIR 'DotKnow.exe'; $manifest=Join-Path $env:TYPELINGO_PACKAGE_DIR '文件校验.txt'; $line=Get-Content -LiteralPath $manifest -Encoding UTF8 | Where-Object { $_ -match '^([A-Fa-f0-9]{64})\s+DotKnow\.exe$' } | Select-Object -First 1; if (-not $line) { throw '缺少文件校验信息。' }; $expected=($line -split '\s+')[0]; if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) { throw '程序校验失败，请重新下载完整安装包。' }; New-Item -ItemType Directory -Path $env:TYPELINGO_INSTALL_DIR -Force | Out-Null; $target=Join-Path $env:TYPELINGO_INSTALL_DIR 'DotKnow.exe'; Copy-Item -LiteralPath $source -Destination $target -Force; Copy-Item -LiteralPath (Join-Path $env:TYPELINGO_PACKAGE_DIR '卸载点知.cmd') -Destination $env:TYPELINGO_INSTALL_DIR -Force; $legacy=Join-Path $env:TYPELINGO_INSTALL_DIR 'TypeLingo.exe'; if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Force }; $shell=New-Object -ComObject WScript.Shell; foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))) { $oldLink=Join-Path $folder '伴译.lnk'; if ((Test-Path -LiteralPath $oldLink) -and $shell.CreateShortcut($oldLink).TargetPath -eq $legacy) { Remove-Item -LiteralPath $oldLink -Force }; $shortcut=$shell.CreateShortcut((Join-Path $folder '点知.lnk')); $shortcut.TargetPath=$target; $shortcut.WorkingDirectory=$env:TYPELINGO_INSTALL_DIR; $shortcut.Description='点知：划选即问的桌面 AI 助手'; $shortcut.Save() }; Write-Host '安装成功。请从桌面或开始菜单打开点知。' } catch { Write-Host ('安装失败：'+$_.Exception.Message); exit 1 }"
if errorlevel 1 goto failed
echo 程序不会设置开机自启。首次启动后，请完成隐私授权，并在任务与提示词中选择模式。
pause
exit /b 0
:failed
echo 请检查安装包完整性、文件权限，并确认旧版点知或伴译已经退出。
pause
exit /b 1
