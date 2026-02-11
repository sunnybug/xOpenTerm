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
    Read-Host "Press Enter to close window"
    exit $LASTEXITCODE
}

Write-Host "Build successful, starting application..." -ForegroundColor Green

# Run application and capture error output
try {
    # Switch to runtime working directory
    Push-Location $RunDir

    # Run application and capture output
    $errorLogPath = Join-Path "log" "error.log"

    # Use dotnet run to run project directly
    $projectPath = Join-Path $Root "src\xOpenTerm.csproj"
    $config = if ($Release) { "Release" } else { "Debug" }
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "$projectPath", "--configuration", "$config" -NoNewWindow -PassThru -RedirectStandardError $errorLogPath

    # Wait for application to exit
    $process.WaitForExit()

    # Check exit code
    if ($process.ExitCode -ne 0) {
        Write-Host "Application exited abnormally, exit code: $($process.ExitCode)" -ForegroundColor Red

        # Read error log
        if (Test-Path -Path $errorLogPath -PathType Leaf) {
            Write-Host "Error log content:" -ForegroundColor Yellow
            Get-Content $errorLogPath | Write-Host
        }
    } else {
        Write-Host "Application exited normally" -ForegroundColor Green
    }
} finally {
    # Restore original directory
    Pop-Location
}