# Security Policy

## Supported versions

The repository does not currently publish tagged release binaries; the
`main` branch is the supported development line.

## Reporting a vulnerability

This project does not maintain a dedicated security contact address.

**Recommended approach:** use GitHub's private vulnerability reporting. If
the "Report a vulnerability" option is available under the repository's
**Security** tab, use it so the issue is not publicly visible while it is
under review.

If private vulnerability reporting is not available for this repository,
open a normal issue labeled `security` and keep exploit details minimal
until a maintainer has had a chance to assess and fix the report.

Please **do not** open a public issue that contains working exploit details.

## Scope

In scope:

- The HTTP API (`SqlOptimizer.Api`): authentication, input handling, error
  disclosure, rate limiting.
- The desktop engine protocol (`SqlOptimizer.Desktop.Engine`): message
  handling, limits, secret handling.
- SQL safety boundaries: the read-only execution guard, rejection of
  non-`SELECT` statements, and the safety of generated/candidate SQL.
- Handling of secrets (connection strings, LLM API keys) in configuration,
  logs and responses.

Out of scope:

- Vulnerabilities in third-party components (report them to the upstream
  project).
- Misconfigurations of the hosting environment.
- Reports that require physical access or social engineering.

## Safety model

SqlOptimizer is designed so that the highest-risk operations are
configuration-gated:

- There is no arbitrary SQL execution endpoint; the analysis path is
  restricted to `SELECT` statements.
- Runtime execution (validation only) goes through a read-only guard and
  requires an explicitly enabled database configuration.
- LLM-generated SQL is treated as untrusted and is never executed simply
  because an LLM produced it.
- Secrets are configuration values only: they are not hard-coded, not
  returned by the API, and not written to normal logs (the desktop engine
  additionally redacts configured secrets from its stderr output).
- Validation reports `NotExecuted` or `Inconclusive` rather than fabricating
  a successful result when it cannot prove equivalence.
