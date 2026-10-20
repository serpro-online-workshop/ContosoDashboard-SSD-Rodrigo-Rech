# Quickstart: Document Upload and Management

This guide validates the feature after implementation. Run commands from the repository
root unless a command says otherwise.

## Prerequisites

Prerequisites: .NET SDK compatible with the application's `net10.0` target, a
browser with JavaScript enabled, and no external database, cloud storage, or scanner
service.

- .NET SDK compatible with the application's `net10.0` target.
- A browser with JavaScript enabled.
- No external database, cloud storage, or scanner service.
- A clean working tree is recommended for repeatable file fixtures.

The mock login uses seeded accounts with no password:

| Account | Role | Department | Useful scenario |
|---|---|---|---|
| `admin@contoso.com` | Administrator | IT | reports, all access, deletion audit |
| `camille.nicole@contoso.com` | Project Manager | Engineering | project ownership and management |
| `floris.kregel@contoso.com` | Team Lead | Engineering | team/project access |
| `ni.kang@contoso.com` | Employee | Engineering | member and personal-file behavior |

## Clean Setup

The application currently uses `EnsureCreated()` rather than EF migrations. Stop any
running instance, then remove the database created in the process working directory and
private upload files before a schema-reset run:

```bash
rm -f ContosoDashboard.db ContosoDashboard.db-shm ContosoDashboard.db-wal
rm -rf AppData/uploads
mkdir -p AppData/uploads

dotnet restore ContosoDashboard/ContosoDashboard.csproj
dotnet build ContosoDashboard/ContosoDashboard.csproj
```

The implementation test project should then be restored and run:

```bash
dotnet test tests/ContosoDashboard.Tests/ContosoDashboard.Tests.csproj
```

Expected result: the application project builds, all document service and SQLite
integration tests pass, and no test requires a network or external service.

Expected outcomes: successful checks leave only available, authorized documents;
rejected or unauthorized operations expose no private file bytes or storage paths.

Start the application from the repository root:

```bash
dotnet run --project ContosoDashboard/ContosoDashboard.csproj
```

Open `http://localhost:5000`, select a seeded user at `/login`, and verify that the
application recreates the SQLite database and seed data. The private upload root must not
be served as static content.

## Automated Validation Scenarios

The test project should include both service tests with fakes and SQLite integration tests.
Use temporary test roots and a temporary SQLite database for every test collection.

| Scenario | Expected outcome |
|---|---|
| Upload each supported type: PDF, Microsoft Word, Excel, PowerPoint, plain text, JPEG, PNG | Each safe file becomes available with generated private storage identity and metadata. |
| Upload multiple files with shared metadata and one per-file override | One result is returned per file; the override affects only its file. |
| Upload over 25 MiB or an unsupported type | File is rejected before availability; no accessible document or retained candidate exists. |
| Scanner returns `Unsafe` | File is rejected and candidate cleanup runs. |
| Scanner throws or returns `Indeterminate` | Operation fails closed; no document is available and the candidate is removed. The deterministic local scanner is for offline training validation and is not production antivirus. |
| Replace an available file with an invalid or unsafe candidate | Original metadata and bytes remain available and unchanged. |
| Replace an available file successfully | New file is available, old private file is removed after commit, and a replacement activity is recorded. |
| Search title, description, tag, uploader, and project | Only authorized available documents are returned; results are bounded and include expected metadata. |
| Share to one user and to one department | Exactly one target is stored; recipient gets the existing in-app notification; access is granted. |
| Revoke a share | Access is denied on the next authorization check while the share and activity history remain. |
| Delete a document | Private bytes and metadata are removed; deletion activity retains title and date for the administrator report. |
| Report activity for a range within 12 months | Administrator receives grouped activity data. |
| Report activity older than 12 months or as a non-administrator | Request is rejected or returns no report data according to the service contract. |
| Request an unauthorized document by ID | Service and file routes return the same not-found behavior as an unknown document. |
| Preview PDF/JPEG/PNG | Response is authenticated, inline, and has the allowlisted content type. |
| Preview DOCX/XLSX/PPTX | Response is `415 Unsupported Media Type`; no bytes are returned. |
| Download a supported file | Response is authenticated, attachment-disposition, sanitized filename, and `nosniff`. |
| Task association from a different project | Association is rejected without a partial document write. |
| Dashboard/project/task document queries | Each view shows only the actor's authorized documents and links to authenticated routes. |

