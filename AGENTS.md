# PlannerWeb development rules

- .NET 10, ASP.NET Core MVC + Razor, SQLite. Work on `master`.
- Preserve the desktop appearance and `docs/desktop-requirements.md`; business rules stay in unchanged Planer.Core. Only animations may change the visual design.
- Keep persistence in Planer.Data, HTTP/authentication/view composition in PlannerWeb.
- Every planner row must have an owner FK; derive owner solely from authenticated claims. Every SELECT/upsert/DELETE must be tenant-scoped. Never accept an owner from form/JSON/query.
- Hold WebDatabase.Gate across the complete load/command/save cycle. Deploy a single process and one replica without overlapping deployments.
- Keep passwords hashed; require authentication and antiforgery on mutations; never commit credentials, databases, session keys, test artifacts, caches or binaries.
- One reviewable feature per commit. Update this log with completion, decisions, validation and material blockers at each feature commit.
- Run rollover, habit history, payment recurrence, historical preservation and owner isolation tests; build and inspect rendered web UI. Keep QA offscreen with disposable data. Never launch or activate the installed desktop Planer for QA.
- Work in this chat; no parallel agents.

## 24 October 2026 — domain baseline

- Copied Planer.Core from desktop commit `08c53b6` byte-for-byte, retaining domain namespaces and rules. Retained the original SQLite implementation only for regression tests.
- Carried over the 70 original tests, including recurrence exclusions, rollover, historical preservation, finance rules and drag geometry, plus the source appearance requirements.
- Validation: 70 original scenarios passed. No installed planner or personal database was used.
