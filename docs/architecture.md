# Architecture and the concepts used

```
React/TS (role-scoped navigation: Create Request, Available Resources, My Owned Tickets for PM/ASDM, and All Release Requests for Admin)
                         --HTTPS+Entra token--> ASP.NET Core API --> Azure SQL
                                                    |--> Blob Storage (attachments, via API only)
Azure Functions (timers) --> same Core services --> Azure SQL, Graph (email), App Insights
```
## Concepts, using this application
- **Entity** - a C# class that maps to a table, e.g. `ReleaseRequest` -> `release_requests`. (`Domain/Entities.cs`)
- **DbContext** - EF Core's gateway to the database: `AppDbContext` has one `DbSet` per table and defines keys, indexes, foreign keys and seed data.
- **Migration** - a generated script describing a schema change. `dotnet ef migrations add` creates it; `database update` applies it. Commit them.
- **DTO** (data transfer object) - the shape sent over HTTP, e.g. `CreateReleaseRequestDto`. We never return entities, so internal columns/relations don't leak and the API can stay stable.
- **Controller** - receives an HTTP request and returns a response. Ours only call a service and pick a status code.
- **Service** - where rules live (`ReleaseRequestService`, `EvaluationService`...). Because they hold no web code, tests and Azure Functions reuse them.
- **Dependency Injection (DI)** - classes ask for what they need in their constructor (`AppDbContext db`, `ICurrentUser user`); ASP.NET supplies them (registered in `AddReleaseTicketingCore`). This is why tests can pass a fake clock or fake user.
- **Middleware** - code that runs on every request in a pipeline: correlation id -> authentication -> load current user -> authorization -> controller. Errors are caught by `GlobalExceptionHandler`.
- **Authentication vs authorization** - authentication = who are you (Entra token validated by the API); authorization = what may you do (roles from the token + checks in services).
- **Azure Function** - a small job host. `SlaMonitor` and `NotificationWorker` are timer functions calling `SlaService` / `NotificationProcessor`.

## Key decisions
- **Modular monolith**: Core (rules) / Api (HTTP) / Functions (jobs). No microservices.
- **Concurrency**: each cluster owns a separate `ClusterEvaluation` allocation. Mutations lock the parent request in a transaction; this permits parallel progress across clusters and ensures only one selection can win before competing active allocation tickets are closed.
- **Roles**: the only supported roles are PM, ASDM, and Admin. Legacy SDM/ClusterManager claims normalize to ASDM; Head/PMO/CompetencyPmo normalize to Admin.
- **SLA**: every request records its original 14-calendar-day deadline at creation. Allocation mutations use that deadline and cannot restart or extend it.
- **Notifications** go through `notification_queue` so email failures never fail a ticket; the worker retries with back-off.
- **Timeline** uses `ticket_status_history` for status changes plus workflow events (`EventType`).
- **Routes** use numeric ids (`/api/v1/release-requests/42`); the UI shows `ticket_number`.
- **Versioning**: all routes under `/api/v1`.
