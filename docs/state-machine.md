# Request and allocation workflow

```
                     release request
                           |
                14-calendar-day SLA starts
                           |
      +--------------------+---------------------+
      |                    |                     |
  Cluster A interest  Cluster B interest   Cluster C interest
      |                    |                     |
      +--> interview/status tracked independently by cluster
                           |
                 one cluster selects
                           |
       winner = SELECTED; other active evaluations = CLOSED
       original release request remains a separate record
```

Each cluster evaluation is an allocation ticket associated with one release request and one cluster. Its states are:

| Current status | Allowed next status |
|---|---|
| PENDING | INTERESTED or NOT_INTERESTED |
| INTERESTED | INTERVIEW_SCHEDULED or REJECTED |
| ASSIGNED (legacy) | INTERVIEW_SCHEDULED or REJECTED |
| IN_PROGRESS (legacy) | WAITING_FOR_RESULTS or REJECTED |
| INTERVIEW_SCHEDULED | WAITING_FOR_RESULTS or REJECTED |
| WAITING_FOR_RESULTS | SELECTED or REJECTED |
| SELECTED, REJECTED, CLOSED, NOT_INTERESTED | none (terminal) |

The 14-calendar-day SLA is recorded on the release request at creation and is not changed by interest or allocation status updates. Status updates after the original deadline are rejected. Selection is serialized by locking the parent request within a transaction; one allocation becomes SELECTED and every other active allocation ticket becomes CLOSED. Rejection closes only that cluster's allocation ticket.

Request-level `OPEN`, `WIP`, `CLOSED`, and `CANCELLED` are separate from allocation-ticket statuses. Selecting a resource does not close the original request. SLA auto-close does not close an already selected request.
