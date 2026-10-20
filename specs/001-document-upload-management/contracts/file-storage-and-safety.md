# File Storage and Safety Contract

**Scope**: Private local file storage and deterministic safety scanning used by the document service.

## Storage Boundary

`IFileStorageService` owns all file-system access. Its root is a private application-data
folder at `AppData/uploads`, resolved from the application base path or an equivalent
configured private root. The folder is outside `wwwroot` and is never exposed as a static
file provider.

The service accepts generated storage identities, not user-provided paths. A stored file
uses a generated opaque identity under the private root; `OriginalFileName` remains
metadata only. Path resolution rejects rooted paths, traversal segments, and identities
that escape the configured root.

## Storage Operations

| Operation | Contract |
|---|---|
| `StoreCandidateAsync` | Copies a bounded stream to an isolated candidate file; refuses more than 25 MiB and returns an opaque candidate identity. |
| `OpenReadAsync` | Opens a private file only after the document service has authorized the actor and resolved the stored identity. |
| `CommitCandidateAsync` | Promotes an approved candidate to its generated final identity without accepting a caller path. |
| `DeleteAsync` | Deletes a private file by generated identity and is idempotent for already-removed candidates. |
| `CleanupCandidateAsync` | Best-effort cleanup used after scan, persistence, or request failure. |

Streams are disposed by the owning operation. Copy operations are bounded and do not
load arbitrarily large files into memory. Storage errors map to `StorageFailure` and do
not make a candidate available.

## Safety Scanner

`IFileSafetyScanner` receives a candidate stream plus validated file metadata and returns
exactly one of:

- `Safe`: the candidate may be committed;
- `Unsafe`: reject it and remove the candidate;
- `Indeterminate`: fail closed, reject it, remove the candidate, and return a clear user-safe result.

The initial implementation is deterministic and local so it works offline. It validates
an allowlisted extension/content-type pair and applies deterministic content checks for
the supported training scenarios. It must never claim `Safe` after an exception, timeout,
malformed input, or unavailable scanner dependency.

## Allowed Types and Limits

The first implementation supports PDF, JPEG, PNG, DOCX, XLSX, and PPTX. The service
validates both the extension and declared/observed content type; the original name is not
trusted for execution. Maximum size is 25 MiB (26,214,400 bytes). The scanner may reject
mismatched signatures, unsafe test markers, or malformed supported files.

## Failure and Cleanup Invariants

- No scanner result other than `Safe` can create an available document.
- A rejected first upload leaves no accessible metadata and no retained candidate.
- A rejected replacement leaves the current file and metadata intact.
- A database failure after candidate storage triggers candidate cleanup.
- Private storage is not reachable through a direct URL, static-file middleware, or a
  user-controlled path segment.
- File bytes are returned only through the authenticated file-access contract.
