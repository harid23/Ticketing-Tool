# Testing
`dotnet test --filter "Category!=SqlServer"` - xUnit with in-memory SQLite and a fake clock (`FakeTimeProvider`).

Covered: valid/invalid create, feedback < 100, employee lookup source, manual and override audit, exception categories, all-role request creation, original 14-day deadline, YES/NO, parallel interest and per-cluster status, rejection isolation, competitor closure after selection, role mapping and migration, per-cluster ownership, SLA reminder/breach, manual close after expiry, one-time Admin-approved 14-day SLA extension and auto-close after the extension, attachment validation, authorization, append-only audit, state machine.

Real concurrent interest and selection tests require SQL Server (`ConcurrencyTests`, run with `RT_TEST_SQL`). HTTP-level controller tests (`WebApplicationFactory`), the Graph email sender, Azure Blob store, and frontend component tests are not covered.
Note: SQLite does not behave exactly like SQL Server (locks, filtered-index syntax), so run the whole suite once against SQL Server in the pipeline.
