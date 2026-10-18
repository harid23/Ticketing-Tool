# Still Pending

Production-readiness items for the Release Ticketing application. Confirm each item against the target environment before launch. Optional services may be deferred only when the related features are intentionally out of scope.

## Required for a basic production launch

- [ ] **Production hosting**
  - Choose and configure hosting for the API and frontend.
  - Configure the frontend to use the deployed API URL.
  - Use HTTPS for public endpoints.
  - Set the deployed frontend origin as an API CORS allowed origin.

- [ ] **Microsoft Entra authentication**
  - Configure an Entra app registration for the API.
  - Expose the `access_as_user` delegated scope.
  - Set the API tenant ID, client ID, and audience.
  - Configure an Entra SPA app registration and register the exact production redirect URI.
  - Grant the SPA the API delegated permission and consent as required by the organization.
  - Configure the frontend build variables:
    - `VITE_RELEASE_API_URL`
    - `VITE_ENTRA_TENANT_ID`
    - `VITE_ENTRA_SPA_CLIENT_ID`
    - `VITE_ENTRA_API_SCOPE`
    - `VITE_AUTH_MODE=entra`
  - Do not put a client secret in the frontend.
  - Do not use local demo sign-in in Production.

- [ ] **Authorization and user/cluster setup**
  - Configure and assign the supported Entra app roles: `PM`, `ASDM`, and `Admin`.
  - Verify role claims are present in API access tokens and map to the intended application permissions.
  - Have each cluster manager sign in so the application creates their user record.
  - Associate each ASDM user with the correct cluster and set each cluster's manager in the database.
  - Test access with representative PM, ASDM, and Admin accounts, including attempts to access data outside their permissions.

- [ ] **Production database**
  - Provision the production SQL Server or Azure SQL database.
  - Configure `Database:Provider=SqlServer` and `ConnectionStrings:Default` through the hosting environment or a secret store.
  - Prefer managed identity where supported; otherwise store credentials securely and rotate them.
  - Grant the API identity only the database permissions it needs.
  - Apply and verify database migrations before directing production traffic.
  - Confirm backup, restore, retention, and recovery procedures.
  - Do not use the development SQLite database or a local database file in Production.

- [ ] **Secrets and configuration**
  - Keep credentials and sensitive settings out of source control and frontend build variables.
  - Configure production settings through the host's protected configuration or secret store.
  - If using Azure Key Vault, configure its URI and grant the application identity access to required secrets.
  - Verify production settings do not inherit local demo or development configuration.

## Required only if the corresponding feature is used

- [ ] **Attachments**
  - If users can upload or retrieve attachments in Production, provision durable blob storage and the `release-attachments` container.
  - Configure `Attachments:StorageAccountUrl` and grant the application identity the required blob permissions.
  - Review file type and size limits and confirm they meet business and security requirements.
  - Do not rely on the local attachment folder for production durability.

- [ ] **Scheduled jobs**
  - If SLA monitoring or notification queue processing is required, deploy the Azure Functions app.
  - Configure its Azure Functions host storage (`AzureWebJobsStorage`) and required SQL/Graph settings.
  - Confirm timer jobs run correctly and avoid unintended duplicate schedule owners.

- [ ] **Email notifications**
  - If the application must send email, configure Microsoft Graph, the sender account, required permissions/consent, and `Graph:Enabled`.
  - Configure `Graph:SenderUser` and verify delivery and failure handling.
  - If email is not part of the launch scope, explicitly accept that email notifications will not be sent.

## Strongly recommended before launch

- [ ] Configure Application Insights or equivalent logging, metrics, and alerting.
- [ ] Confirm logs do not expose tokens, passwords, connection strings, or other sensitive data.
- [ ] Set the correct `Notifications:AppUrl` for production links.
- [ ] Verify the business time zone and notification/SLA behavior.
- [ ] Confirm database and storage network restrictions, TLS, identity permissions, and firewall rules.
- [ ] Document deployment, rollback, incident response, and database restore procedures.

## Production smoke tests

- [ ] Verify the API `/health` endpoint reports healthy status, including its database check.
- [ ] Sign in through the deployed frontend using Entra.
- [ ] Verify `GET /api/v1/me` returns the expected signed-in user and role.
- [ ] Create and update a test ticket using authorized accounts.
- [ ] Verify unauthorized requests are rejected and users cannot access another cluster's restricted data.
- [ ] If enabled, upload and download an attachment and verify it persists in blob storage.
- [ ] If enabled, verify scheduled SLA processing and notification queue processing.
- [ ] If enabled, verify email delivery and failure visibility.
- [ ] Remove test data or clearly mark it after testing.

## Project references

- `README.md` — project status, prerequisites, and local run instructions.
- `docs/entra-setup.md` — Entra app registrations, SPA/API configuration, roles, and cluster assignment.
- `docs/deployment.md` — Azure hosting, SQL, storage, Functions, Key Vault, monitoring, and smoke-test outline.

## Scope note

Not every optional service must be launched on day one. If attachments, scheduled jobs, or email are intentionally out of scope, document that decision and verify the user experience does not promise those features. Authentication, authorization, production hosting, secure configuration, CORS, and a properly migrated and accessible production database are still required for a basic production release.
