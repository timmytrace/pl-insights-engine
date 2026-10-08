// Virtual Studio Crew on Azure Container Apps.
//
// One container serves the overlay, the REST API and the replay WebSocket. It signs in to the
// existing Azure AI Foundry resource with a user-assigned managed identity, so no keys are
// stored anywhere. Scales to zero when nobody is watching.

@description('Location for the registry, identity and logs. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Location for the Container Apps environment and app. Separate because Container Apps capacity varies by region.')
param appLocation string = location

@description('Name of the existing Azure AI Foundry (AIServices) account in this resource group.')
param aiAccountName string

@description('Model deployment the crew uses.')
param modelDeployment string = 'gpt-4.1-mini'

@description('"azure" for the live crew; "mock" runs the offline scripted crew at no model cost.')
@allowed(['azure', 'mock'])
param crewMode string = 'azure'

@description('Base name for the new resources.')
param name string = 'studio-crew'

@description('Container image. The deploy script builds and sets the real one after the first deployment.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Minimum replicas. 0 scales to zero when idle (cheapest, a few seconds of cold start).')
@minValue(0)
param minReplicas int = 0

var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  openAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  speechUser: 'f2dc8367-1007-4938-bd23-fe263f013447'
}

resource ai 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiAccountName
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${name}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// Traces from the crew: every replay, workflow step, agent run and model call.
resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${name}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: take('${replace(name, '-', '')}${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${name}-identity'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, identity.id, roles.acrPull)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ai
  name: guid(ai.id, identity.id, roles.openAiUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.openAiUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource speechUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: ai
  name: guid(ai.id, identity.id, roles.speechUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.speechUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${name}-${appLocation}-env'
  location: appLocation
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: appLocation
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'   // HTTP/1.1 upgrades for the replay WebSocket
      }
      registries: [
        { server: registry.properties.loginServer, identity: identity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'studio'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'Crew__Mode', value: crewMode }
            { name: 'Crew__Endpoint', value: 'https://${aiAccountName}.openai.azure.com/' }
            { name: 'Crew__Deployment', value: modelDeployment }
            { name: 'Crew__SpeechRegion', value: ai.location }
            { name: 'Crew__SpeechResourceId', value: ai.id }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }   // DefaultAzureCredential picks the managed identity
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/api/health', port: 8080 }, periodSeconds: 30 }
          ]
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: 2 }
    }
  }
  dependsOn: [acrPull, openAiUser, speechUser]
}

output url string = 'https://${app.properties.configuration.ingress.fqdn}'
output registryName string = registry.name
output registryServer string = registry.properties.loginServer
output appName string = app.name
output insightsName string = insights.name
