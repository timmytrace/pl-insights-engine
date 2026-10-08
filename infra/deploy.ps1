<#
.SYNOPSIS
  Deploys Virtual Studio Crew to Azure Container Apps.

.DESCRIPTION
  1. Registers the resource providers the deployment needs (free, one-off).
  2. Deploys infra/main.bicep: registry, managed identity and roles, Container Apps environment, app.
  3. Builds the image in Azure Container Registry (no local Docker needed) and rolls the app onto it.

  Requires the Azure CLI, signed in (az login), with Owner on the resource group so the template
  can assign the identity its roles.

.EXAMPLE
  ./infra/deploy.ps1 -ResourceGroup rg-studio-crew -AiAccount studio-crew-ai-1991

.EXAMPLE
  # Switch the hosted demo to the offline crew (no model cost), e.g. before credits expire:
  az containerapp update -n studio-crew -g rg-studio-crew --set-env-vars Crew__Mode=mock
#>
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $AiAccount,
    [ValidateSet('azure', 'mock')] [string] $CrewMode = 'azure'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Az { az @args; if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed" } }

Write-Host "1/3 Registering resource providers..."
foreach ($ns in 'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.OperationalInsights', 'Microsoft.ManagedIdentity') {
    Invoke-Az provider register --namespace $ns --wait | Out-Null
}

Write-Host "2/3 Deploying infrastructure..."
$outputs = Invoke-Az deployment group create -g $ResourceGroup -n studio-crew `
    -f (Join-Path $PSScriptRoot 'main.bicep') `
    -p aiAccountName=$AiAccount crewMode=$CrewMode `
    --query properties.outputs -o json | ConvertFrom-Json

$tag = (git -C $root rev-parse --short HEAD).Trim()
Write-Host "3/3 Building image studio-crew:$tag in $($outputs.registryName.value)..."
Invoke-Az acr build -r $outputs.registryName.value -t "studio-crew:$tag" -f (Join-Path $root 'Dockerfile') $root | Out-Null
Invoke-Az containerapp update -n $outputs.appName.value -g $ResourceGroup --image "$($outputs.registryServer.value)/studio-crew:$tag" | Out-Null

Write-Host ""
Write-Host "Live at $($outputs.url.value)"
