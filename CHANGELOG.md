# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

No changes yet.

## [1.0.0] Initial public release

Initial public release of the SqlOptimizer source tree. This entry describes
the functionality present in the repository at the time of publishing; it is
not a reconstruction of earlier development history.

### Added

- Deterministic SQL analysis engine for T-SQL (SQL Server dialect): parsing,
  AST, 20 optimization rules (`SQL001`-`SQL020`), deterministic complexity
  and performance scoring, and query statistics.
- Deterministic optimization pipeline: optimization plan, SQL candidate
  generation, candidate ranking, index recommendations, and explicit
  pipeline-level limitations.
- Semantic validation: syntax checks, structural comparison, semantic-risk
  analysis, and optional runtime result comparison against a configured
  SQL Server behind a read-only execution guard. Validation never fakes a
  successful comparison: `NotExecuted` and `Inconclusive` are first-class
  outcomes.
- REST API (ASP.NET Core, .NET 8 minimal API) with
  `POST /api/v1/analyze`, `POST /api/v1/optimize`, `POST /api/v1/validate`,
  `GET /api/v1/health`, plus `GET /` and `GET /health`; RFC 7807 Problem
  Details error model with stable error codes; optional API-key
  authentication; per-client rate limiting; request body limits;
  Swagger/OpenAPI in the Development environment.
- Windows Forms desktop application (`.NET Framework 4.8`) with a local
  `.NET 8` engine sidecar (`SqlOptimizer.Desktop.Engine`) communicating
  exclusively over stdin/stdout JSON. The desktop application uses no HTTP,
  no localhost endpoints and no ASP.NET. Engine operations: `configure`,
  `ping`, `health`, `analyze`, `optimize`, `validate`, `cancel`, `shutdown`.
- Optional LLM-assisted optimization (OpenAI-compatible providers and a mock
  provider); the deterministic pipeline remains fully functional without any
  LLM configuration.
- Optional SQL Server database integration: schema metadata, index
  information, execution plans, and runtime validation. The application runs
  fully without a database.
- Test suite: 651 offline unit tests across five test projects
  (Domain, Rules, Application, Api, Infrastructure), plus 51
  Docker-dependent SQL Server integration tests (Api and Infrastructure
  integration projects, Testcontainers-based).
- Sample request bodies for the HTTP API under `samples/`.
