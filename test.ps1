# Function: Build and run xOpenTerm project, default Debug, use --release for Release
# Runtime working directory is .run

param(
    [switch]$Release,
    [switch]$TestRdp
)

if ($args -contains "--release") { $Release = $true }
if ($args -contains "--test-rdp") { $TestRdp = $true }

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

# Clear runtime logs (skip when in test mode)
if (-not $TestRdp) {
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
}

# Run application and capture error output
# 工作路径为 .run，配置文件从 工作路径\config（即 .run\config）读取，日志在 .run\log
$errorLogPath = Join-Path $RunDir "log\error.log"
$appArgs = if ($TestRdp) { "--test-rdp" } else { "" }
try {
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config", "--", $appArgs.Trim() -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

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
    # Check for crash/error logs
    $logDir = Join-Path $RunDir "log"
    if (Test-Path -Path $logDir -PathType Container) {
        # Check for crash/error log files
        $crashErrorFiles = Get-ChildItem -Path $logDir -File | Where-Object { $_.Name -match "error|crash|exception" }
        foreach ($file in $crashErrorFiles) {
            Write-Host "Crash/error log file: $($file.Name)" -ForegroundColor Red
        }

        # Check for error keyword in all log files
        $allLogFiles = Get-ChildItem -Path $logDir -File
        foreach ($file in $allLogFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content -match "error|exception|failed|fatal") {
                Write-Host "Log file with error keyword: $($file.Name)" -ForegroundColor Red
            }
        }
    }
}