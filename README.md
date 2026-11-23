# wolverine-demo

A minimal, runnable demo of Wolverine's Saga support used as a process manager,
built for a conference talk ("Process Managers, Not Process-Aware Aggregates").
It runs the same document-ingestion story as the talk's slides -- extract text,
chunk, embed in batches, reconcile -- against a real local Postgres, with
colorized console output standing in for "feedback to the UI."

## Setup

1. You need a local Postgres server reachable from this machine (this was
   built and compiled against a local Postgres 17/18 install, confirmed
   listening on `5432`/`5433`).
2. Copy `src/WolverineDemo/appsettings.example.json` to
   `src/WolverineDemo/appsettings.json` and set `ConnectionStrings:WolverineDemo`
   to your own Postgres connection string (host, port, username, password).
   The app will create the target database itself on first run if it doesn't
   already exist -- you don't need to create it by hand, just point at a
   server and a database name.
3. From `src/WolverineDemo`, run:
   ```
   dotnet run
   ```

## Using it

You get an interactive menu:

```
1) Start ingestion (none)
2) Start ingestion (flaky)
3) Start ingestion (dead)
4) Start ingestion (stall -- triggers the stale-saga sweep after ~20s)
5) View dead letters
6) Replay dead letters
7) Exit
```

- **none** -- the happy path. Watch the colorized stage-by-stage output:
  extracting (cyan) -> chunking (yellow) -> embedding each batch (magenta) ->
  complete (green).
- **flaky** -- the fake embedding client fails the first four calls for a
  given batch, then succeeds. That's deliberately more than layer 1's own
  retry budget (`TransientAiCall`, 3 attempts), so you'll see layer 1 retry
  and give up, then Wolverine's message-level `RetryWithCooldown` on
  `GenerateEmbeddingsBatchHandler` pick the message back up and finish the
  job -- both retry layers from the talk, live.
- **dead** -- always fails. Exhausts both retry layers and the message lands
  in Wolverine's `wolverine_dead_letters` table. Use menu option 5 to see it,
  then 6 to mark it replayable (Wolverine's durability agent picks it back up
  on its next polling cycle, so it isn't instantaneous).
- **stall** -- the fake embedding client hangs forever. After ~20 seconds the
  stale-saga sweep (a `BackgroundService` ticking every ~10s, standing in for
  "Wolverine has no saga-timeout primitive") notices the run has gone quiet
  and force-fails it.

## What's real vs. simplified here

- The saga (`DocumentIngestionAttempt`), its messages, the two retry layers,
  the cache-before-call pattern, the dead-letter handling, and the Wolverine
  registration (`PersistMessagesWithPostgresql`, `DisableConventionalDiscovery`
  + `IncludeType<T>()`, `[NonTransactional]`) are all real, compiling Wolverine
  code, verified against the actual `WolverineFx`/`WolverineFx.Postgresql`/
  `WolverineFx.RuntimeCompilation` 6.65.1 API. (Core WolverineFx no longer
  ships the Roslyn runtime compiler; the RuntimeCompilation package is required
  for `TypeLoadMode.Dynamic` local `dotnet run`.)
- The stale-saga sweep uses an in-memory activity tracker
  (`SagaActivityTracker`) rather than querying Wolverine's own saga storage
  table directly -- enough to demo the pattern without depending on the exact
  internal schema of Wolverine's lightweight saga persistence.
- The "AI" calls (document extraction, embeddings) are fakes with
  `Task.Delay` and configurable failure injection -- there's no real model
  behind this, by design.
- The `Document` aggregate lives in an in-memory dictionary for this demo,
  not its own Postgres table.

## Status as of this commit

Compiles cleanly (`dotnet build`, 0 warnings/0 errors) on .NET 10. It was run
against a real local Postgres instance on this machine and confirmed to reach
the server and negotiate authentication correctly -- it failed only on the
placeholder password in `appsettings.json`, which is expected since that
value was never meant to be real. The full menu flow (all four fail modes,
dead-letter view/replay) has not yet been exercised end-to-end against a
live database; do that once you've set your own connection string.
