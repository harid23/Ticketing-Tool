# Architecture analysis

**Status note:** this analysis predates implementation and is retained as historical context. The codebase now contains the API, shared services, Azure Functions, React module, Entra authentication, and local SQLite demo workflow. Local builds/tests do not establish production readiness; deployment details still need validation in the target environment.

## Verified in this codebase
| Area | Value |
|---|---|
| Frontend | Standalone React 18, strict TypeScript, Vite, React Router 7 |
| Authentication | Standalone MSAL/Entra sign-in; host apps can provide an access-token callback |
| Backend | ASP.NET Core API and Azure Functions sharing the Core services |
| Database | SQLite in Development; SQL Server is the non-development default |
| UI styles | Self-contained `rr-*` styles |

## Still to confirm with the host team
1. **"APEX"**: confirm whether the React UI is hosted separately or must be rebuilt as Oracle APEX pages calling the API.
2. Confirm the production employee source and how data is imported into the `employees` table.
3. Confirm the host's Entra token acquisition, app roles, allowed origins, and exact deployment targets.

## Recommended approach
- Separate ASP.NET Core API (modular monolith: Core + Api + Functions). Nothing in the existing dashboards changes.
- React module is self-contained under `frontend/src/modules/release-requests`; integration points are `configureReleaseApi`, `releaseRequestRoutes`, and the host navigation link.
- Employee data enters through an import into `employees`; Excel is never read at ticket time.

## Risks
| Risk | Mitigation |
|---|---|
| Host authentication and employee source are not yet confirmed | Validate host token integration, role assignments, and production employee import before rollout |
| Production configuration not yet validated | Supply and validate target Entra, Azure SQL, CORS, storage, Graph, monitoring, and hosting settings before release |
| Business rules ambiguous | See `open-questions.md`; all are configurable |
