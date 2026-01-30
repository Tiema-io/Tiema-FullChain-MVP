# 构建脚本
Write-Host "=== 构建 Tiema MVP ===" -ForegroundColor Green

# 清理
Remove-Item -Path "./plugins/*" -Force -ErrorAction SilentlyContinue

# 构建SDK
Write-Host "构建 SDK..." -ForegroundColor Yellow
dotnet build "./src/Tiema.Plugin.Sdk" -c Release -o "./lib/"

# 构建插件
Write-Host "构建插件..." -ForegroundColor Yellow
dotnet build "./src/Demo.Plugins/modbussensor" -c Release -o "./plugins/"
dotnet build "./src/Demo.Plugins/simplealarm" -c Release -o "./plugins/"
dotnet build "./src/Demo.Plugins/temperaturelogic" -c Release -o "./plugins/"


# 构建运行时
Write-Host "构建运行时..." -ForegroundColor Yellow  
dotnet build "./src/Tiema.Runtime" -c Release 

Write-Host "=== 构建完成 ===" -ForegroundColor Green
