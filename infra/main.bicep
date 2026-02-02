@description('Base name used for all resources (letters/numbers only recommended).')
param prefix string = 'ccd'

@description('Azure region')
param location string = resourceGroup().location

@description('Name of the Function App (must be globally unique)')
param functionAppName string = toLower('${prefix}-func-${uniqueString(resourceGroup().id)}')

@description('Name of the Storage Account (must be globally unique, 3-24 chars, lowercase letters/numbers)')
param storageAccountName string = toLower('${prefix}sa${uniqueString(resourceGroup().id)}')

var queueStart = 'queue-start'
var queueProcess = 'queue-process'
var queueStartPoison = 'queue-start-poison'
var queueProcessPoison = 'queue-process-poison'
var blobContainerName = 'images'

// Storage account
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false 
    supportsHttpsTrafficOnly: true
  }
}

// Blob container
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  name: '${storage.name}/default'
}
resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storage.name}/default/${blobContainerName}'
  properties: {
    publicAccess: 'None'
  }
  dependsOn: [ blobService ]
}

// Queues
resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-01-01' = {
  name: '${storage.name}/default'
}
resource qStart 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${storage.name}/default/${queueStart}'
  dependsOn: [ queueService ]
}
resource qProcess 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${storage.name}/default/${queueProcess}'
  dependsOn: [ queueService ]
}
// Optional but useful in Azure debugging (poison queues)
resource qStartPoison 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${storage.name}/default/${queueStartPoison}'
  dependsOn: [ queueService ]
}
resource qProcessPoison 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-01-01' = {
  name: '${storage.name}/default/${queueProcessPoison}'
  dependsOn: [ queueService ]
}

// App Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: toLower('${prefix}-appi-${uniqueString(resourceGroup().id)}')
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

// Consumption plan
resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: toLower('${prefix}-plan-${uniqueString(resourceGroup().id)}')
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

// Function App (Windows consumption: simplest)
resource func 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        // Required runtime settings for dotnet-isolated
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }

        // Storage connection for queues + blobs
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}' }

        // App Insights
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY', value: appInsights.properties.InstrumentationKey }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }

        // Keep “always on” not needed for consumption; omit
      ]
    }
  }
  dependsOn: [
    storage
    imagesContainer
    qStart
    qProcess
    appInsights
    plan
  ]
}

output functionAppName string = func.name
output storageAccountName string = storage.name
output blobContainerName string = blobContainerName
output queueStartName string = queueStart
output queueProcessName string = queueProcess
