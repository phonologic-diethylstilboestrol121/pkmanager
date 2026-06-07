# pkmanager 协议处理器注册脚本
# 运行一次即可: 右键 → 使用 PowerShell 运行
# 或者: powershell -ExecutionPolicy Bypass -File register-pkmanager-protocol.ps1
# 需要管理员权限 (写入 HKCR)

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$launcherPath = Join-Path $PSScriptRoot "pkmanager-launcher.ps1"
if (-not (Test-Path $launcherPath)) {
    Write-Host "[ERROR] 找不到启动器脚本: $launcherPath" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

$command = "powershell -ExecutionPolicy Bypass -WindowStyle Minimized -File `"$launcherPath`" `"%1`""

Write-Host "[pkmanager] 注册协议处理器..." -ForegroundColor Green
Write-Host "  启动器路径: $launcherPath"
Write-Host "  命令: $command"

try {
    # 注册 pkmanager:// 协议
    New-Item -Path "HKCR:\pkmanager" -Force -ErrorAction Stop | Out-Null
    Set-ItemProperty -Path "HKCR:\pkmanager" -Name "(Default)" -Value "URL:pkmanager Protocol" -Force
    Set-ItemProperty -Path "HKCR:\pkmanager" -Name "URL Protocol" -Value "" -Force

    New-Item -Path "HKCR:\pkmanager\shell\open\command" -Force -ErrorAction Stop | Out-Null
    Set-ItemProperty -Path "HKCR:\pkmanager\shell\open\command" -Name "(Default)" -Value $command -Force

    Write-Host "[pkmanager] 协议处理器注册成功！" -ForegroundColor Green
    Write-Host "  现在点击 pkmanager 中的「本机」按钮即可直接启动模拟器。"
} catch {
    Write-Host "[ERROR] 注册失败: $_" -ForegroundColor Red
    Write-Host "  请尝试: 右键此脚本 → 以管理员身份运行" -ForegroundColor Yellow
}

Read-Host "按 Enter 退出"
