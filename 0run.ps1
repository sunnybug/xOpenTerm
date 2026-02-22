# Function: Build and run xOpenTerm project, default Debug, use --release for Release
# Runtime working directory is .run（config 与 log 在 .run 下）
# 以下测试均针对配置中名为 test 的节点（或其下子节点）：
# --test-ssh-status：仅构建并运行 SSH 状态获取单元测试（agent 连接，连接超时 3s），无 UI，测试结束自动退出。
# --test-scan-port：打开 UI，仅对 test 节点下 SSH/RDP 主机执行端口扫描，扫描完成后延迟 3 秒自动退出。
# --test-connect：遍历 test 节点下所有子节点进行连接测试，结果输出到命令行，无论成功失败均自动退出。
# --test：运行所有单元测试（dotnet test）以及上述 test-ssh-status、test-scan-port、test-connect，任一失败则退出。

param(
    [switch]$Release,
    [switch]$Test,
    [switch]$TestRdp,
    [switch]$TestSshStatus,
    [switch]$TestScanPort,
    [switch]$TestConnect
)

if ($args -contains "--release") { $Release = $true }
if ($args -contains "--test") { $Test = $true }
if ($args -contains "--test-rdp") { $TestRdp = $true }
if ($args -contains "--test-ssh-status") { $TestSshStatus = $true }
if ($args -contains "--test-scan-port") { $TestScanPort = $true }
if ($args -contains "--test-connect") { $TestConnect = $true }

$AllowedArgs = @("--release", "--test", "--test-rdp", "--test-ssh-status", "--test-scan-port", "--test-connect")
$InvalidArgs = @()
foreach ($a in $args) {
    if ($a -notin $AllowedArgs) {
        $InvalidArgs += $a
    }
}
if ($InvalidArgs.Count -gt 0) {
    Write-Host "错误：不支持的参数: $($InvalidArgs -join ', ')" -ForegroundColor Red
    Write-Host ""
    Write-Host "支持的参数：" -ForegroundColor Yellow
    Write-Host "  --release           使用 Release 配置构建并运行"
    Write-Host "  --test              运行所有单元测试及 0run 的 test-ssh-status、test-scan-port、test-connect（均针对 test 节点）"
    Write-Host "  --test-rdp          测试 RDP 模式"
    Write-Host "  --test-ssh-status   仅运行 SSH 状态获取单元测试，无 UI，测试结束自动退出"
    Write-Host "  --test-scan-port    打开 UI，仅对 test 节点下主机执行端口扫描，完成后延迟 3 秒自动退出"
    Write-Host "  --test-connect      遍历名为 test 的节点下所有子节点进行连接测试，结果输出到命令行并自动退出"
    Write-Host ""
    exit 1
}

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

