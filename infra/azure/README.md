# ForgeScope policy service on Azure

This template creates one private Azure Container App worker. It does not expose
HTTP ingress and it never writes credentials to the repository. It reuses an
existing PostgreSQL server.

The GitHub workflow publishes `ghcr.io/brunnojob/forgescope-policy` after a
policy-service change reaches `main`. Deploy an immutable `sha-...` image tag,
not `latest`. Make the package public before deployment or configure an Azure
registry credential separately.

Create a database and role dedicated to ForgeScope. The JDBC URL has this form:

```text
jdbc:postgresql://SERVER.postgres.database.azure.com:5432/forgescope?sslmode=require
```

GitHub-generated RSA PEM files are accepted directly. Encode the whole file as
one base64 value before sending it to Azure. The value is a secret parameter and
is stored as a Container Apps secret.

```sh
az deployment group create \
  --resource-group YOUR_RESOURCE_GROUP \
  --template-file infra/azure/main.bicep \
  --parameters \
    image=ghcr.io/brunnojob/forgescope-policy:sha-COMMIT \
    githubAppId=5897767 \
    githubPrivateKeyBase64="$(base64 -w0 forgescope.private-key.pem)" \
    postgresJdbcUrl='jdbc:postgresql://SERVER.postgres.database.azure.com:5432/forgescope?sslmode=require' \
    postgresUsername=forgescope \
    postgresPassword='USE_A_SECURE_VALUE'
```

Do not paste the command with real values into issues, Actions logs or shell
history. For an interactive deployment, use a temporary shell and clear the
secret variables after the deployment. Keep exactly one replica because each
worker scans all active App installations.
