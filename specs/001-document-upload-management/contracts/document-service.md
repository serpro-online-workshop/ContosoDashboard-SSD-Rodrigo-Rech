# Document Service Contract

**Scope**: Application-layer contract for document metadata, authorization, lifecycle, sharing, and audit operations.

The service is the authorization boundary for all document operations. Blazor pages,
notification workflows, file endpoints, and reports must call the service instead of
querying `ApplicationDbContext` or resolving a storage path directly.

## Actor Context

Each operation receives the authenticated actor's existing user identity and role:

- `UserId`
- `DisplayName`
- `Department`
- `UserRole`

An unauthenticated actor is rejected before service work begins. Authorization uses the
rules in [data-model.md](../data-model.md), including owner, project membership, manager,
team lead, administrator, and active explicit-share checks.

## Operations

| Operation | Input | Result |
|---|---|---|
| `ListVisibleAsync` | actor, search text, category, project/task filter, date range, page size/cursor | Paged summaries containing only available documents visible to the actor. |
| `GetVisibleAsync` | actor, document ID | Full metadata and authorized action flags; no private path or file bytes. |
| `UploadAsync` | actor, one or more bounded upload items, shared metadata plus per-file overrides | Per-file outcome with document ID on success and a stable rejection reason on failure. |
| `UpdateMetadataAsync` | actor, document ID, title/description/category/tags, expected update timestamp | Updated summary; rejects unauthorized or invalid metadata without partial writes. |
| `ReplaceFileAsync` | actor, document ID, bounded stream, original name, content type, expected update timestamp | Updated file metadata after storage and safety checks; preserves old file on failure. |
| `DeleteAsync` | actor, document ID | Success after private bytes and metadata are removed; activity snapshot remains reportable. |
| `CreateShareAsync` | actor, document ID, exactly one target user or department | Active share and recipient notification request. |
| `RevokeShareAsync` | actor, share ID | Revoked share with `RevokedAtUtc`; historical share row remains. |
| `GetActivityReportAsync` | administrator actor, inclusive UTC date range within 12 months, grouping/filter options | Aggregated activity report and bounded row details. |

## Upload Item Contract

Each upload item contains:

- client file name for metadata only;
- declared content type for validation;
- bounded readable stream or equivalent server-side buffer;
- optional per-file title, description, category, tags, project, and task overrides.

Shared metadata supplies defaults. A per-file override wins only for that item. The
service processes items independently and returns one result per file, so one rejected
file does not make successful files appear rejected or expose an unsafe file.

## Validation and Failure Results

The service returns machine-readable result categories suitable for inline UI messages:

- `Success`
- `MissingRequiredMetadata`
- `UnsupportedFileType`
- `FileTooLarge`
- `InvalidAssociation`
- `Unauthorized`
- `NotFound`
- `UnsafeFile`
- `ScanIndeterminate`
- `StorageFailure`
- `PersistenceFailure`

A first-upload failure creates no available document. A replacement failure leaves the
existing available metadata and file unchanged. Upload and replacement never return a
storage path to the caller.

## Transaction and Audit Rules

1. Validate actor, associations, metadata, file size, and allowed type before persistence.
2. Store and scan the candidate under the private storage boundary.
3. Commit metadata, tags, share changes, and activity in the SQLite unit of work.
4. Remove candidates when scanning or persistence fails.
5. For replacement, commit the new file identity before deleting the old private file.
6. Record successful uploads, downloads, previews, replacements, deletions, shares, and
   access-review/report actions as `DocumentActivity` rows.
7. Create existing in-app notifications only after the corresponding share or project
   association is valid. Notification failure must not grant access.

## List and Search Contract

Search combines title, description, normalized tags, uploader display name, and project
name. It applies `IsAvailable = true` and authorization predicates before pagination. The
initial contract supports up to 500 documents while keeping result pages bounded. Search
returns summaries, not file streams or private storage identities.