# 工作目录为 .run，确保 .run/log 与 .run/config 存在
$LogDir = Join-Path $RunDir "log"
if (! (Test-Path -Path $RunDir -PathType Container)) { New-Item -ItemType Directory -Path $RunDir -Force | Out-Null }
if (! (Test-Path -Path $LogDir -PathType Container)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
if (! (Test-Path -Path (Join-Path $RunDir "config") -PathType Container)) { New-Item -ItemType Directory -Path (Join-Path $RunDir "config") -Force | Out-Null }

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

# --test：运行所有单元测试及 0run.ps1 支持的所有测试，任一失败则退出
if ($Test) {
    # 1. 所有单元测试
    Write-Host "========== 1/4 运行所有单元测试 (dotnet test) ==========" -ForegroundColor Cyan
    & dotnet test
    if ($LASTEXITCODE -ne 0) {
        Write-Host "单元测试失败, exit code: $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "单元测试通过." -ForegroundColor Green

    # 2. test-ssh-status
    Write-Host "========== 2/4 SSH 状态获取单元测试 ==========" -ForegroundColor Cyan
    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | Remove-Item -Force
        }
    } catch { Write-Host "Error clearing logs: $_" -ForegroundColor Yellow }
    $testsPath = Join-Path $Root "tests\xOpenTerm.Tests.csproj"
    & dotnet test $testsPath --filter "FullyQualifiedName~SshStatusFetch"
    if ($LASTEXITCODE -ne 0) { Write-Host "SSH 状态测试失败." -ForegroundColor Red; exit $LASTEXITCODE }
    Write-Host "SSH 状态测试通过." -ForegroundColor Green

    # 3. test-scan-port
    Write-Host "========== 3/4 端口扫描测试 ==========" -ForegroundColor Cyan
    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | ForEach-Object {
                try { Remove-Item -Path $_.FullName -Force -ErrorAction Stop }
                catch { Write-Host "  Skipping locked file: $($_.Name)" -ForegroundColor DarkYellow }
            }
        }
    } catch { Write-Host "Error clearing logs: $_" -ForegroundColor Yellow }
    try {
        $processes = Get-Process -Name "xOpenTerm" -ErrorAction SilentlyContinue
        foreach ($process in $processes) { $process.Kill(); $process.WaitForExit(2000) }
    } catch { Write-Host "Error killing process: $_" -ForegroundColor Yellow }
    $errorLogPath = Join-Path $LogDir "error.log"
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config", "--", "--test-scan-port" -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath
    $process.WaitForExit()
    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0) {
        Write-Host "端口扫描测试失败, exit code: $($process.ExitCode)" -ForegroundColor Red
        exit $process.ExitCode
    }
    Write-Host "端口扫描测试通过." -ForegroundColor Green

    # 4. test-connect
    Write-Host "========== 4/4 连接测试 (test 节点) ==========" -ForegroundColor Cyan
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | ForEach-Object {
                try { Remove-Item -Path $_.FullName -Force -ErrorAction Stop }
                catch { Write-Host "  Skipping locked file: $($_.Name)" -ForegroundColor DarkYellow }
            }
        }
    } catch { Write-Host "Error clearing logs: $_" -ForegroundColor Yellow }
    try {
        $processes = Get-Process -Name "xOpenTerm" -ErrorAction SilentlyContinue
        foreach ($process in $processes) { $process.Kill(); $process.WaitForExit(2000) }
    } catch { Write-Host "Error killing process: $_" -ForegroundColor Yellow }
    $errorLogPath = Join-Path $LogDir "error.log"
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config", "--", "--test-connect" -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath
    $process.WaitForExit()
    $testConnectLogPath = Join-Path $LogDir "test-connect.log"
    if (Test-Path -Path $testConnectLogPath -PathType Leaf) {
        Write-Host "Test-connect output:" -ForegroundColor Cyan
        Get-Content $testConnectLogPath -Encoding UTF8 | Write-Host
    }
    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0) {
        Write-Host "连接测试失败, exit code: $($process.ExitCode)" -ForegroundColor Red
        exit $process.ExitCode
    }
    Write-Host "连接测试通过." -ForegroundColor Green

    Write-Host "========== 全部测试通过 ==========" -ForegroundColor Green
    exit 0
}

# --test-ssh-status：仅运行 SSH 状态获取单元测试，无交互，测试结束自动退出。日志在 .run\log
if ($TestSshStatus) {
    # 与主流程一致：运行前先清理 .run/log，便于从干净状态检查结果
    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | Remove-Item -Force
        }
    } catch {
        Write-Host "Error clearing logs: $_" -ForegroundColor Yellow
    }
    Write-Host "Running SSH status fetch unit tests..." -ForegroundColor Cyan
    $testsPath = Join-Path $Root "tests\xOpenTerm.Tests.csproj"
    & dotnet test $testsPath --filter "FullyQualifiedName~SshStatusFetch"
    # 检查结果：若有内容的 crash/error 日志或 log 中含 error 等，则输出 log 完整路径
    if (Test-Path -Path $LogDir -PathType Container) {
        $pathsToShow = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $crashErrorFiles = Get-ChildItem -Path $LogDir -File | Where-Object { $_.Name -match "error|crash|exception" }
        foreach ($file in $crashErrorFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content.Trim().Length -gt 0) {
                [void]$pathsToShow.Add($file.FullName)
            }
        }
        $allLogFiles = Get-ChildItem -Path $LogDir -File
        foreach ($file in $allLogFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content -match '\b(error|err|crash|fatal)\b') {
                [void]$pathsToShow.Add($file.FullName)
            }
        }
        foreach ($path in $pathsToShow) {
            Write-Host "Log: $path" -ForegroundColor Red
        }
    }
    exit $LASTEXITCODE
}

