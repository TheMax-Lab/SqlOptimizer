# Contributing to SqlOptimizer

Thank you for considering a contribution to SqlOptimizer. This guide explains
how to build, test and extend the project, and the conventions that keep the
codebase coherent.

## Prerequisites

- Windows 10/11 (full solution, including the desktop application)
- Visual Studio 2022 (or any editor that works with .NET solutions)
- .NET 8 SDK (builds and runs all `net8.0` projects)
- .NET Framework 4.8 Developer Pack (ships with Visual Studio 2022)
- Docker (only for the SQL Server integration tests)

The deterministic core (API, rules, offline tests) builds and runs with only
the .NET 8 SDK.

## Build

```bash
dotnet restore
dotnet build SqlOptimizer.sln
```

The solution contains 14 projects. A successful build reports 0 warnings and
0 errors.

## Tests

Offline test suite (no external dependencies):

```bash
dotnet test tests/SqlOptimizer.Domain.Tests
dotnet test tests/SqlOptimizer.Rules.Tests
dotnet test tests/SqlOptimizer.Application.Tests
dotnet test tests/SqlOptimizer.Api.Tests
dotnet test tests/SqlOptimizer.Infrastructure.Tests
```

Baseline: 651/651 tests passing.

Docker-dependent integration tests (require Docker or an external SQL Server
via the `SQLOPTIMIZER_INTEGRATION_SQLSERVER` environment variable):

```bash
dotnet test tests/SqlOptimizer.Api.IntegrationTests
dotnet test tests/SqlOptimizer.Infrastructure.IntegrationTests
```

## Coding expectations

- Keep the existing layering: `Domain` (models, AST, rules contracts) has no
  dependency on ASP.NET Core, SQL Server or LLM providers; `Rules` implements
  the deterministic rules; `Application` orchestrates the pipeline;
  `Infrastructure` hosts external integrations; `Api` is a thin HTTP surface.
- Preserve the deterministic, evidence-based semantics: a finding is a
  diagnosis, a candidate is a proposal, and only runtime validation may
  prove semantic equivalence. Do not weaken `NotExecuted`/`Inconclusive`
  handling.
- Do not retarget projects, rename projects, move source files, or change the
  Desktop/Engine stdin-stdout JSON protocol without an explicit design
  discussion.
- Do not introduce HTTP or network endpoints into the desktop application.
- Do not hard-code secrets. Connection strings and API keys come from
  configuration only and must never be logged or echoed in responses.
- Match the existing style: XML documentation on public types and members,
  nullable reference types, C# 12, deterministic behavior.
- Add or update tests with every behavioral change.

## Adding or modifying SQL rules

1. Add the rule class in `src/SqlOptimizer.Rules/Rules/` implementing
   `ISqlOptimizationRule` (extend `SqlOptimizationRuleBase` where
   appropriate). Use the next free `SQLxxx` identifier.
2. Register it in
   `src/SqlOptimizer.Rules/SqlOptimizerRulesServiceCollectionExtensions.cs`.
3. Add focused tests in `tests/SqlOptimizer.Rules.Tests/Rules/`.
4. Update the rule table in `README.md`.
5. Run the full offline suite.

Rules must be deterministic: same input, same findings, no LLM or database
required unless the rule explicitly needs schema metadata (in which case the
rule must skip cleanly when metadata is absent).

## Documentation

- Keep `README.md` accurate: endpoints, rules, configuration, and status
  sections must reflect the implementation.
- Update `CHANGELOG.md` for user-visible changes.
- Do not document features that are not implemented.

## Pull requests

- One focused change per pull request.
- Fill in the pull request template: summary, motivation, changes, testing,
  documentation, breaking changes.
- The solution must build and the offline test suite must pass.
- Changes to the engine protocol or the HTTP API contract are reviewed with
  extra attention because both surfaces are versioned.
