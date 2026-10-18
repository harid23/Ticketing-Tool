# API (all under /api/v1, bearer token required)
Enums are UPPER_SNAKE text (`WIP`, `YES_INTERESTED`). Errors are RFC 7807 ProblemDetails (+ `errors` for 400, `traceId`).

| Method | Route | Notes |
|---|---|---|
| GET | /me | current user + roles |
| GET | /notifications | latest 50 visible allocation/SLA changes from the last 7 days; excludes the current user's own changes |
| POST | /local-demo/scenarios | Development only; idempotently seeds synthetic fresh, near-deadline, manual-decision, and closed-after-extension requests |
| GET | /employees/{employeeId} | 404 = enter manually |
| GET | /clusters, /release-categories | reference data |
| GET | /resources/available | PM/ASDM/Admin; filter by `search`, `grade`, `location`; paged |
| GET | /release-requests | filters: ticketNumber, employeeId, employeeName, clusterId, status, slaStatus, ownerUserId, releaseCategoryId, releaseDateFrom/To, createdFrom/To, mine; paging: page, pageSize (max 100); sortBy (ticketNumber, employeeName, status, releaseDate, slaStatus, deadline, createdAt), sortDirection |
| GET | /release-requests/{id} | includes evaluations and `allowedActions` |
| POST | /release-requests | 201 |
| PUT | /release-requests/{id} | limited editable fields; audited diff |
| POST | /release-requests/{id}/cancel | `{reason, opportunityIdentifiedOn}` |
| POST | /release-requests/{id}/close | Admin or related ASDM `{reason}`; records the final manual decision |
| POST | /release-requests/{id}/sla-exception/approve | Admin only; one 14-calendar-day extension after the original deadline |
| POST | /release-requests/{id}/reopen | Admin; 201 with NEW ticket |
| GET | /release-requests/{id}/history, /audit | audit: Admin |
| GET | /release-requests/{id}/evaluations | |
| GET | /cluster-evaluations/owned | PM: allocations on own requests; ASDM: own cluster; Admin: 403 (admins use the unique release-request list) |
| GET | /cluster-evaluations/pending | ASDM only; pending resources for assigned cluster |
| POST | .../evaluations/{eid}/respond | ASDM only; `{response}`; 409 if already answered |
| PUT | .../evaluations/{eid}/status | ASDM own cluster or Admin; `{status, rejectionReason?}` |
| POST | .../evaluations/{eid}/start, /complete | legacy status adapters |
| GET/POST | /release-requests/{id}/attachments | multipart `kind`, `file` |
| GET | .../attachments/{aid}/download | streamed via API |
| GET | /dashboard/summary | server-side counts |

At the initial 14-day SLA deadline, requests remain open for an Admin/ASDM manual close decision unless Admin approves the one-time extension. An approved extension gives the ASDM manager 14 more days for results; an unresolved request closes automatically at the extended deadline.

Status codes: 200, 201, 400 (validation), 401, 403, 404, 409 (conflict), 422 (business rule), 500.
