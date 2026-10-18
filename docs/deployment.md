# Deployment (nothing is deployed automatically)
1. **Azure SQL** database; create contained users for the API and Functions managed identities (`CREATE USER [name] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader/db_datawriter ADD MEMBER ...`). Apply migrations from the pipeline or `dotnet ef migrations bundle`.
2. **Storage account** + container `release-attachments`; give identities `Storage Blob Data Contributor`. Set `Attachments__StorageAccountUrl`.
3. **App Service** (Linux, .NET 10) for the API with system-assigned identity. App settings: `ConnectionStrings__Default` (use `Authentication=Active Directory Default`), `AzureAd__*`, `Cors__AllowedOrigins__0`, `KeyVault__Uri`, `Graph__Enabled=true`, `Graph__SenderUser`, `Notifications__AppUrl`.
4. **Function App** (isolated .NET 10, plus its own storage account for `AzureWebJobsStorage`) with the same SQL/Graph settings. Keep one instance schedule owner (timer triggers are singletons).
5. **Key Vault** for secrets; **Application Insights** connection string in both apps.
6. **Entra**: app registrations and app roles (see security.md); assign users.
7. Smoke test: `/health`, sign in, `GET /api/v1/me`, create a ticket, watch `notification_queue` drain.
Rollback: redeploy the previous package; migrations are forward-only, so test them in a copy first.
