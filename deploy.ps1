# Deploys the Cloudflare Worker front after wrangler login has been completed.
# Run:  pwsh -File deploy.ps1
$ErrorActionPreference = "Stop"

$workerDir = Join-Path $PSScriptRoot "worker"
if (-not (Test-Path $workerDir)) { $workerDir = Join-Path $PSScriptRoot "WebApplication1\..\worker" }
$workerDir = (Resolve-Path $workerDir).Path

Push-Location $workerDir
try {
    wrangler whoami
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Not authenticated. Opening OAuth login..." -ForegroundColor Yellow
        wrangler login
        if ($LASTEXITCODE -ne 0) { throw "wrangler login failed" }
    }

    $renderUrl = Read-Host "Render app URL (e.g. https://chess-app.onrender.com)"
    if ($renderUrl) {
        (Get-Content wrangler.toml) -replace 'ORIGIN = ".*"', "ORIGIN = `"$renderUrl`"" | Set-Content wrangler.toml
    }

    wrangler deploy
    Write-Host "Worker deployed. Your site is at: https://chess-front.<your-subdomain>.workers.dev" -ForegroundColor Green
}
finally {
    Pop-Location
}