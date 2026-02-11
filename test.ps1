# 功能说明：构建并运行 xOpenTerm 项目，默认 Debug，传参 --release 时构建 Release
# 运行时的工作目录为 .run

param(
    [switch]$Release
)

if ($args -contains "--release") { $Release = $true }

$ErrorActionPreference = "Stop"
trap {
    Write-Host "命令行被中止: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    break
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BuildScript = Join-Path $Root "script\build.ps1"
$RunDir = Join-Path $Root ".run"
$ExePath = if ($Release) {
    Join-Path $Root "bin\Release\net8.0-windows\xOpenTerm.exe"
} else {
    Join-Path $Root "bin\Debug\net8.0-windows\xOpenTerm.exe"
}

# 确保运行时目录存在
if (! (Test-Path -Path $RunDir -PathType Container)) {
    New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $RunDir "log") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $RunDir "config") -Force | Out-Null
}

# 构建项目
Write-Host "开始构建项目..." -ForegroundColor Cyan
if ($Release) {
    & $BuildScript --release
} else {
    & $BuildScript
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败，退出码: $LASTEXITCODE" -ForegroundColor Red
    Read-Host "按 Enter 键关闭窗口"
    exit $LASTEXITCODE
}

Write-Host "构建成功，开始运行应用程序..." -ForegroundColor Green

# 运行应用程序，捕捉错误输出
try {
    # 切换到运行时工作目录
    Push-Location $RunDir

    # 运行应用程序并捕捉输出
    $errorLogPath = Join-Path "log" "error.log"
    $process = Start-Process -FilePath $ExePath -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

    # 等待应用程序退出
    $process.WaitForExit()

    # 检查退出码
    if ($process.ExitCode -ne 0) {
        Write-Host "应用程序异常退出，退出码: $($process.ExitCode)" -ForegroundColor Red

        # 读取错误日志
        if (Test-Path -Path $errorLogPath -PathType Leaf) {
            Write-Host "错误日志内容:" -ForegroundColor Yellow
            Get-Content $errorLogPath | Write-Host
        }
    } else {
        Write-Host "应用程序正常退出" -ForegroundColor Green
    }
} finally {
    # 恢复原始目录
    Pop-Location
}

