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

## 24 October 2026 — accounts and owner-scoped SQLite

- Added registration with nickname/email/password confirmation and login by either nickname or email. PasswordHasher, secure production cookies, lockout, rate limiting, CSRF and claim-derived ownership protect web access.
- All 12 planner tables use owner_id FK and (owner_id,id) keys. Transactional saves and a gate over full load/command/save avoid cross-account deletion and lost concurrent updates. A desktop DB is refused explicitly.
- Added MVC boundary commands and unchanged Core services, configured timezone and persisted cookie keys. Windows-specific integration stays outside the web application.
- Validation: 36 web checks passed, including owner isolation, FK integrity, same-period keys, recurrence exclusions, history, account lockout and 25 concurrent writes. Release solution built with zero warnings/errors. Original 70 tests still pass.

## 24 October 2026 — MVC interface and desktop appearance

- Ported week (5+2), month, year modes, current day, habits, payments and current budget to server-rendered MVC views. Included original PNG/font assets, palette, mood/habit indicators, task completion colors, goals, notes and matrix.
- Added full-card pointer drag with original grab point, snap/release thresholds, recurrence scope dialogs, context menu and keyboard operations. Day section heights use measured content; only empty sections donate height.
- Notification rules stay in Core; an open browser displays due events and retains receipts per owner. Windows autostart/tray/topmost/COM are OS-specific and not included; the current day opens inside the website.
- Validation: 41 HTTP checks passed against an automatically created disposable database. All seven screens and task/habit/payment/category dialogs inspected in a hidden browser. Registration, edit, completion, repeat scope, real pointer drag, mood/habit marks and exact budget rejection exercised. Scale/drag checked at 1600x900; current day fits 390x844. No browser errors observed.
- Desktop Core source SHA256 matches the original. The original repository remains clean, and the installed planner/personal data were not opened.

## 24 October 2026 — Amvera deployment preparation

- Added a .NET 10 multi-stage Dockerfile, non-root runtime after mounted-volume permission initialization, port 8080 and Amvera persistenceMount /data. SQLite uses DELETE journal for persistent-volume compatibility; database and Data Protection keys survive rebuilds outside /app.
- Added solution, Docker exclusions, deployment/runbook and Linux CI: warning-free build, 70 domain tests, 36 web checks, 41 HTTP checks, Docker build and container health smoke test. Production secure cookies work behind TLS termination without trusting arbitrary forwarded headers.
- Documented one replica/no overlapping deployment, HTTPS/AllowedHosts, backup/recovery, timezone and differences from Windows integration. No Amvera resource has been created; deployment requires the user's hosting account/project.
- Local validation: Release solution and publish succeeded with zero warnings/errors; 147 automated checks passed. Docker is unavailable locally; container validation is delegated to the repository CI, to be recorded after completion.
