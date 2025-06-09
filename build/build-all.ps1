#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$true)]
    [string]$FFmpegTar,

    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# Complete FFmpeg.AutoGen build pipeline
# Usage: .\build-all.ps1 <ffmpeg_tar> [-Clean]

if (-not $FFmpegTar) {
    Write-Host "❌ Usage: $($MyInvocation.MyCommand.Name) <ffmpeg_tar> [-Clean]" -ForegroundColor Red
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\build-all.ps1 ffmpeg-7.1.0.tar"
    Write-Host "  .\build-all.ps1 ffmpeg-7.1.0.tar -Clean"
    exit 1
}

Write-Host "🚀 Starting FFmpeg.AutoGen build pipeline" -ForegroundColor Green
Write-Host "📦 Input: $FFmpegTar"
Write-Host "🕒 Started: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

$StartTime = Get-Date

# Clean build if requested
if ($Clean) {
    Write-Host ""
    Write-Host "🧹 Cleaning previous build..." -ForegroundColor Yellow
    $CleanPaths = @(".\staging", ".\packages", ".\FFmpeg.AutoGen.Bindings\generated", ".\FFmpeg.AutoGen.Redist\runtimes")
    foreach ($path in $CleanPaths) {
        if (Test-Path $path) {
            Remove-Item -Recurse -Force $path
            Write-Host "   Cleaned: $path"
        }
    }
}

# Step 1: Extract FFmpeg
Write-Host ""
Write-Host "🔸 Step 1: Extracting FFmpeg archive" -ForegroundColor Cyan
& ".\build\extract-ffmpeg.ps1" -FFmpegTar $FFmpegTar -OutputPath ".\staging"
if ($LASTEXITCODE -ne 0) { throw "Extract failed" }

# Step 2: Generate bindings
Write-Host ""
Write-Host "🔸 Step 2: Generating C# bindings" -ForegroundColor Cyan
& ".\build\generate-bindings.ps1" -HeadersPath ".\staging\include" -BinariesPath ".\staging\bin"
if ($LASTEXITCODE -ne 0) { throw "Generate bindings failed" }

# Step 3: Create packages
Write-Host ""
Write-Host "🔸 Step 3: Creating NuGet packages" -ForegroundColor Cyan
& ".\build\create-packages.ps1" -StagingPath ".\staging"
if ($LASTEXITCODE -ne 0) { throw "Create packages failed" }

$EndTime = Get-Date
$Duration = $EndTime - $StartTime

Write-Host ""
Write-Host "🎉 Build pipeline completed successfully!" -ForegroundColor Green
Write-Host "⏱️  Total time: $($Duration.ToString('mm\:ss'))"

# Show final output
Write-Host ""
Write-Host "📊 Build summary:" -ForegroundColor Blue

$BindingsFiles = @(Get-ChildItem -Path ".\FFmpeg.AutoGen.Bindings\generated" -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue)
if ($BindingsFiles.Count -gt 0) {
    Write-Host "   Generated files: $($BindingsFiles.Count)"
}

$Packages = @(Get-ChildItem -Path ".\packages" -Filter "*.nupkg" -ErrorAction SilentlyContinue)
if ($Packages.Count -gt 0) {
    Write-Host "   NuGet packages: $($Packages.Count)"
    foreach ($pkg in $Packages) {
        $size = [Math]::Round($pkg.Length / 1MB, 1)
        $sizeUnit = if ($size -gt 1) { "$($size)MB" } else { "$([Math]::Round($pkg.Length / 1KB, 0))KB" }
        Write-Host "     • $($pkg.Name) ($sizeUnit)"
    }
}