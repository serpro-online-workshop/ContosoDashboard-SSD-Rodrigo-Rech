# Research: Document Upload and Management

**Feature**: `001-document-upload-management`  
**Date**: 2026-10-20

## Decision 1: Preserve the Existing Web and Data Stack

**Decision**: Extend the existing ASP.NET Core Blazor Server application, its Razor Pages authentication flow, scoped services, `ApplicationDbContext`, and SQLite `DefaultConnection`. Keep the current .NET 10 target and EF Core 8 package references for this feature; do not combine document delivery with a framework or package upgrade.

**Rationale**: The application already builds and provides reusable patterns for authorization, notifications, dashboard aggregation, project membership, and SQLite initialization. A separate upgrade would enlarge the change surface and obscure the document feature's learning goals.

**Alternatives considered**:

- Upgrade all EF and authentication packages to .NET 10-compatible versions first: rejected because it is an unrelated modernization effort and would make feature failures harder to isolate.
- Add a separate document web host or API project: rejected because the feature fits the existing single web application and does not require a second deployment unit.

## Decision 2: Private Local Storage Behind an Abstraction

**Decision**: Add an `IFileStorageService` boundary with a `LocalFileStorageService` implementation. Use offline local storage outside `wwwroot`: store content beneath runtime `AppData/uploads`, using generated relative paths with user, project-or-personal scope, and a GUID-based filename. Store only the relative storage identity and metadata in SQLite. Authenticated application endpoints will stream content after service authorization.

**Rationale**: The path is not directly browsable, generated names prevent traversal and collisions, and the interface preserves the planned future cloud-provider swap without placing cloud SDK dependencies in the training project.

**Alternatives considered**:

- Store files under `wwwroot`: rejected because static-file access would bypass document authorization.
- Store file bytes in SQLite: rejected because it increases database size and complicates the local training workflow without improving the required access boundary.
- Use user-supplied filenames as storage paths: rejected because filenames are untrusted and can collide or enable path traversal.

## Decision 3: Deterministic Offline Safety Boundary

**Decision**: Add an `IFileSafetyScanner` boundary with deterministic fail-closed scanning in a local training implementation. It performs the configured training checks and rejects unsafe or indeterminate input; the upload is never made available when scanning cannot complete. Production can replace the implementation with a stronger scanner without changing the document service contract.

**Rationale**: The clarified requirement demands offline operation and fail-closed behavior, while the constitution prohibits presenting the training control as production antivirus. A deterministic implementation makes acceptance tests repeatable.

**Alternatives considered**:

- Require a locally installed antivirus engine: rejected because it would make the offline training feature environment-dependent and difficult to test consistently.
- Omit malware scanning and rely only on extension and size checks: rejected because it violates the clarified security requirement.
- Add a cloud scanning dependency: rejected because it violates the offline-first constraint.

## Decision 4: Bounded Blazor Upload Workflow

**Decision**: Use multi-file metadata overrides: the Blazor upload component will validate file size and type before opening a bounded stream, copy each selected file into a bounded in-memory buffer for the service call, and clear the browser-file reference after the copy. Multi-file submissions use shared metadata by default with per-file overrides. Each file receives an isolated result.

**Rationale**: This matches the existing Blazor Server lifecycle and the stakeholder requirement to avoid browser-file disposal and reuse problems. Bounded reads enforce the 25 MB limit before persistence and make partial success explicit.

**Alternatives considered**:

- Let the UI write files directly: rejected because storage, scanning, and authorization would escape the service boundary.
- Require separate metadata entry before every file: rejected because it conflicts with the clarified fast multi-file workflow.
- Apply one immutable metadata set to every file: rejected because it prevents accurate categorization of mixed uploads.

## Decision 5: Centralized Resource Authorization and Soft Not-Found Results

**Decision**: `DocumentService` will perform authorization before returning document metadata or file streams. Access is derived from ownership, project membership, project-manager authority, team-lead authority over team-member documents, administrator authority, or an active explicit user/department share. Download and preview endpoints will call the same service checks and return a not-found response for missing or unauthorized documents.

**Rationale**: Existing services already enforce authorization in the business layer. Returning the same not-found result for unauthorized resources avoids disclosing whether a document exists and protects direct requests from IDOR behavior.

**Alternatives considered**:

- Rely only on `[Authorize]` at the page or endpoint: rejected because authentication does not express document ownership, project membership, or share grants.
- Put authorization only in UI visibility checks: rejected because direct requests and stale pages could bypass the UI.
- Return a distinct forbidden response for every unauthorized document: rejected because it reveals resource existence and differs from the existing soft-not-found service pattern.

