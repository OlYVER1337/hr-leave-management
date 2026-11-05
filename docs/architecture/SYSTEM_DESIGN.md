# System design

## Scope and runtime components

The application is a role-aware HR and leave management system. Its runtime is
the React web client, two ASP.NET Core services, one SQL Server instance with
two independently owned databases, and Redis as an optional HR catalog cache.
The deployment and local startup procedure are in the [root README](../../README.md).

See the [architecture diagram](diagrams/png/api-architecture.png), its
[editable Mermaid source](diagrams/mermaid/api-architecture.mmd), and the
[login](diagrams/png/seq-login.png), [leave request](diagrams/png/seq-create-leave.png),
and [approval](diagrams/png/seq-approve-leave.png) sequence diagrams.

## Responsibilities and data ownership

| Component | Owns | Responsibilities |
|---|---|---|
| Web (React + Nginx) | UI state only | Role-aware navigation and screens; proxies `/hr-api/*` and `/leave-api/*` to their respective services |
| HR Service | `HR_DB` | Authentication and refresh-token lifecycle; employees, departments, manager hierarchy, leave types, policies, holidays |
| Leave Service | `Leave_DB` | Leave requests, balances, annual allocations, approval history, calendar and approval inbox |
| SQL Server | `HR_DB`, `Leave_DB` | Durable source of truth; each service migrates and accesses only its own database |
| Redis | HR catalog cache | Best-effort cache for leave types, policies and holidays; never the authority for leave decisions |

`Leave_DB` stores HR identifiers as scalar values. There are deliberately no
cross-database foreign keys: Leave Service resolves employee, approver, leave
type, policy and holiday information through HR Service's REST API.

## Communication and request flows

1. The browser sends sign-in credentials to HR Service. HR returns a short-lived
   access JWT in the response body and sets the rotating refresh token in an
   HttpOnly cookie.
2. The browser sends the JWT as `Authorization: Bearer <access-token>` to
   protected HR and Leave endpoints.
3. On leave creation or decision, Leave Service uses the caller's bearer token
   to retrieve authoritative HR data (employee/manager and current leave
   catalog). It uses `authoritative=true` for catalog reads, which bypasses the
   HR Redis cache.
4. Leave Service validates working days, overlap, authorization and balance,
   then changes the request, yearly allocations, balance and history in its
   SQL transaction.
5. HR screens read HR-owned data from HR Service; leave screens read
   Leave-owned data from Leave Service. Managers read only their active direct
   reports through the role-protected `GET /api/v1/me/team` endpoint; the
   organization-wide employee administration endpoints remain HR-only.
   Dashboard summaries are assembled from available API data; there is no
   separate report/export service endpoint.

The web/API route prefixes are configured in [nginx.conf](../../src/web/nginx.conf).
Browser API traffic uses the web origin; host ports for the APIs are also
published for local development and direct API testing.

## Failure handling and consistency

- **Redis unavailable:** ordinary HR catalog reads fall back to SQL and log
  cache errors. A failed invalidation can leave a non-authoritative catalog
  entry stale until its TTL expires. Leave decisions bypass cache.
- **HR unavailable to Leave Service:** dependency failure is surfaced as HTTP
  503 Problem Details. Leave Service must not create/decide a request using an
  incomplete HR snapshot; the outage integration test verifies a failed create
  is not persisted.
- **Concurrent leave changes:** SQL transactions and rowversion columns protect
  balance/request updates; the Leave Service SQL integration tests exercise
  competing requests and decisions.
- **Database startup:** both services apply EF Core migrations at startup. HR
  additionally retries transient SQL initialization failures and runs optional
  environment-configured seeders.
- **Unexpected service errors:** exception middleware returns sanitized
  Problem Details and logs server-side exceptions. See the [API reference](../API_REFERENCE.md)
  for the status-code contract.

Access JWTs are validated locally by both APIs. Revoking a refresh token does
not revoke an already-issued access JWT; it remains valid until its `exp`.
The default access-token lifetime is one minute. Refresh-token revocation on
employee deactivation is covered by HR tests.

## Operational boundaries

The Compose stack is intended for local/demo deployment, not an internet-facing
production environment. Before production, provide managed secrets, TLS and
network policy, backups/restore procedures, production-grade observability, and
a migration/rollback release procedure. OpenAPI JSON is exposed only when the
services run in Development.
