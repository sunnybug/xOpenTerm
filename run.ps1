# 功能说明：编译并运行 Debug 版本

param()

$ErrorActionPreference = "Stop"
trap {
    Write-Host "命令行被中止: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    break
}

$ScriptDir = Join-Path $PSScriptRoot "script"
& (Join-Path $ScriptDir "test.ps1")
