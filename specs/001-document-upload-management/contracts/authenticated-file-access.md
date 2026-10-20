# Authenticated Download and Preview Contract

**Scope**: Browser-facing endpoints that return private document bytes.

## Routes

| Method and route | Behavior |
|---|---|
| `GET /documents/{documentId:int}/download` | Authorizes the current user, records a download activity, and streams the private file as an attachment. |
| `GET /documents/{documentId:int}/preview` | Authorizes the current user, records a preview activity, and streams only previewable PDF/JPEG/PNG content inline. |

The exact route mapping may be implemented as minimal endpoints or Razor/Blazor host
endpoints, but both routes must use the same `IDocumentService` authorization boundary
and `IFileStorageService` read operation.

## Request Rules

- Authentication is required.
- The document ID is the only caller-supplied identifier; callers never provide a file path.
- The service resolves the document, checks `IsAvailable`, and applies owner/project/team,
  administrator, and active-share rules.
- Missing and unauthorized documents return the same not-found response to avoid leaking
  document existence.
- A missing private file is treated as unavailable and does not expose a path or stack trace.

## Response Rules

Successful download responses:

- status `200`;
- stored allowlisted content type;
- `Content-Disposition: attachment`;
- sanitized original file name;
- `X-Content-Type-Options: nosniff`;
- streamed body from private storage.

Successful preview responses:

- status `200`;
- `Content-Disposition: inline`;
- content type limited to `application/pdf`, `image/jpeg`, or `image/png`;
- `X-Content-Type-Options: nosniff`;
- streamed body from private storage.

A preview request for a supported non-preview format returns `415 Unsupported Media
Type` without exposing bytes. Invalid IDs, unauthorized requests, unavailable files, and
missing files return `404` from the access boundary. Unexpected storage failures return
`500` with a generic message and are logged without file contents.

## Audit and Cache Rules

A successful stream records activity only after authorization and before or during the
response according to the endpoint implementation; failed authorization does not create a
successful download/preview activity. Responses must not be publicly cacheable because
the same URL can be used by different authenticated actors.
