<#
.SYNOPSIS
    本地构建并打包 RcloneHelper
.DESCRIPTION
    模拟 GitHub Actions 的构建流程，在本地生成发布产物
.PARAMETER Runtime
    目标运行时，默认 win-x64
.PARAMETER Version
    版本号，如 v1.0.0。不指定则从 Assembly 读取
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime linux-x64
    .\build.ps1 -Version v1.0.0
#>
param(
    [string]$Runtime = "win-x64",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$ProjectDir = "RcloneHelper"
$OutputDir = "publish/$Runtime"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RcloneHelper Build ($Runtime)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 获取版本号
if ([string]::IsNullOrEmpty($Version)) {
    $Version = (dotnet msbuild "$ProjectDir/RcloneHelper.csproj" -getProperty:Version -nologo 2>$null) -replace '\s',''
    if ([string]::IsNullOrEmpty($Version)) {
        $Version = "0.0.0-dev"
    }
}
Write-Host "Version: $Version"
Write-Host ""

# 清理旧产物
if (Test-Path $OutputDir) {
    Write-Host "Cleaning old publish files..."
    Remove-Item -Recurse -Force $OutputDir
}

# 构建发布
Write-Host "[Step 1/3] Publishing application..."
dotnet publish "$ProjectDir/RcloneHelper.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Publish failed!" -ForegroundColor Red
    exit 1
}

# 清理 PDB
Write-Host "[Step 2/3] Cleaning up..."
Remove-Item -Path "$OutputDir/*.pdb" -ErrorAction SilentlyContinue

# 打包 ZIP
$ZipName = "RcloneHelper-$Version-$Runtime.zip"
Write-Host "[Step 3/3] Creating ZIP: $ZipName"
Compress-Archive -Path "$OutputDir/*" -DestinationPath $ZipName -Force

$Size = (Get-Item $ZipName).Length / 1MB
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Build completed!" -ForegroundColor Green
Write-Host "  Output: $OutputDir" -ForegroundColor Green
Write-Host "  Package: $ZipName ($([math]::Round($Size, 1)) MB)" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
