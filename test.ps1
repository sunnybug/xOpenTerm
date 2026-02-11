# Function: Build and run xOpenTerm project, default Debug, use --release for Release
# Runtime working directory is .run

param(
    [switch]$Release
)

if ($args -contains "--release") { $Release = $true }

$ErrorActionPreference = "Stop"
trap {
    Write-Host "Command aborted: $_" -ForegroundColor Red
    Write-Host "$($_.InvocationInfo.ScriptName):$($_.InvocationInfo.ScriptLineNumber)" -ForegroundColor Red
    Read-Host "Press Enter to close window"
    break
}

$Root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$BuildScript = Join-Path $Root "script\build.ps1"
$RunDir = Join-Path $Root ".run"

# Ensure runtime directory exists
if (! (Test-Path -Path $RunDir -PathType Container)) {
    New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $RunDir "log") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $RunDir "config") -Force | Out-Null
}

# Build project
Write-Host "Starting build..." -ForegroundColor Cyan
if ($Release) {
    & $BuildScript --release
} else {
    & $BuildScript
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed, exit code: $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Build successful, starting application..." -ForegroundColor Green

# Kill existing xOpenTerm process
Write-Host "Killing existing xOpenTerm process..." -ForegroundColor Yellow
try {
    $processes = Get-Process -Name "xOpenTerm" -ErrorAction SilentlyContinue
    foreach ($process in $processes) {
        Write-Host "Killing process: $($process.Id)" -ForegroundColor Yellow
        $process.Kill()
        $process.WaitForExit(2000)
    }
} catch {
    Write-Host "Error killing process: $_" -ForegroundColor Yellow
}

# Clear runtime logs
Write-Host "Clearing runtime logs..." -ForegroundColor Yellow
try {
    $logDir = Join-Path $RunDir "log"
    if (Test-Path -Path $logDir -PathType Container) {
        Get-ChildItem -Path $logDir -File | Remove-Item -Force
        Write-Host "Logs cleared successfully" -ForegroundColor Green
    }
} catch {
    Write-Host "Error clearing logs: $_" -ForegroundColor Yellow
}

# Run application and capture error output
# 使用项目根目录作为工作目录，这样应用内 .run\log 会正确解析为 <根>\.run\log（与脚本清空/查看的目录一致）
$errorLogPath = Join-Path $RunDir "log\error.log"
try {
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config" -WorkingDirectory $Root -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

    # Wait for application to exit
    $process.WaitForExit()

    # Check exit code ($null = 进程被终止或无法获取，例如无界面环境)
    if ($null -eq $process.ExitCode) {
        Write-Host "Application process ended without exit code (e.g. terminated or no GUI). Build succeeded." -ForegroundColor Yellow
    } elseif ($process.ExitCode -ne 0) {
        Write-Host "Application exited abnormally, exit code: $($process.ExitCode)" -ForegroundColor Red

        # Read error log
        $logDir = Join-Path $RunDir "log"
        if (!(Test-Path $logDir -PathType Container)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
        if (Test-Path -Path $errorLogPath -PathType Leaf) {
            Write-Host "Error log content:" -ForegroundColor Yellow
            Get-Content $errorLogPath | Write-Host
        }
    } else {
        Write-Host "Application exited normally" -ForegroundColor Green
    }
} finally {
    # nothing to pop
}