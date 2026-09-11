## Summary
<!-- One or two sentences: what does this PR do? -->

## Motivation
<!-- Why is this change needed? Link the related issue if there is one. -->

## Changes
<!-- Bullet list of the concrete changes made. -->

## Testing
- [ ] Solution builds with 0 errors (`dotnet build SqlOptimizer.sln`)
- [ ] Offline test suite runs green (651 tests)
- [ ] New/changed behavior is covered by tests
- [ ] SQL Server integration tests considered (if database-backed behavior changed)

## Documentation
- [ ] README / CHANGELOG / samples updated where user-visible behavior changed
- [ ] New rules documented in the README rules section (SQLxxx)

## Breaking changes
<!-- API contract, engine protocol or configuration changes. "None" if not applicable. -->

## Checklist
- [ ] No changes to business logic unrelated to this PR
- [ ] Desktop architecture preserved (WinForms .NET Framework 4.8 + .NET 8 engine over stdin/stdout JSON, no HTTP)
- [ ] No target framework changes or project retargeting
- [ ] No new dependencies unless strictly required
- [ ] Code follows the existing style and layering (Domain / Rules / Application / Infrastructure / Api)
- [ ] No secrets, connection strings or credentials added to the repository
