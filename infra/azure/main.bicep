@description('Azure region for the Container Apps environment.')
param location string = resourceGroup().location

@description('Container App name.')
param appName string = 'forgescope-policy'

@description('Container Apps environment name.')
param environmentName string = 'forgescope-production'

@description('Immutable GHCR image tag, preferably sha-<40 character commit>.')
param image string

@description('Numeric GitHub App ID.')
param githubAppId string

@secure()
@description('Base64 of the complete GitHub App PEM file.')
param githubPrivateKeyBase64 string

@description('PostgreSQL JDBC URL including sslmode=require or verify-full.')
param postgresJdbcUrl string

@description('Dedicated PostgreSQL user.')
param postgresUsername string

@secure()
@description('Dedicated PostgreSQL password.')
param postgresPassword string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
}

resource policy 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
      }
      secrets: [
        {
          name: 'github-private-key'
          value: githubPrivateKeyBase64
        }
        {
          name: 'postgres-password'
          value: postgresPassword
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'policy'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'FORGESCOPE_APP_ID', value: githubAppId }
            { name: 'FORGESCOPE_DISCOVER_INSTALLATIONS', value: 'true' }
            { name: 'FORGESCOPE_PRIVATE_KEY_BASE64', secretRef: 'github-private-key' }
            { name: 'SPRING_DATASOURCE_URL', value: postgresJdbcUrl }
            { name: 'SPRING_DATASOURCE_USERNAME', value: postgresUsername }
            { name: 'SPRING_DATASOURCE_PASSWORD', secretRef: 'postgres-password' }
            { name: 'JAVA_TOOL_OPTIONS', value: '-XX:MaxRAMPercentage=75 -XX:+ExitOnOutOfMemoryError' }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output containerAppName string = policy.name
