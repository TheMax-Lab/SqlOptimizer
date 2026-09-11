# API samples

Ready-to-send request bodies for the SqlOptimizer HTTP API. Start the API first:

```bash
dotnet run --project src/SqlOptimizer.Api
```

Then (from the repository root), for example:

```bash
curl -s http://localhost:63621/api/v1/analyze \
  -H "Content-Type: application/json" \
  -d @samples/analyze.request.json
```

| File | Endpoint | Shows |
|---|---|---|
| `analyze.request.json` | `POST /api/v1/analyze` | Deterministic analysis of a SELECT with known findings (`SQL001`, `SQL004`) |
| `optimize-no-llm.request.json` | `POST /api/v1/optimize` | Full offline optimization: `useLlm: false`, semantics validated per candidate |
| `optimize.request.json` | `POST /api/v1/optimize` | Default options (`useLlm: true`); with no LLM configured this degrades to deterministic-only with an explicit limitation |
| `validate.request.json` | `POST /api/v1/validate` | Static validation with `compareResults: true`; without a configured database the result is `NotExecuted` (200), never a fabricated pass |
| `validate-with-runtime.request.json` | `POST /api/v1/validate` | Runtime comparison; requires a database configured and `SqlOptimizer__EnableRuntimeValidation=true` (see README) |
| `unsafe-sql.request.json` | `POST /api/v1/analyze` | A write operation: rejected with `400 SQL_INVALID_INPUT` (demonstration of the read-only boundary) |

All samples contain only read-only, non-destructive SQL and no credentials or keys.
