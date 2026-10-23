# Release Request Management

Internal ticketing module for releasing employees from projects and offering them to other clusters.
Backend: ASP.NET Core Web API (.NET 10) + EF Core + Azure SQL. Jobs: Azure Functions. Frontend: a React/TypeScript module for the existing APEX app.

> Status: local development build and SQLite-compatible workflow tests pass. This is not yet a production deployment: production identity, SQL, CORS, storage, Graph, monitoring, and hosting settings must be supplied and validated for your environment.

## Layout
```
src/ReleaseTicketing.Core        Entities, DbContext, services (all business rules), DTOs. No web code.
src/ReleaseTicketing.Api         Controllers, Entra ID auth, error handling. Thin.
src/ReleaseTicketing.Functions   Timer jobs: SLA monitor (15 min), notification worker (1 min).
tests/ReleaseTicketing.Tests     xUnit tests (SQLite in memory; one SQL Server concurrency test).
frontend/src/modules/release-requests   React module to copy into the existing app.
docs/                            Architecture, rules, state machine, deployment, open questions.
```
Why a separate `Core`? The API and the Azure Functions both need the same rules (e.g. "close a ticket"). Putting them in one shared library means they cannot drift apart.

## Prerequisites
- .NET 10 SDK (`dotnet --version`)
- Node.js 20+ and npm
- SQL Server LocalDB or Docker SQL Server, or an Azure SQL database
- EF tools: `dotnet tool install --global dotnet-ef`
- Azure Functions Core Tools v4 (only to run the functions locally)

## First run
### Local testing without SQL Server
The API uses a local SQLite file by default in the Development environment, so SQL Server and SSMS are not needed for basic local testing. Install the .NET 10 SDK, then from the repository root run:
```bash
dotnet run --project src/ReleaseTicketing.Api/ReleaseTicketing.Api.csproj --urls http://localhost:5000
```
The API creates `release-ticketing.local.db` automatically. This isolated local database is for development/testing only; deployment continues to use SQL Server. The frontend is already configured to use `http://localhost:5000` in development.
The frontend shows a local demo sign-in page. Choose PM to enter as a project manager, ASDM to select one of the five clusters, or Admin and enter the demo password `admin123`. Sign out returns to the role selector, so you can test multiple personas in the same browser tab. The dashboard's **Load demo requests** button seeds idempotent synthetic requests for fresh, day-13, breached/manual-decision, and closed-after-approved-extension SLA scenarios. At the initial 14-day deadline, Admin can approve one 14-day extension; otherwise Admin or the related ASDM manager closes the request with a final note. The notification bell polls visible request activity every 10 seconds so other interested clusters can see status changes. These are local-only demo features; production authentication uses Microsoft Entra ID. The three supported roles are PM, ASDM, and Admin; legacy Entra role names are normalized during login.

### SQL Server setup
```bash
# 1. restore, build, and test
dotnet restore src/ReleaseTicketing.Api/ReleaseTicketing.Api.csproj
dotnet restore src/ReleaseTicketing.Functions/ReleaseTicketing.Functions.csproj
dotnet build tests/ReleaseTicketing.Tests/ReleaseTicketing.Tests.csproj -c Release
dotnet build src/ReleaseTicketing.Functions/ReleaseTicketing.Functions.csproj -c Release
dotnet test tests/ReleaseTicketing.Tests/ReleaseTicketing.Tests.csproj --filter "Category!=SqlServer"

# 3. create the database (migration is generated once, then committed)
dotnet ef migrations add InitialCreate -p src/ReleaseTicketing.Core -o Data/Migrations
dotnet ef database update -p src/ReleaseTicketing.Core

# 4. configure the SQL Server connection string and Entra values (never commit secrets)
cd src/ReleaseTicketing.Api
dotnet user-secrets set "ConnectionStrings:Default" "Server=<server>;Database=<database>;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<api-client-id>"
dotnet user-secrets set "AzureAd:Audience" "api://<api-client-id>"

# 5. run the API (migrations are applied at startup; https://localhost:xxxx/health, /openapi/v1.json)
dotnet run
```
Functions (optional locally): copy `local.settings.json.example` to `local.settings.json`, then `cd src/ReleaseTicketing.Functions && func start`.

Standalone frontend: copy `frontend/.env.example` to `frontend/.env.production` (or configure equivalent build environment variables), set the SPA/API registration values, then run `npm install`, `npm run dev`, or `npm run build`. See [docs/entra-setup.md](docs/entra-setup.md) for Entra app registration, local sign-in, and cluster assignment.

When embedding the module in an existing host app instead, copy `frontend/src/modules/release-requests` and configure `configureReleaseApi({ baseUrl, getAccessToken })` with the host's MSAL token provider, then add `{releaseRequestRoutes}` to the host `<Routes>`.

## Tests
From the repository root, run `dotnet test tests/ReleaseTicketing.Tests/ReleaseTicketing.Tests.csproj --filter "Category!=SqlServer"` to run everything except the real-SQL-Server concurrency test.
To run the SQL Server tests on PowerShell: `$env:RT_TEST_SQL="Server=...;Database=ReleaseTicketing_Tests;..."` then `dotnet test tests/ReleaseTicketing.Tests/ReleaseTicketing.Tests.csproj --filter Category=SqlServer` (it drops/recreates that database).

Frontend production bundle: from `frontend`, run `npm ci` followed by `npm run build`. Azure Pipelines runs the backend builds/tests and frontend build, but does not deploy either application.

## Docs
Start with `docs/open-questions.md` (decisions only you can make), then `docs/architecture.md`.
