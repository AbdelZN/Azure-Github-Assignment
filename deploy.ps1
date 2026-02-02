param(
  [string]$ResourceGroupName = "rg-azure-github-assignment",
  [string]$Location = "westeurope",
  [string]$Prefix = "ccd",
  [string]$FunctionsProjectPath = ".\src\Functions\Functions.csproj"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Ensuring Azure login..."
az account show | Out-Null

Write-Host "==> Creating resource group (if needed)..."
az group create --name $ResourceGroupName --location $Location | Out-Null

Write-Host "==> Deploying Bicep..."
$deployment = az deployment group create `
  --resource-group $ResourceGroupName `
  --template-file ".\infra\main.bicep" `
  --parameters prefix=$Prefix location=$Location `
  --query "properties.outputs" -o json | ConvertFrom-Json

$functionAppName = $deployment.functionAppName.value
Write-Host "==> Function App: $functionAppName"

Write-Host "==> Publishing function project..."
$publishDir = Join-Path $PSScriptRoot "publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $FunctionsProjectPath -c Release -o $publishDir | Out-Null

Write-Host "==> Creating zip package..."
$zipPath = Join-Path $PSScriptRoot "publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "==> Deploying zip to Azure Function App..."
az functionapp deployment source config-zip `
  --resource-group $ResourceGroupName `
  --name $functionAppName `
  --src $zipPath | Out-Null

Write-Host "==> Done."
Write-Host "Test endpoints after deploy:"
Write-Host "  https://$functionAppName.azurewebsites.net/api/StartImageSet"
Write-Host "  https://$functionAppName.azurewebsites.net/api/GetStatus?jobId=<jobId>"
Write-Host "  https://$functionAppName.azurewebsites.net/api/GetResults?jobId=<jobId>"

# Optional cleanup
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }