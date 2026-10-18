# Database
Tables: users, roles, user_roles, employees, clusters, release_categories, release_requests, cluster_evaluations, ticket_status_history, audit_logs, ticket_attachments, notification_queue, notification_history, sla_configuration, working_calendar, plus `ticket_counters` (extra, needed for safe yearly numbering).

Production defaults to SQL Server and requires `ConnectionStrings:Default`. Development defaults to a local SQLite file and needs no server. Configure the SQL Server connection string locally with API user-secrets (run from the API project directory):

```powershell
dotnet user-secrets set "ConnectionStrings:Default" "Server=<server>;Database=<database>;Trusted_Connection=True;TrustServerCertificate=True"
```

Use the connection details that successfully connect in SSMS. For SQL authentication, enter the connection string directly in the terminal; do not put credentials in tracked settings files. Startup applies EF migrations to the configured SQL Server. SQLite uses `EnsureCreated` for isolated local testing. Existing databases consolidate legacy role rows during startup; SQL Server deployments also apply the `ConsolidateThreeRoles` migration, which preserves role memberships while mapping legacy roles.

Seeded by the first migration: PM, ASDM, and Admin roles, clusters EY1-EY5, 5 release categories, one SLA configuration. No employees or users are seeded.

## One-time setup after the first migration
```sql
-- ASDM users: link a user (after their first sign-in) to a cluster, and make them the cluster owner
UPDATE users    SET ClusterId = (SELECT Id FROM clusters WHERE Code='EY2') WHERE Email = 'manager@company.com';
UPDATE clusters SET ManagerUserId = (SELECT Id FROM users WHERE Email='manager@company.com'), NotificationEmail='ey2-dl@company.com' WHERE Code='EY2';
-- PMs in a cluster
UPDATE users SET ClusterId = (SELECT Id FROM clusters WHERE Code='EY1') WHERE Email = 'pm@company.com';
-- Escalation mailbox for SLA breaches
UPDATE sla_configuration SET EscalationEmails = 'escalation@company.com';
-- Holidays (weekends are already treated as non-working)
INSERT INTO working_calendar (Date, IsWorkingDay, HolidayName) VALUES ('2026-12-25', 0, 'Christmas');
```
Roles are NOT assigned in SQL: they come from Entra ID app roles and are synced into `user_roles` at each sign-in.

## Employee import (not yet written)
Load Excel/CSV into `employees` with a scheduled import (upsert by EmployeeId, set `LastSyncedAt`). The ticket form only ever reads this table.
