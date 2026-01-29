# 运行脚本
Write-Host "=== 启动 Tiema MVP ===" -ForegroundColor Green

# 先构建
& "./scripts/build.ps1"

# 运行
Write-Host "启动容器..." -ForegroundColor Yellow
cd "./src/Tiema.Runtime"
dotnet run

Write-Host "=== 运行结束 ===" -ForegroundColor Green