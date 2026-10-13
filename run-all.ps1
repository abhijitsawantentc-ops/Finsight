# Run all Portfolio Tracker microservices locally
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Starting Algorithmic Portfolio Tracker Microservices... " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$services = @(
    @{ Name = "MarketData.Service"; Path = "src/Services/MarketData.Service/MarketData.Service.csproj"; Port = 5020 },
    @{ Name = "RiskEngine.Service"; Path = "src/Services/RiskEngine.Service/RiskEngine.Service.csproj"; Port = 5041 },
    @{ Name = "Notification.Service"; Path = "src/Services/Notification.Service/Notification.Service.csproj"; Port = 5050 },
    @{ Name = "ApiGateway"; Path = "src/Services/ApiGateway/ApiGateway.csproj"; Port = 5000 }
)

foreach ($service in $services) {
    Write-Host "Starting $($service.Name) on port $($service.Port)..." -ForegroundColor Green
    Start-Process -FilePath "dotnet" -ArgumentList "run --project $($service.Path)" -WorkingDirectory $PSScriptRoot
}

Write-Host "`nAll services have been started!" -ForegroundColor Yellow
Write-Host "Dashboard & API Gateway: http://localhost:5000" -ForegroundColor Cyan
Write-Host "Press Ctrl+C or close windows to terminate services." -ForegroundColor Gray
