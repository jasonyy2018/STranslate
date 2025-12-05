$ErrorActionPreference = "Stop"

function Log([string]$msg, [string]$color = "Yellow") {
    Write-Host $msg -ForegroundColor $color
}

# 清理构建输出
Log "正在清理之前的构建..."
$artifactPath = "..\src\.artifacts\Release\"

if (Test-Path $artifactPath) {
    Remove-Item -Path $artifactPath -Recurse -Force -ErrorAction SilentlyContinue
}

# 更新 Fody 配置文件
Log "正在更新 FodyWeavers..."

$src = "../src/STranslate/FodyWeavers.Release.xml"
$bak = "../src/STranslate/FodyWeavers.xml.bak"
$dst = "../src/STranslate/FodyWeavers.xml"

if (Test-Path $src) {
    Copy-Item $src $bak -Force
    Move-Item -Path $bak -Destination $dst -Force
} else {
    Log "未找到 $src，跳过更新。" "Red"
}

# 构建解决方案
Log "正在重新生成解决方案..."
dotnet build ..\src\STranslate.sln --configuration Release --no-incremental

# 还原 FodyWeavers.xml
Log "正在还原 FodyWeavers.xml..."
git restore $dst

# 清理插件目录中多余文件
Log "正在清理多余的 STranslate.Plugin 文件..."

$pluginsPath = "../src/.artifacts/Release/Plugins"
if (Test-Path $pluginsPath) {
    Get-ChildItem -Path $pluginsPath -Recurse -Include "STranslate.Plugin.dll","STranslate.Plugin.xml" |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Log "构建完成！" "Green"