# --test-scan-port：打开 UI，仅对 test 节点下 SSH/RDP 主机执行端口扫描，完成后延迟 3 秒自动退出。日志在 .run\log
if ($TestScanPort) {
    # 与主流程一致：运行前先清理 .run/log，便于从干净状态检查结果
    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | ForEach-Object {
                try { Remove-Item -Path $_.FullName -Force -ErrorAction Stop }
                catch { Write-Host "  Skipping locked file: $($_.Name)" -ForegroundColor DarkYellow }
            }
        }
    } catch {
        Write-Host "Error clearing logs: $_" -ForegroundColor Yellow
    }

    Write-Host "Starting application with port scan test mode..." -ForegroundColor Cyan
    Write-Host "The UI will open and scan only hosts under the 'test' node." -ForegroundColor Cyan
    Write-Host "After scanning completes, the application will wait 3 seconds then exit automatically." -ForegroundColor Cyan

    # 杀死现有进程（与主流程一致）
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

    # 运行应用（传递 --test-scan-port 参数）
    $errorLogPath = Join-Path $LogDir "error.log"
    try {
        $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
        $config = if ($Release) { "Release" } else { "Debug" }
        $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config", "--", "--test-scan-port" -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

        # 等待应用退出
        $process.WaitForExit()

        # 检查退出码
        if ($null -eq $process.ExitCode) {
            Write-Host "Application process ended without exit code." -ForegroundColor Yellow
        } elseif ($process.ExitCode -ne 0) {
            Write-Host "Port scan test failed, exit code: $($process.ExitCode)" -ForegroundColor Red
        } else {
            Write-Host "Port scan test completed successfully." -ForegroundColor Green
        }

        # 检查日志中的错误
        if (Test-Path -Path $LogDir -PathType Container) {
            $pathsToShow = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            $crashErrorFiles = Get-ChildItem -Path $LogDir -File | Where-Object { $_.Name -match "error|crash|exception" }
            foreach ($file in $crashErrorFiles) {
                $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
                if ($content -and $content.Trim().Length -gt 0) {
                    [void]$pathsToShow.Add($file.FullName)
                }
            }
            $allLogFiles = Get-ChildItem -Path $LogDir -File
            foreach ($file in $allLogFiles) {
                $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
                if ($content -and $content -match '\b(error|err|crash|fatal)\b') {
                    [void]$pathsToShow.Add($file.FullName)
                }
            }
            foreach ($path in $pathsToShow) {
                Write-Host "Log: $path" -ForegroundColor Red
            }
        }

        exit $process.ExitCode
    } finally {
        # 清理
    }
}

