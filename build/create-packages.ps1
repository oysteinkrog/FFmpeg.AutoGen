#!/usr/bin/env pwsh
param(
    [string]$StagingPath = ".\staging",
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\packages"
)

$ErrorActionPreference = "Stop"

# Create FFmpeg.AutoGen NuGet packages
# Usage: .\create-packages.ps1 [-StagingPath <staging_path>] [-Configuration <configuration>] [-OutputPath <output_path>]

Write-Host "🔄 Creating NuGet packages..." -ForegroundColor Blue

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Copy native libraries to redist project
Write-Host "📦 Preparing native libraries..." -ForegroundColor Yellow
$RedistRuntimePath = ".\FFmpeg.AutoGen.Redist\runtimes\win-x64\native"

if (-not (Test-Path $RedistRuntimePath)) {
    New-Item -ItemType Directory -Path $RedistRuntimePath -Force | Out-Null
}

# Copy DLLs from staging
$BinPath = Join-Path $StagingPath "bin"
if (Test-Path $BinPath) {
    $dlls = Get-ChildItem -Path $BinPath -Filter "*.dll" -ErrorAction SilentlyContinue
    if ($dlls) {
        $dlls | Copy-Item -Destination $RedistRuntimePath -Force
        Write-Host "   Copied $($dlls.Count) DLLs"
    } else {
        Write-Host "⚠️  No DLL files found at $BinPath - using existing libraries" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠️  No binaries found at $BinPath - using existing libraries" -ForegroundColor Yellow
}

# Build bindings package
Write-Host ""
Write-Host "🔨 Building FFmpeg.AutoGen.Bindings..." -ForegroundColor Yellow
$bindingsArgs = @(
    "pack"
    ".\FFmpeg.AutoGen.Bindings\FFmpeg.AutoGen.Bindings.csproj"
    "--configuration"
    $Configuration
    "--output"
    $OutputPath
    "--verbosity"
    "quiet"
)

& dotnet @bindingsArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to build bindings package" -ForegroundColor Red
    exit 1
}

# Build redist package
Write-Host "🔨 Building FFmpeg.AutoGen.Redist..." -ForegroundColor Yellow
$redistArgs = @(
    "pack"
    ".\FFmpeg.AutoGen.Redist\FFmpeg.AutoGen.Redist.csproj"
    "--configuration"
    $Configuration
    "--output"
    $OutputPath
    "--verbosity"
    "quiet"
)

& dotnet @redistArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to build redist package" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Package creation completed successfully" -ForegroundColor Green

# Show created packages
Write-Host ""
Write-Host "📦 Created packages:" -ForegroundColor Blue
$packages = Get-ChildItem -Path $OutputPath -Filter "*.nupkg" -ErrorAction SilentlyContinue
foreach ($package in $packages) {
    $size = [Math]::Round($package.Length / 1MB, 1)
    $sizeUnit = if ($size -gt 1) { "$($size)MB" } else { "$([Math]::Round($package.Length / 1KB, 0))KB" }
    Write-Host "   $($package.Name) ($sizeUnit)"
}

Write-Host ""
Write-Host "🎯 Package usage:" -ForegroundColor Green
Write-Host "   Bindings only: <PackageReference Include=`"FFmpeg.AutoGen.Bindings`" Version=`"7.1.1`" />"
Write-Host "   Complete:      <PackageReference Include=`"FFmpeg.AutoGen.Bindings`" Version=`"7.1.1`" />"
Write-Host "                  <PackageReference Include=`"FFmpeg.AutoGen.Redist`" Version=`"7.1.1`" />"