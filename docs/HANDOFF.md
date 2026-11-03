# Handover checklist

This checklist distinguishes repository evidence from work that still requires
an explicit release or human sign-off. Last evidence review: **03/11/2026**.

| # | Deliverable | Status | What is included / remaining |
|---:|---|---|---|
| 1 | Source code and Git history | **Present; release action pending** | Frontend, HR and Leave services, automated tests, Docker/Compose and docs are in the repository. Git has an established commit history. The current worktree contains uncommitted changes; review, commit and push to the intended remote before declaring the source handoff complete. |
| 2 | Running application and role workflows | **API flow verified; UI sign-off pending** | Compose-based demo, login, employee request, Manager approval and balance update were verified through APIs on an isolated `hrbench` stack. Browser opened the login page, but authenticated visual acceptance was not completed. HR/Manager/Employee workflows are in [USER-GUIDE.md](USER-GUIDE.md). Dashboard data is present; report export and notifications are not implemented. |
| 3 | System design | **Documented** | [System design](architecture/SYSTEM_DESIGN.md), architecture and sequence diagrams, service ownership, communication and failure behavior. |
| 4 | Database design | **Documented** | [Database design](architecture/DATABASE_DESIGN.md), both ERDs, migrations, runtime seeding, indexes and constraints. |
| 5 | API documentation | **Documented** | [API reference](API_REFERENCE.md), Development OpenAPI JSON, request/response examples and status codes. HR APIs only restore built-in leave types or add missing policies for balance-deducting types. |
| 6 | Frontend Design System | **Documented; accessibility sign-off pending** | [Design System](design/DESIGN_SYSTEM.md), shared UI source map, layout and interaction rules. Visual authenticated workflow, responsive and accessibility checks still need human acceptance. |
| 7 | Redis design and evidence | **Documented and verified** | [Redis report](performance/redis-performance.md) records key/TTL/invalidation, fallback, outage behavior and catalog ON/OFF measurements. Redis serves HR catalog cache; Leave decisions bypass it. |
| 8 | Tests and performance report | **Automated tests pass; one load case open** | Latest results: HR 53/53, Leave 25/25, Web 31/31; build passed. Catalog k6 includes Redis ON/OFF at 50/100 VU. Workforce Redis ON at 100 VU returned 21 HTTP 500 (0.31%); cause remains unknown, and workforce Redis OFF has not been measured. See [test and acceptance report](testing/demo-and-acceptance.md). |
| 9 | Deployment package | **Present; clean-environment rehearsal pending** | Root Compose file, shared .NET Dockerfile, Web Dockerfile/Nginx config, `.env.example`, persistent volumes and fresh-start instructions are present. Rehearse from a clean clone/host using only documented prerequisites before production or external handoff. |

## Required before final sign-off

1. Review the current worktree; exclude local/generated benchmark artifacts as
   appropriate, commit the intended source and documentation, then push to the
   agreed Git remote.
2. Investigate and re-run the workforce 100 VU test until it meets the documented
   error threshold; collect the missing workforce Redis OFF comparison if it is
   part of the acceptance criteria.
3. Run the authenticated browser demo across HR, Manager and Employee roles and
   record responsive/accessibility acceptance.
4. Rehearse the documented deployment from a fresh checkout. For production,
   replace local defaults with secret management, TLS, backups and operational
   monitoring.

Do not treat open items as completed based solely on documentation. The measured
100 VU HTTP 500 result and the uncommitted worktree are explicit handoff risks.
