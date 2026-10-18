# Business rules (as implemented)
1. Ticket numbers `RR-YYYY-000001` are produced by the database counter inside the creation transaction.
2. Creation: PM, ASDM, and Admin. Detailed feedback >= 100 chars (trimmed) enforced on frontend AND backend.
3. Release category drives behaviour (`release_categories`): circulation, interview, competency notification.
4. One parent `release_requests` row, one `cluster_evaluations` allocation ticket per eligible cluster. Decisions are never deleted.
5. Every request receives its 14-calendar-day deadline at creation. Interest and status updates do not reset it; allocation updates are rejected after the deadline. Admin may approve one 14-day extension after expiry.
6. Every interested cluster may progress its own allocation ticket in parallel: INTERESTED -> INTERVIEW_SCHEDULED -> WAITING_FOR_RESULTS -> SELECTED or REJECTED.
7. REJECTED ends only the current cluster's allocation ticket; other cluster tickets continue independently.
8. SELECTED marks the winning allocation and atomically closes all other active allocations for that request. The source release request remains a separate record and is not closed by a cluster selection.
9. SLA (config, UTC, 15-minute job): reminders on configured days (dedupe) and BREACHED at deadline (notify Admin role + escalation emails + owner). Without an approved extension, an expired request stays open for an Admin or related ASDM manager to close with a final note. An approved extension expires after 14 days and then auto-closes unresolved requests as SYSTEM with reason `SLA_EXPIRED_NO_SELECTION`. A selected request is not auto-closed by the SLA job.
10. Cancel: allowed within N working days (default 2) of the entered identification date; notifies interested clusters.
11. Reopen: new ticket REOPENED linked by `reopened_from_ticket_id`; original unchanged.
12. History and audit are append-only (EF refuses updates/deletes; also revoke UPDATE/DELETE in SQL, see security.md).

## Role matrix
| Action | PM | ASDM | Admin |
|---|---|---|---|
| Create release request | yes | yes | yes |
| View available resources | all active resources | all active resources | all active resources |
| Express interest | no | **own cluster only** | no |
| View My Owned Tickets | own requests, read-only | **own cluster's tickets** | no; use All Release Requests |
| Close release request | no | **own cluster's request, with final note** | any active request, with final note |
| Approve SLA extension | no | no | one 14-day extension after the original deadline |
| Update allocation status | no | **own cluster only** | according to existing Admin authorization |
| View audit / administrative actions | no | no | yes |

Existing Entra role values are normalized as follows: SDM, Manager, and ClusterManager -> ASDM; Head, PMO, and CompetencyPmo -> Admin. Only PM, ASDM, and Admin are supported roles after normalization.