# --test-connect：运行程序并遍历 test 节点下所有子节点进行连接测试，结果输出到命令行，无论成功失败均自动退出
if ($TestConnect) {
    # 控制台使用 UTF-8，以便正确显示中文结果
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

    try {
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | ForEach-Object {
                try { Remove-Item -Path $_.FullName -Force -ErrorAction Stop }
                catch { Write-Host "  Skipping locked file: $($_.Name)" -ForegroundColor DarkYellow }
            }
        }
    } catch {
        Write-Host "Error clearing logs: $_" -ForegroundColor Yellow
    }

    Write-Host "Starting application with test-connect mode..." -ForegroundColor Cyan
    Write-Host "Traversing all child nodes under 'test' and testing connections. Results will be printed below." -ForegroundColor Cyan

    try {
        $processes = Get-Process -Name "xOpenTerm" -ErrorAction SilentlyContinue
        foreach ($process in $processes) {
            Write-Host "Killing existing process: $($process.Id)" -ForegroundColor Yellow
            $process.Kill()
            $process.WaitForExit(2000)
        }
    } catch {
        Write-Host "Error killing process: $_" -ForegroundColor Yellow
    }

    $errorLogPath = Join-Path $LogDir "error.log"
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config", "--", "--test-connect" -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

    $process.WaitForExit()

    if ($null -eq $process.ExitCode) {
        Write-Host "Application process ended without exit code." -ForegroundColor Yellow
    } elseif ($process.ExitCode -ne 0) {
        Write-Host "Test-connect finished with failures, exit code: $($process.ExitCode)" -ForegroundColor Red
    } else {
        Write-Host "Test-connect finished: all connections succeeded." -ForegroundColor Green
    }

    $testConnectLogPath = Join-Path $LogDir "test-connect.log"
    if (Test-Path -Path $testConnectLogPath -PathType Leaf) {
        Write-Host "Test-connect output:" -ForegroundColor Cyan
        Get-Content $testConnectLogPath -Encoding UTF8 | Write-Host
    }

    if (Test-Path -Path $errorLogPath -PathType Leaf) {
        $errContent = Get-Content $errorLogPath -Raw -ErrorAction SilentlyContinue
        if ($errContent -and $errContent.Trim().Length -gt 0) {
            Write-Host "Stderr:" -ForegroundColor Yellow
            Get-Content $errorLogPath | Write-Host
        }
    }
    exit $process.ExitCode
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
        if (Test-Path -Path $LogDir -PathType Container) {
            Get-ChildItem -Path $LogDir -File | Remove-Item -Force
            Write-Host "Logs cleared successfully" -ForegroundColor Green
        }
    } catch {
        Write-Host "Error clearing logs: $_" -ForegroundColor Yellow
    }
}

# Run application and capture error output（工作路径为 .run，config 与 log 在 .run 下）
$errorLogPath = Join-Path $LogDir "error.log"
$appArgs = if ($TestRdp) { "--test-rdp" } elseif ($TestScanPort) { "--test-scan-port" } else { "" }
try {
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }

    # 构建参数列表：只在有参数时才添加 "--" 和参数值
    $argList = @("run", "--project", "$projectPath", "--configuration", "$config")
    if ($appArgs -ne "") {
        $argList += "--"
        $argList += $appArgs
    }

    $process = Start-Process -FilePath "dotnet" -ArgumentList $argList -WorkingDirectory $RunDir -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

    # Wait for application to exit
    $process.WaitForExit()

    # Check exit code ($null = 进程被终止或无法获取，例如无界面环境)
    if ($null -eq $process.ExitCode) {
        Write-Host "Application process ended without exit code (e.g. terminated or no GUI). Build succeeded." -ForegroundColor Yellow
    } elseif ($process.ExitCode -ne 0) {
        Write-Host "Application exited abnormally, exit code: $($process.ExitCode)" -ForegroundColor Red

        # Read error log
        if (!(Test-Path -Path $LogDir -PathType Container)) {
            New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
        }
        if (Test-Path -Path $errorLogPath -PathType Leaf) {
            Write-Host "Error log content:" -ForegroundColor Yellow
            Get-Content $errorLogPath | Write-Host
        }
    } else {
        Write-Host "Application exited normally" -ForegroundColor Green
    }
} finally {
    # 若有内容的 crash/error 日志，或任意日志含 error 关键字，则输出该日志的完整路径
    if (Test-Path -Path $LogDir -PathType Container) {
        $pathsToShow = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

        # crash/error 命名的文件且非空时，输出完整路径
        $crashErrorFiles = Get-ChildItem -Path $LogDir -File | Where-Object { $_.Name -match "error|crash|exception" }
        foreach ($file in $crashErrorFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content.Trim().Length -gt 0) {
                [void]$pathsToShow.Add($file.FullName)
            }
        }

        # 任意日志文件中含 error/err/crash/fatal 整词时，输出完整路径（匹配 [ERR]、error、crash、fatal，避免误匹配 failed/exception 等）
        $allLogFiles = Get-ChildItem -Path $LogDir -File
        foreach ($file in $allLogFiles) {
            $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
            if ($content -and $content -match '\b(error|err|crash|fatal)\b') {
                [void]$pathsToShow.Add($file.FullName)
            }
        }

        foreach ($path in $pathsToShow) {
            Write-Host "Log: $path" -ForegroundColor Red
        }
    }
}
