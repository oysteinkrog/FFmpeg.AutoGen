#!/usr/bin/env pwsh
param(
    [string]$HeadersPath = ".\staging\include",
    [string]$BinariesPath = ".\staging\bin",
    [string]$OutputPath = ".\FFmpeg.AutoGen.Bindings\generated",
    [string]$Namespace = "FFmpeg.AutoGen"
)

$ErrorActionPreference = "Stop"

# Generate FFmpeg bindings using the CppSharpUnsafeGenerator
# Usage: .\generate-bindings.ps1 [-HeadersPath <headers_path>] [-BinariesPath <binaries_path>] [-OutputPath <output_path>] [-Namespace <namespace>]

Write-Host "🔄 Generating FFmpeg bindings..." -ForegroundColor Blue

# Verify input paths
if (-not (Test-Path $HeadersPath)) {
    Write-Host "❌ Headers path not found: $HeadersPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $BinariesPath)) {
    Write-Host "❌ Binaries path not found: $BinariesPath" -ForegroundColor Red
    exit 1
}

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

Write-Host "📂 Headers:   $HeadersPath"
Write-Host "📂 Binaries:  $BinariesPath"
Write-Host "📂 Output:    $OutputPath"
Write-Host "📦 Namespace: $Namespace"

# Build generator if needed
Write-Host ""
Write-Host "🔨 Building generator..." -ForegroundColor Yellow
Push-Location ".\FFmpeg.AutoGen.CppSharpUnsafeGenerator"
try {
    dotnet build --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build generator"
    }
}
finally {
    Pop-Location
}

# Run generator
Write-Host "⚡ Running generator..." -ForegroundColor Yellow
$generatorArgs = @(
    "run"
    "--project"
    ".\FFmpeg.AutoGen.CppSharpUnsafeGenerator"
    "--configuration"
    "Release"
    "--"
    "--namespace"
    $Namespace
    "--headers"
    $HeadersPath
    "--bin"
    $BinariesPath
    "--output"
    $OutputPath
)

& dotnet @generatorArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Code generation failed" -ForegroundColor Red
    exit 1
}

# Copy required support files to bindings project
Write-Host ""
Write-Host "📋 Setting up bindings project..." -ForegroundColor Blue

$BindingsDir = ".\FFmpeg.AutoGen.Bindings"
$BindingsGenDir = "$BindingsDir\generated"

# Copy core support files from legacy project
$SupportFiles = @(
    "ConstCharPtrMarshaler.cs",
    "UTF8Marshaler.cs",
    "IFixedArray.cs",
    "FunctionResolverBase.cs",
    "FunctionResolverFactory.cs",
    "IFunctionResolver.cs"
)

foreach ($file in $SupportFiles) {
    $sourcePath = ".\FFmpeg.AutoGen\$file"
    $destPath = "$BindingsDir\$file"

    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destPath -Force
        Write-Host "   Copied: $file"
    }
}

# Copy Native directory
$NativeSourceDir = ".\FFmpeg.AutoGen\Native"
$NativeDestDir = "$BindingsDir\Native"

if (Test-Path $NativeSourceDir) {
    if (Test-Path $NativeDestDir) {
        Remove-Item -Recurse -Force $NativeDestDir
    }
    Copy-Item -Path $NativeSourceDir -Destination $NativeDestDir -Recurse -Force
    Write-Host "   Copied: Native directory"
}

# Files are now generated directly to the unified Bindings project

Write-Host "✅ Bindings generation completed successfully" -ForegroundColor Green

# Show generated files
Write-Host ""
Write-Host "📋 Generated files:" -ForegroundColor Blue
if (Test-Path $BindingsGenDir) {
    Get-ChildItem -Path $BindingsGenDir -Filter "*.cs" | ForEach-Object {
        $size = [Math]::Round($_.Length / 1KB, 0)
        $sizeUnit = if ($size -gt 1024) { "$([Math]::Round($_.Length / 1MB, 1))MB" } else { "$($size)KB" }
        Write-Host "   $($_.Name) ($sizeUnit)"
    }
}

# Build the generated bindings project
Write-Host ""
Write-Host "🔨 Building bindings project..." -ForegroundColor Yellow
Push-Location $BindingsDir
try {
    dotnet build --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Failed to build bindings project" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Bindings project built successfully" -ForegroundColor Green
}
finally {
    Pop-Location
}