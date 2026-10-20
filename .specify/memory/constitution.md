<!--
Sync Impact Report
Version change: unversioned placeholder template -> 1.0.0
Modified principles: five placeholder principles -> Training-First Scope; Layered and
Replaceable Architecture; Security and Authorization by Default; Offline SQLite and
Data Discipline; Spec-Driven, Verifiable Delivery
Added sections: Project Constraints; Development Workflow
Removed sections: none
Follow-up TODOs: TODO(RATIFICATION_DATE), because the original adoption date is not
recorded in the repository.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Training-First Scope

ContosoDashboard MUST remain explicitly scoped as a local training application. New
functionality MUST work without external cloud services unless a feature specification
explicitly changes that constraint. Documentation MUST identify mock authentication,
simplified security, and other non-production behavior. Rationale: the repository
teaches Spec-Driven Development and security concepts without making production claims.

### II. Layered and Replaceable Architecture

New functionality MUST preserve separation among Models, Data, Services, and Pages. UI
components MUST call application services rather than query ApplicationDbContext
directly. Business rules and authorization MUST live in services. Infrastructure
dependencies MUST use dependency injection abstractions when provider replacement or
cloud migration is part of the feature. Rationale: clear boundaries keep training
examples understandable and make infrastructure changes localized.

### III. Security and Authorization by Default

Protected pages MUST enforce authorization. Every new service operation that reads or
mutates user- or project-scoped data MUST check the requesting user's role, ownership,
assignment, or membership before accessing that data. Inputs, file paths, cookies,
headers, and external content MUST be validated according to their risk. Mock login
MUST remain clearly marked as training-only and MUST NOT be described as production
identity. Rationale: authorization belongs at the resource boundary, not only in the
user interface.

### IV. Offline SQLite and Data Discipline

The local runtime MUST use SQLite through Entity Framework Core and the configured
DefaultConnection. Entity changes MUST be represented in ApplicationDbContext with
explicit relationships, constraints, and indexes where justified. Application code
added or changed MUST use asynchronous database access in request paths. Provider-
specific SQL MUST be avoided or documented with its migration path. Rationale: a
portable local database keeps training available offline while preserving disciplined
data modeling.

### V. Spec-Driven, Verifiable Delivery

Every feature MUST have a specification with prioritized user scenarios, testable
requirements, edge cases, and measurable success criteria before implementation. A
plan and dependency-ordered task list MUST precede implementation. Changes MUST map
back to those tasks and update affected documentation. Validation MUST include a
successful build; security and data behavior MUST have automated tests when a test
project exists, otherwise a documented manual validation scenario. Rationale: the
repository exists to demonstrate a traceable path from intent to working behavior.

## Project Constraints

- The primary application stack is ASP.NET Core Blazor Server, Razor Pages, Entity
	Framework Core, SQLite, dependency injection, and Bootstrap-based UI.
- Runtime configuration MUST come from configuration files or environment variables;
	secrets MUST NOT be committed to the repository.
- The SQLite database file and its journal files MUST remain untracked. Development
	data MUST be reproducible from the model configuration and seed data.
- New external integrations MUST define their offline behavior, failure handling, and
	migration or replacement path in the feature plan.
- The application MUST NOT claim production compliance, identity assurance, or audit
	completeness while it uses mock authentication and simplified training controls.

## Development Workflow

1. Describe the feature with `/speckit.specify`, resolve material ambiguity with
	 `/speckit.clarify`, and create the technical design with `/speckit.plan`.
2. Generate dependency-ordered work with `/speckit.tasks` before changing application
	 code. Use `/speckit.analyze` for cross-artifact consistency review when appropriate.
3. Implement in task order and validate each meaningful slice with a focused test,
	 build, or documented manual scenario. Use `/speckit.converge` after implementation
	 when the code and task artifacts need reconciliation.
4. Reviews MUST check authorization boundaries, data access, SQLite startup behavior,
	 documentation accuracy, and the constitution gates before accepting a change.
5. A change is complete only when the relevant tasks, documentation, and validation
	 evidence agree with the implemented behavior.

## Governance

This constitution is authoritative for project governance. Feature specifications,
plans, task lists, implementation changes, and reviews MUST comply with it. A proposed
amendment MUST describe the affected principles or sections, rationale, impact on
existing work, and any required migration. The amendment MUST update the Sync Impact
Report, version, and amendment date in this file before it is accepted.

Versioning follows semantic rules: MAJOR changes remove or redefine a governance
requirement incompatibly; MINOR changes add a principle or materially expand guidance;
PATCH changes clarify wording without changing obligations. Compliance MUST be reviewed
at planning time and again before merge or release. Any accepted exception MUST record
its rationale, scope, owner, and expiration; security and authorization gaps MUST NOT be
hidden by an undocumented exception.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE) | **Last Amended**: 2026-10-20