## Browser Validation Scenarios

### 1. Upload and Organize

1. Log in as Ni Kang.
2. Open the document workspace and select two supported files.
3. Set shared category and tags, then override the title and category for one file.
4. Submit the upload.
5. Confirm both files have independent success results, expected metadata, uploader/date,
   size/type, and project/task context where selected.
6. Search by title, tag, and project; confirm only matching authorized rows remain.

Expected: a multi-file upload does not collapse into one document, and the per-file
metadata override does not change the other file.

### 2. Validation and Fail-Closed Safety

1. Select an unsupported executable or other disallowed type.
2. Select a file larger than 25 MiB.
3. Use the deterministic unsafe/indeterminate test fixture supported by the implementation.
4. Submit alongside one valid file.
5. Confirm the valid file succeeds while each invalid file shows its own actionable result.
6. Confirm rejected files do not appear in search and no rejected bytes remain under
   `AppData/uploads`.

Expected: rejection is explicit, per-file, and fail-closed.

### 3. Project, Team, and Explicit Share Access

This scenario validates sharing and revocation independently from project membership.

1. Log in as Camille Nicole and create or upload a project-scoped document for the seeded
   `ContosoDashboard Development` project.
2. Log in as Floris Kregel and Ni Kang; verify access follows existing project membership
   and role rules.
3. As the owner or authorized manager, share a personal document directly with one user.
4. Share another document with the `Engineering` department.
5. Log in as the recipients and confirm the notification and document access.
6. Revoke one share and retry its authenticated preview/download route.

Expected: project access and explicit shares are evaluated independently; revoking a share
removes that grant without deleting audit history.

### 4. Preview, Download, and IDOR Protection

1. Copy an authorized document ID into its preview and download links.
2. Log in as a user without access and request the same IDs directly.
3. Request an unknown ID, a deleted ID, and a preview for a non-previewable format.
4. Inspect response headers for successful routes.

Expected: unauthorized and unknown IDs are indistinguishable at the access boundary;
previewable content is inline, downloads are attachments, non-previewable previews return
415, and no response exposes a filesystem path.

### 5. Metadata, Replacement, Deletion, and Reporting

1. Edit title, description, category, and tags as the owner.
2. Replace the file with a valid supported file.
3. Attempt a replacement with an unsafe fixture and verify the prior file still opens.
4. Delete the document.
5. Log in as the administrator and open the activity report for the current UTC month.
6. Filter/group by action, file type, uploader, and access pattern.

Expected: metadata and replacement permissions follow the service contract; failed
replacement is non-destructive; deletion removes the file while its activity snapshot
remains reportable for 12 months.

### 6. Task and Dashboard Integration

1. Associate an authorized document with seeded task 1 or 2 in project 1.
2. Open project details, the task view, and the dashboard as a permitted user.
3. Confirm the document is listed in each applicable view.
4. Log in as a user without access and confirm the document is absent and its routes reject access.

Expected: the task/project consistency rule is enforced and all surfaces use service-level
authorization rather than UI-only visibility.

## Performance Check

Seed or generate 500 available documents with representative titles, descriptions, tags,
uploaders, and projects in the SQLite test database. Measure an authorized search from
request start through the rendered result page. The target is a result within two seconds
on a normal development machine, with no unauthorized rows in the response.

Record the test command, dataset size, query terms, and elapsed time in the test output or
review notes. A failure should trigger query/index review rather than weakening the
authorization predicate.
