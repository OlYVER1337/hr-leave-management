# Database design

## Ownership and ERDs

SQL Server hosts two databases. HR Service owns `HR_DB`; Leave Service owns
`Leave_DB`. The diagrams are maintained in Mermaid:

- [HR ERD](diagrams/png/erd-hr-db.png) ([source](diagrams/mermaid/erd-hr-db.mmd))
- [Leave ERD](diagrams/png/erd-leave-db.png) ([source](diagrams/mermaid/erd-leave-db.mmd))

Cross-service IDs in `Leave_DB` are references by convention, not SQL foreign
keys. Only relationships fully owned by the same service have database
constraints.

## HR_DB schema

| Table | Purpose and important columns |
|---|---|
| `Departments` | Organization units: identity `Id`, required `Name` (100 chars) |
| `Employees` | HR identity and account: GUID `Id`, `Code` (20), `FullName` (150), `Email` (200), `PasswordHash`, string `Role`, `HireDate`, `IsActive`, `DepartmentId`, optional `ManagerId` |
| `LeaveTypes` | Catalog: identity `Id`, `Code` (20), `Name` (100), `IsPaid`, `DeductsBalance`, `IsActive` |
| `LeavePolicies` | Annual entitlement: identity `Id`, `LeaveTypeId`, `Year`, `DaysPerYear` (`decimal(5,1)`) |
| `Holidays` | Non-working dates: identity `Id`, `HolidayDate`, `Name` (150) |
| `RefreshTokens` | Hashed refresh-token lifecycle: GUID `Id`, `EmployeeId`, `TokenHash`, creation/expiry/revocation timestamps and replacement hash |

Important keys and constraints:

- Unique indexes: `Departments.Name`, `Employees.Code`, `Employees.Email`,
  `LeaveTypes.Code`, `LeavePolicies(LeaveTypeId, Year)`,
  `Holidays.HolidayDate`, and `RefreshTokens.TokenHash`.
- `Employees.DepartmentId` references `Departments`; `Employees.ManagerId`
  self-references `Employees`. Both use `Restrict` delete behavior.
- `LeavePolicies.LeaveTypeId` references `LeaveTypes` with `Restrict`.
- `RefreshTokens.EmployeeId` references `Employees` with cascade delete.
- Supporting refresh-token index:
  `(EmployeeId, RevokedAtUtc, ExpiresAtUtc)`.

## Leave_DB schema

| Table | Purpose and important columns |
|---|---|
| `LeaveBalances` | Per employee/type/year entitlement and counters (`Entitled`, `Used`, `Pending`, `decimal(6,1)`); SQL `rowversion` |
| `LeaveRequests` | Request data: employee/approver IDs, department snapshot, leave type ID, date range, total days, reason (1,000 chars), status, timestamps; SQL `rowversion` |
| `LeaveRequestHistories` | Append-style action/status history, actor ID, optional note (1,000 chars), timestamp |
| `LeaveRequestYearAllocations` | Days assigned to each calendar year for a request spanning years |

Important keys and constraints:

- Unique index `LeaveBalances(EmployeeId, LeaveTypeId, Year)` prevents duplicate
  balances for the same employee, type and year.
- `LeaveRequests(EmployeeId, Status, StartDate, EndDate)` supports employee
  request and overlap queries.
- `LeaveRequestHistories(LeaveRequestId, CreatedAtUtc)` supports chronological
  history retrieval.
- `LeaveRequestYearAllocations` has composite primary key
  `(LeaveRequestId, Year)`.
- History and yearly allocation rows reference `LeaveRequests` and cascade on
  deletion. Employee, approver, department and leave type IDs are HR-owned
  references without cross-database foreign keys.
- Status values are persisted as strings. `LeaveRequestStatus` values are
  `Pending`, `Approved`, `Rejected`, and `Cancelled`.

## Migrations and initialization

EF Core migrations are versioned with each owning Infrastructure project:

- HR: `20261026043924_InitialHrSchema`,
  `20261102014444_AddRefreshTokens`.
- Leave: `20261027041915_InitialLeaveSchema`,
  `20261027060954_AddApprovalAndYearAllocations`.

The HR `DatabaseInitializer` applies migrations before seeding. The Leave API
applies migrations before serving requests. Each project includes a model
snapshot; add future changes as new migrations rather than editing applied
migrations. The migration sources are under each service's
`Infrastructure/Persistence/Migrations` directory.

## Seed data

Seeders are idempotent and disabled unless configured:

- `INITIAL_HR_ENABLED=true` creates the initial HR account and its department
  when the email is not present.
- `DEMO_DATA_ENABLED=true` creates configured Manager/Employee demo accounts,
  department, and the canonical Annual, Sick and Unpaid catalog entries and
  applicable policies.
- Load-test-only scale data is generated separately by
  `scripts/load-test/seed-scale-data.sql`; never run it against production data.

Use unique local/demo passwords and keep them in an untracked `.env` or secret
store. Seed settings are not a production account-management mechanism.

## Source of schema truth

ERD images are a human-readable overview. EF Core model configuration and the
migration files are authoritative for actual SQL types, indexes, constraints
and deployed schema.
