# UI and Integration Contract

**Scope**: Blazor Server pages and existing application workflows that consume document services.

## Document Workspace

The authenticated document workspace provides:

- upload control accepting multiple files with per-file progress/result state;
- shared title, description, category, tags, project, and task defaults;
- per-file metadata overrides;
- visible-document list with title, type, size, uploader, upload date, project/task context,
  and available actions;
- search across title, description, tags, uploader, and project;
- preview action for PDF/JPEG/PNG and download action for all supported types;
- edit, replace, delete, and share actions only when returned authorization flags allow them.

The UI displays each service result without treating a partial multi-file failure as a
whole-request failure. It does not display or construct private storage paths.

## Share Flow

A share form supports exactly one recipient mode:

- a named user selected from eligible users; or
- a department/team selected from existing `User.Department` values.

The form shows active and revoked shares according to actor permissions. A successful
share causes the existing notification service to create a recipient notification; a
revocation removes access on the next authorization check while retaining history.

## Project, Task, and Dashboard Integration

- Project details includes a document list filtered by project and uses the same service
  authorization as the workspace.
- Task details can list and link documents associated with that task; task association
  must belong to the same project.
- The dashboard includes recent documents and documents shared with the current user,
  each item linking to an authenticated preview or download route.
- Project membership makes a project-scoped document visible according to existing rules;
  explicit direct or department shares remain independent from membership.
- Failed notifications do not change the document permission result.

## Administrator Report

Administrators can open a document activity report with an inclusive UTC date range no
older than 12 months. The view supports grouping by action, file type, uploader, and
access pattern and displays deletion snapshots when the original document no longer
exists. Non-administrators do not receive report data or report endpoints.

## Accessibility and Safety Expectations

Upload rejection, scanner indeterminate, authorization, and storage failure states are
shown as actionable text associated with the affected file. Actions use existing
application navigation and authorization conventions. All file operations remain
server-authorized even when the UI hides a button.
