# Open questions and assumptions
Each item says what the code does today, so nothing is silently invented. Most are configuration, not code.

| # | Question | What is implemented now | Where to change |
|---|---|---|---|
| 1 | SLA exception handling after the original 14-day deadline | Admin may approve one 14-day extension; otherwise Admin/related ASDM closes manually. An unresolved approved extension auto-closes at its deadline. | `ReleaseRequestService`, `SlaService` |
| 2 | Who are "Barani" and "Senthil"? | Barani = users normalized to **Admin**. Senthil = addresses in `sla_configuration.EscalationEmails` | Entra role assignment / that column |
| 3 | Employee master source | Table `employees`, filled by an import you still need (Excel/CSV/API). **No importer is written yet.** | Next step |
| 4 | Cluster distribution lists | `clusters.NotificationEmail` (empty by default) + the cluster manager's email | `clusters` table |
| 5 | Permission boundaries | See `business-rules.md` role matrix | `ReleaseRequestService`, `EvaluationService` |
| 6 | Simultaneous interest | Database lock serialises the timestamp/sequence only; each interested cluster proceeds independently | `EvaluationService` |
| 7 | Allocation workflow | Interested -> Interview Scheduled -> Waiting for Results -> Selected or Rejected; rejection is cluster-local | `EvaluationService` |
| 8 | Closure requirements | Admin can close requests under existing administrative rules; a selection closes competing allocation tickets, not the source request | `EvaluationService.UpdateAllocationStatusAsync` |
| 9 | Rehire workflow | "Reopen" creates a NEW ticket (status REOPENED, owned by Head) and queues Reopened + Competency notifications. It never rehires automatically | `ReopenAsync` |
| 10 | Attachment types/sizes | pdf, doc(x), xls(x), msg, eml, png, jpg; 10 MB | `Attachments` config section |
| 11 | Which clusters get the request | All active clusters **except the initiating cluster** | `CreateAsync` |
| 12 | Direct-release categories (resignation etc.) | Ticket goes straight to the administrative workflow and still receives the original 14-calendar-day deadline | `CreateAsync` |
| 13 | All clusters answer NO | Request stays open with all cluster declines recorded; no automatic owner handoff | `EvaluationService` |
| 14 | Can a cluster that said NO change its mind? | No (409). | - |
| 15 | Employee data edited vs master | Source becomes MANUAL if any master field differs; an audit entry lists the differences | `MasterDifferences` |
| 16 | Cancellation clock | Counted from a date the user enters ("opportunity identified"), in the business time zone, using `working_calendar` (weekends default non-working). Holidays must be loaded | `CancelAsync`, `working_calendar` |
| 17 | Who may cancel | Creator / same-cluster PM or ASDM, or Admin; while OPEN/WIP/REOPENED | `CanCancelRole` |
| 18 | Cluster managers | ASDM users are mapped to a cluster by `users.ClusterId` (admin sets it), roles come from Entra | `database.md` |
| 19 | Competency notification on normal release | Queued when the ticket **closes**, for categories flagged `RequiresCompetencyNotification` | `TicketWorkflow.CloseAsync` |
| 20 | Auto-close of an in-progress interview | Allowed (spec says auto-close if not manually closed) | `SlaService` |
