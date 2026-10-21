@echo off
chcp 65001 >nul
setlocal
title 点解 - 中文卸载向导
rem 只删除点解程序和本脚本创建的快捷方式，保留用户设置及加密密钥。
echo 请先从系统托盘退出点解。
echo 此操作保留个人设置和加密密钥，不删除其他用户文件。
pause
set "TYPELINGO_INSTALL_DIR=%LOCALAPPDATA%\Programs\TypeLingo"
powershell.exe -NoProfile -Command "$ErrorActionPreference='Stop'; try { $exe=Join-Path $env:TYPELINGO_INSTALL_DIR 'DotKnow.exe'; if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }; foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))) { foreach ($name in @('点解.lnk','点知.lnk')) { $link=Join-Path $folder $name; if (Test-Path -LiteralPath $link) { $shell=New-Object -ComObject WScript.Shell; if ($shell.CreateShortcut($link).TargetPath -eq $exe) { Remove-Item -LiteralPath $link -Force } } } }; Write-Host '程序和本软件快捷方式已删除。个人配置仍保留在本地应用数据目录的 TypeLingo 文件夹。' } catch { Write-Host ('卸载失败：'+$_.Exception.Message); exit 1 }"
if errorlevel 1 goto failed
pause
exit /b 0
:failed
echo 请先退出正在运行的点解，再重试卸载。
pause
exit /b 1
