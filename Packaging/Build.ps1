param(
    [ValidateSet("Release", "Canary")]
    [string]$Mode = "Release",

    # Canary 模式下的证书配置
    [string]$CanaryPublisher = "CN=凌莞"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path "$PSScriptRoot\.."

# ==========================================
# 1. 版本号处理
# ==========================================
Write-Host "Calculating version..." -ForegroundColor Cyan
$BuildVersion = "1.0.0.0"

try {
    Push-Location $ProjectRoot
    $gitDescribe = git describe --tags --long
    Pop-Location
    
    if ($gitDescribe -match "v?(\d+\.\d+(?:\.\d+)?)-(\d+)-g[0-9a-f]+") {
        $baseVer = $Matches[1]
        $commitCount = $Matches[2]
        
        $baseVerFull = if ($baseVer.Split('.').Length -eq 2) { "$baseVer.0" } else { $baseVer }
        
        if ($Mode -eq "Canary") {
            $BuildVersion = "$baseVerFull.$commitCount"
        } else {
            $BuildVersion = "$baseVerFull.0"
        }
    } else {
        Write-Warning "Git describe format mismatch: $gitDescribe. Fallback to $BuildVersion"
    }
} catch {
    Write-Warning "Git describe failed. Fallback to $BuildVersion"
}

Write-Host "Target Version: $BuildVersion" -ForegroundColor Green

# 修改 C# 源码版本号
# Canary 模式使用完整的 4 段版本号 ($BuildVersion)
# Release 模式使用 3 段版本号 ($baseVer)
Write-Host "Updating C# source version..." -ForegroundColor Cyan
$AppMainPath = "$ProjectRoot\MaiChartManager\AppMain.g.cs"

if ($Mode -eq "Canary") {
    $SourceVersion = $BuildVersion
} else {
    $SourceVersion = $baseVer
}

$AppMainContent = @"
    // Auto-generated file. Do not modify manually.
    namespace MaiChartManager;

    public partial class AppMain
    {
        public const string Version = "$SourceVersion";
    }
"@
Set-Content $AppMainPath $AppMainContent -Encoding UTF8


# ==========================================
# 2. 清理环境
# ==========================================
Write-Host "Cleaning up..." -ForegroundColor Cyan
$PackDir = "$PSScriptRoot\Pack"
if (Test-Path $PackDir) { Remove-Item $PackDir -Recurse -Force }
Remove-Item "$PSScriptRoot\*.appx" -ErrorAction SilentlyContinue
Remove-Item "$PSScriptRoot\*.msix" -ErrorAction SilentlyContinue

# Canary 模式下 wwwroot 由 CI 的 Linux job 预构建，跳过前端构建
# Release 模式下（本地构建）始终构建前端
if ($Mode -ne "Canary" -or -not (Test-Path "$ProjectRoot\MaiChartManager\wwwroot\index.html")) {
    Write-Host "Building Frontend..." -ForegroundColor Cyan
    Push-Location "$ProjectRoot"
    try {
        pnpm install
        if ($LASTEXITCODE -ne 0) { throw "pnpm install failed with exit code $LASTEXITCODE" }
        pnpm --filter mcm-frontend build
        if ($LASTEXITCODE -ne 0) { throw "Frontend build failed with exit code $LASTEXITCODE" }
    } finally {
        Pop-Location
    }
} else {
    Write-Host "wwwroot already exists, skipping frontend build." -ForegroundColor Yellow
}

# ==========================================
# 5. 发布主程序
# ==========================================
Write-Host "Publishing MaiChartManager..." -ForegroundColor Cyan
Push-Location "$ProjectRoot"
try {
    # Determine Configuration
    $ConfigName = if ($Mode -eq "Canary") { "Crack" } else { "Release" }
    Write-Host "Using Configuration: $ConfigName" -ForegroundColor Yellow
    
    dotnet publish Sitreamai.slnx -c $ConfigName
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

# ==========================================
# 6. 准备打包目录
# ==========================================
Write-Host "Preparing Package Directory..." -ForegroundColor Cyan

# 复制 Base 资源
Copy-Item "$PSScriptRoot\Base\*" $PackDir -Recurse -Force

# 复制 Canary Base 资源 (如果存在且是 Canary 模式)
if ($Mode -eq "Canary") {
    $BaseCanary = "$PSScriptRoot\Base-Canary"
    if (Test-Path $BaseCanary) {
        Write-Host "Copying Canary assets..." -ForegroundColor Cyan
        Copy-Item "$BaseCanary\*" $PackDir -Recurse -Force
    }
}

# ==========================================
# 7. 修改 Manifest (仅 Canary)
# ==========================================
if ($Mode -eq "Canary") {
    Write-Host "Patching Manifest for Canary..." -ForegroundColor Cyan
    $ManifestPath = "$PackDir\AppxManifest.xml"
    
    [xml]$xml = Get-Content $ManifestPath

    # 命名空间管理器
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace("x", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $ns.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
    $ns.AddNamespace("desktop", "http://schemas.microsoft.com/appx/manifest/desktop/windows10")
    $ns.AddNamespace("uap3", "http://schemas.microsoft.com/appx/manifest/uap/windows10/3")

    # 修改 Identity
    $xml.Package.Identity.Name = $xml.Package.Identity.Name + ".Canary"
    $xml.Package.Identity.Publisher = $CanaryPublisher
    $xml.Package.Identity.Version = $BuildVersion

    # 修改 Properties
    $xml.Package.Properties.DisplayName = "MaiChartManager (Canary)"
    $xml.Package.Properties.PublisherDisplayName = "凌莞"
    
    # 修改 Applications
    foreach ($app in $xml.Package.Applications.Application) {
        if ($app.VisualElements) {
            $app.VisualElements.DisplayName = $app.VisualElements.DisplayName + " (Canary)"
        }
        
        if ($app.Id -eq "CliTool") {
            $aliasNode = $app.SelectSingleNode(".//desktop:ExecutionAlias", $ns)
            $aliasNode.Alias = "mcmc.exe"
        }
        if ($app.Id -eq "MuConvert") {
            $aliasNode = $app.SelectSingleNode(".//desktop:ExecutionAlias", $ns)
            $aliasNode.Alias = "MuConvertCanary.exe"
        }
    }

    $xml.Save($ManifestPath)
} else {
    # Release 模式也要更新 Version
    $ManifestPath = "$PackDir\AppxManifest.xml"
    [xml]$xml = Get-Content $ManifestPath
    $xml.Package.Identity.Version = $BuildVersion
    $xml.Save($ManifestPath)
}

# ==========================================
# 8. 生成 PRI 并打包
# ==========================================
Write-Host "Generating PRI and Packing..." -ForegroundColor Cyan
Push-Location $PackDir
try {
    Remove-Item "priconfig.xml" -ErrorAction SilentlyContinue
    Remove-Item "*.pri" -ErrorAction SilentlyContinue
    
    makepri.exe createconfig /cf priconfig.xml /dq zh-CN
    makepri.exe new /pr . /cf .\priconfig.xml
    Remove-Item "priconfig.xml"
    
    $OutputName = if ($Mode -eq "Canary") { "MaiChartManager_Canary_$BuildVersion.appx" } else { "Store64.appx" }
    $OutputAppx = "$PSScriptRoot\$OutputName"
    
    makeappx pack /d . /p $OutputAppx
} finally {
    Pop-Location
}

# ==========================================
# 9. 签名 (仅 Canary)
# ==========================================
if ($Mode -eq "Canary") {
    Write-Host "Signing Appx..." -ForegroundColor Cyan
    
    $SignCmd = "D:\Sign\signcode.cmd"
    if (Test-Path $SignCmd) {
        # 直接调用 cmd 脚本
        & $SignCmd $OutputAppx
        Write-Host "Build & Sign Complete: $OutputAppx" -ForegroundColor Green
    } else {
        Write-Warning "Sign script not found at $SignCmd. Skipping signing."
    }
} else {
    Write-Host "Build Complete: $OutputAppx" -ForegroundColor Green
}