## Decision 6: Department-Based Team Shares and Independent Grants

**Decision**: A team share targets users whose existing `User.Department` value matches the selected department. Project membership remains a separate access relationship. Independent explicit shares remain active when project membership is removed until the share is revoked.

**Rationale**: The current model has a department field and project membership but no standalone team entity. Reusing the existing department grouping avoids an unnecessary team schema while preserving the clarified distinction between membership-derived and explicitly granted access.

**Alternatives considered**:

- Use project membership as the team definition: rejected because project access already exists separately and would not support department-wide sharing.
- Support both department teams and project teams in the first release: rejected because it adds two recipient models when the current application exposes only one organizational grouping.
- Add a standalone team entity: rejected because it is outside the initial scope and requires new membership administration.

## Decision 7: Relational Metadata and Query Indexes

**Decision**: Use SQLite relational data/indexes: store documents, tags, shares, and activity records as relational entities in the existing EF Core context. Keep document category as a constrained text value, use integer document IDs, and represent department or user share targets with explicit nullable target fields and validation. Add indexes for uploader/date, project/date, availability/date, searchable ownership/project fields, share targets, and activity timestamps.

**Rationale**: Separate rows support tag search, explicit revocation, audit reporting, and referential integrity without introducing a category table that would conflict with the clarified text-category requirement. Indexes target the 500-document list and two-second search goals.

**Alternatives considered**:

- Store tags or shares as serialized JSON: rejected because filtering, authorization, revocation, and SQLite reporting would be less reliable.
- Use an integer category enum: rejected because the specification requires named text values.
- Use GUID document keys: rejected because the specification requires integer keys consistent with existing records.

## Decision 8: Authenticated Download and Preview Endpoints

**Decision**: Add authenticated download/preview endpoints as thin application endpoints. Each endpoint resolves the document through `DocumentService`, verifies access, checks the requested preview type, and streams the private file with the stored content type and a safe download name. PDF and JPEG/PNG content can be rendered inline; other supported files use attachment download behavior.

**Rationale**: The current application has no controller or file endpoint convention, and a thin endpoint is the smallest way to serve files outside `wwwroot` while retaining service-layer authorization. The endpoint is not a second API surface for business logic.

**Alternatives considered**:

- Expose storage paths through static files: rejected because it bypasses authorization.
- Put all file streaming logic in the Razor component: rejected because direct downloads, previews, and authorization would be difficult to protect consistently.
- Add a full MVC controller layer: rejected because a thin endpoint fits the existing application without introducing a broad architectural layer.

## Decision 9: Add Focused Test Infrastructure

**Decision**: Add `tests/ContosoDashboard.Tests` targeting the application runtime. Use service-level tests for validation and authorization, SQLite-backed integration tests for relationships and query behavior, and documented manual browser scenarios for upload progress, preview, dashboard, task, and notification flows.

**Rationale**: No test project exists today, but the constitution requires automated security and data validation when test infrastructure is available. The feature's highest risks are authorization, file lifecycle compensation, and query filtering, which need executable coverage rather than only UI checks.

**Alternatives considered**:

- Rely only on manual testing: rejected because direct-resource authorization and failed upload/replacement behavior are regression-prone.
- Add a full end-to-end browser harness immediately: deferred because the repository has no existing browser-test infrastructure; the quickstart will define manual flows and the initial test project will cover service and data behavior.
- Add a generic repository or test abstraction layer: rejected because it would increase complexity without helping the feature's specific risks.

## Decision 10: Twelve-Month Activity Reporting Window

**Decision**: Record document uploads, downloads, previews, replacements, deletions, shares, and relevant access events with UTC timestamps. Provide a 12-month audit reporting window: keep records reportable for 12 months and support administrator date-range filtering within that window. Do not add indefinite retention or a background purge job to the training release.

**Rationale**: The clarified requirement defines a concrete audit window, while the existing application has no scheduler or retention service. Query-level date filtering provides the required report behavior without adding unrelated infrastructure.

**Alternatives considered**:

- Retain only 90 days: rejected because it weakens the clarified audit requirement.
- Retain indefinitely: rejected because the training scope does not claim indefinite compliance retention.
- Add configurable retention and a scheduled purge now: deferred because it requires operational configuration and scheduling not present in the current application.

## Research Outcome

All technical-context questions are resolved for planning. The design can proceed with the existing application boundaries, a private local storage adapter, a deterministic fail-closed safety adapter, centralized service authorization, relational SQLite metadata, authenticated file endpoints, a focused test project, and a 12-month report window.
