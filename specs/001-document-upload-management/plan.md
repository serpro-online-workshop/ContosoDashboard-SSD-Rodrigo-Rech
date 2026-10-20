# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-document-upload-management/spec.md`

## Summary

ContosoDashboard needs a private, offline-capable document workflow covering upload,
metadata, role- and project-scoped access, search, preview/download, sharing,
task/dashboard integration, notifications, and a 12-month activity report window.
The implementation will extend the existing service-and-EF architecture with document
entities, a local filesystem storage adapter, a deterministic fail-closed safety scanner,
centralized authorization, authenticated download/preview endpoints, and focused tests.

## Technical Context

**Language/Version**: C# on .NET 10.0; existing project package references remain EF Core 8.0.0 and ASP.NET authentication packages until a separate upgrade is planned.  
**Primary Dependencies**: ASP.NET Core Blazor Server, Razor Pages, Entity Framework Core SQLite, cookie authentication, existing scoped application services, and xUnit-based tests to be added.  
**Storage**: SQLite `DefaultConnection` for metadata and audit records; private local filesystem under application-managed `AppData/uploads` for file content; no `wwwroot` storage.  
**Testing**: New `ContosoDashboard.Tests` project with service/unit tests and SQLite-backed integration tests; build and documented manual browser scenarios remain required.  
**Target Platform**: Local/offline development on the repository’s supported .NET host platforms, with the existing Blazor Server browser client.  
**Project Type**: Single web application with a new test project.  
**Performance Goals**: Upload valid files up to 25 MB within 30 seconds on a typical supported network; lists up to 500 permitted documents within 2 seconds; search within 2 seconds; PDF/image preview within 3 seconds.  
**Constraints**: Offline-first training scope; supported file whitelist; 25 MB per file; generated private paths; fail-closed deterministic safety scan; service-layer authorization; numeric document IDs; text categories; permanent deletion; explicit shares independent of project membership; 12-month activity reporting.  
**Scale/Scope**: Existing seeded users and projects, up to 500 documents in a list, six prioritized user journeys, one local application instance, and no cloud or mobile release.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Constitution gate | Status | Plan evidence |
|---|---|---|
| I. Training-First Scope | PASS | Local filesystem storage and deterministic scanner work offline; documentation will label the scanner and mock identity as training-only. |
| II. Layered and Replaceable Architecture | PASS | Pages call `IDocumentService`; storage and safety checks are injected abstractions; business authorization stays in services. |
| III. Security and Authorization by Default | PASS | Service operations and authenticated download/preview endpoints perform resource checks before returning metadata or bytes; generated paths and validation prevent path traversal. |
| IV. Offline SQLite and Data Discipline | PASS | Metadata and audit data use the existing SQLite context with explicit relationships, indexes, and asynchronous request-path access. |
| V. Spec-Driven, Verifiable Delivery | PASS | Design artifacts map the six stories and 26 requirements to tests, manual scenarios, build validation, and dependency-ordered implementation work. |

No constitution violations require an exception.

## Post-Design Constitution Check

The Phase 1 data model and contracts preserve the pre-research gates. No exception is
needed.

| Constitution gate | Status | Phase 1 evidence |
|---|---|---|
| I. Training-First Scope | PASS | `file-storage-and-safety.md` defines private local storage and a deterministic fail-closed scanner; `quickstart.md` uses seeded mock accounts and offline validation. |
| II. Layered and Replaceable Architecture | PASS | `document-service.md` keeps orchestration and authorization in `IDocumentService`; storage and scanning remain injected contracts; `ui-and-integration.md` keeps pages service-driven. |
| III. Security and Authorization by Default | PASS | `authenticated-file-access.md` requires authentication, service authorization, indistinguishable not-found behavior, safe headers, and no caller-controlled paths. |
| IV. Offline SQLite and Data Discipline | PASS | `data-model.md` specifies EF relationships, constraints, indexes, asynchronous service operations, and SQLite-compatible relational search/reporting data. |
| V. Spec-Driven, Verifiable Delivery | PASS | The contracts and quickstart map upload, rejection, access, sharing, lifecycle, reporting, and integration behavior to automated and manual checks. |

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs
│   ├── DocumentShare.cs
│   ├── DocumentTag.cs
│   └── DocumentActivity.cs
├── Data/
│   └── ApplicationDbContext.cs
├── Services/
│   ├── IDocumentService.cs
│   ├── DocumentService.cs
│   ├── IFileStorageService.cs
│   ├── LocalFileStorageService.cs
│   ├── IFileSafetyScanner.cs
│   └── DeterministicLocalFileSafetyScanner.cs
├── Pages/
│   ├── Documents.razor
│   ├── ProjectDetails.razor
│   ├── Tasks.razor
│   └── Index.razor
├── Program.cs
├── appsettings.json
└── AppData/uploads/             # Runtime-created, outside wwwroot, ignored by git
tests/
└── ContosoDashboard.Tests/
    ├── Services/
    ├── Integration/
    └── ContosoDashboard.Tests.csproj
```

**Structure Decision**: Extend the existing single Blazor Server project in its current
Models/Data/Services/Pages layout. Add a focused xUnit test project at
`tests/ContosoDashboard.Tests` because no test project currently exists. Keep private
file content under runtime `AppData/uploads` and expose bytes only through authenticated
application endpoints; do not add a second web host, cloud SDK, repository layer, or
standalone team entity.

## Design Boundaries

- `DocumentService` owns validation, authorization, metadata persistence, notifications,
  activity recording, and orchestration of storage and safety scanning.
- `IFileStorageService` owns generated relative paths, private file writes, reads,
  replacement, and deletion. `LocalFileStorageService` is the offline implementation.
- `IFileSafetyScanner` owns the replaceable safety boundary. The deterministic local
  implementation rejects configured unsafe test markers and indeterminate input; it is
  explicitly not production antivirus protection.
- `DocumentService` returns no document metadata for missing or unauthorized resources.
  Download and preview endpoints call the service before opening a file stream.
- Multi-file uploads use shared metadata defaults with per-file overrides. A failed file
  is isolated from successful files, and a failed replacement leaves the prior file.
- Explicit user or department shares are independent grants. Project membership is a
  separate access source and does not revoke a direct share when membership changes.
- Categories remain named text values from the six specified options. Document IDs are
  integer keys. Tags are separate rows so search and duplicate handling are explicit.
- Activity records remain reportable for 12 months and administrator reports accept a
  date range within that window. No indefinite-retention or background purge feature is
  added to this training release.

## Implementation Phases

1. **Data and infrastructure**: Add entities, relationships, indexes, category constants,
   storage configuration, private local storage, deterministic scanner, and test fixtures.
2. **Service and security core**: Implement upload/replacement/deletion lifecycle,
   authorization predicates, list/search/filter operations, share grants, activity
   recording, and notifications behind `IDocumentService`.
3. **Access surface and integration**: Add the authorized documents UI, download/preview
   endpoints, project/task attachment flows, dashboard recent/count data, sharing UI,
   and administrator reporting.
4. **Verification and documentation**: Add authorization, lifecycle, search, scanner,
   share, audit, and integration tests; run build/test/manual quickstart checks; update
   user-facing documentation with training-only storage and scanner limitations.

## Complexity Tracking

No constitution violations or unnecessary complexity exceptions are proposed. The new
test project, storage/safety interfaces, and authenticated endpoints are required by the
specification and existing constitution gates, not general-purpose abstractions.
