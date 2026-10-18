# Microsoft Entra sign-in setup

The standalone React app uses MSAL Browser with redirect sign-in. The SPA requests a delegated API token and sends it as a bearer token; the API validates it with Microsoft.Identity.Web. No client secret belongs in the SPA.

## 1. Register the API

1. Create an Entra app registration for the ASP.NET Core API. Record its **Application (client) ID** and **Directory (tenant) ID**.
2. Under **Expose an API**, set the Application ID URI to `api://<api-client-id>` and add the delegated scope `access_as_user`.
3. Under **App roles**, create user/group roles with these exact values: `PM`, `ASDM`, and `Admin`.
4. Assign each ASDM user or group to the API's `ASDM` app role under **Enterprise applications → Users and groups**. ASDM users must also have a cluster assigned in the database.

## 2. Register the SPA

1. Create a separate app registration for the React SPA and add a **Single-page application** platform.
2. Add the exact deployed frontend URL as a redirect URI (for example `https://tickets.example.com/`). For local Entra testing also register `http://localhost:5174/`.
3. Under **API permissions**, add the API's delegated `access_as_user` permission and grant consent as required by the organization.
4. Do not create or put a client secret in the frontend.

## 3. Configure frontend build variables

Copy `frontend/.env.example` to the deployment's frontend environment and set:

- `VITE_RELEASE_API_URL`: deployed API origin, with no `/api/v1` suffix.
- `VITE_ENTRA_TENANT_ID`: tenant/directory ID.
- `VITE_ENTRA_SPA_CLIENT_ID`: SPA application/client ID.
- `VITE_ENTRA_API_SCOPE`: `api://<api-client-id>/access_as_user`.
- `VITE_AUTH_MODE`: `entra`.

Vite embeds `VITE_*` values into the build, so set these before building/releasing the frontend. The app displays a setup screen instead of falling back to localhost when production configuration is missing.

## 4. Configure the API and database

Set API configuration in the host's environment/secret store (never commit tenant-specific settings):

- `AzureAd__Instance=https://login.microsoftonline.com/`
- `AzureAd__TenantId=<tenant-id>`
- `AzureAd__ClientId=<api-client-id>`
- `AzureAd__Audience=api://<api-client-id>`
- `Cors__AllowedOrigins__0=https://tickets.example.com`
- `ConnectionStrings__Default=<production SQL Server connection string>`

Apply database migrations before deployment. Have each cluster manager sign in once so the API creates their `users` row, then associate that user and cluster in SQL. Example for EY2:

```sql
UPDATE users
SET ClusterId = (SELECT Id FROM clusters WHERE Code = 'EY2')
WHERE Email = 'manager@company.com';

UPDATE clusters
SET ManagerUserId = (SELECT Id FROM users WHERE Email = 'manager@company.com')
WHERE Code = 'EY2';
```

The role comes from the Entra access token and is synchronized to `user_roles`; do not grant app roles by inserting rows directly. The database `ClusterId` determines which cluster allocation ticket an ASDM user can manage. Legacy role claims (`SDM`, `Manager`, `ClusterManager`) are normalized to ASDM; `Head`, `PMO`, and `CompetencyPmo` are normalized to Admin.

## Local development

`frontend/.env.development` sets `VITE_AUTH_MODE=local`; this intentionally uses the backend's configurable Development-only local identity and sends no bearer token. To test real Entra sign-in locally, set `VITE_AUTH_MODE=entra`, provide the tenant/client/scope values, register the exact local redirect URI in the SPA app, and run the API in Development mode. Never enable the local identity fallback in a deployed Production environment.

For a local UI test without an Entra tenant, run the API and frontend in Development/local mode and open the frontend. Choose PM, ASDM (then select EY1–EY5), or Admin (demo password `admin123`). The frontend keeps that local persona in the current tab's session storage; Sign out returns to the role selector so you can test another persona. This demo password and role selection are not authentication and must never be used in Production. Production authorization always comes from validated Entra access tokens and the signed-in user's database cluster assignment.