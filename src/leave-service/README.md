# Leave Service

Leave Service owns leave requests, balances, and request history in `Leave_DB`.
Employee identity and leave catalog data remain owned by HR Service.

## API

All endpoints require the bearer token issued by HR Service:

- `POST /api/v1/leave-requests` submits a request for the authenticated employee.
- `GET /api/v1/leave-requests/mine` lists that employee's requests.
- `GET /api/v1/leave-requests/{id}` returns one of that employee's requests.
- `POST /api/v1/leave-requests/{id}/cancel` cancels that employee's pending request.
- `GET /api/v1/leave-requests/pending-approval` lists requests assigned to the
  authenticated manager; HR can list all pending requests.
- `POST /api/v1/leave-requests/{id}/approve` and
  `POST /api/v1/leave-requests/{id}/reject` accept `{"note":"..."}` and decide
  a pending request. Managers can only decide requests assigned to them; HR
  can decide any pending request.
- `POST /api/v1/leave-requests/{id}/cancel` accepts `{"note":"..."}` and only
  the owner can cancel their own pending request.
- `GET /api/v1/leave-balances/mine?year=2026` returns the authenticated
  employee's balance for the requested year.

Example request:

```json
{
  "leaveTypeId": 1,
  "startDate": "2026-11-02",
  "endDate": "2026-11-03",
  "reason": "Personal leave"
}
```

The service counts weekdays, excludes HR-configured holidays, and supports a
request spanning calendar years by allocating the days to each year's policy.
For balance-deducting leave types, it initializes a missing employee balance
from the HR policy and then atomically creates the pending request, increments
the pending balance, and records the creation history. Duplicate dates and
insufficient balances return `409 Conflict`. Non-working-day-only ranges and
invalid inputs return `422 Unprocessable Entity`.

The API is exposed through Nginx under `/leave-api/`; for example,
`/leave-api/api/v1/leave-requests/mine`.

Decisions, cancellation, balance updates, and history are committed atomically.
The year and requested days allocated to that year are stored with each request,
so later approval/rejection releases the exact pending reservation even if the
HR holiday catalog changes.

## Tests

Run unit tests without Docker:

```powershell
dotnet test src\leave-service\LeaveService.Tests\LeaveService.Tests.csproj
```

SQL Server persistence and concurrency tests run when
`HR_TEST_SQL_CONNECTION_STRING` is set. They create and drop an isolated test
database; use only a local/test SQL Server.
