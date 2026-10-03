# Weir contract tests

A test suite that judges a **running** Weir server from the outside. It starts the built server as its own
process with its own data folder and port, and talks to it only over HTTP and server-sent events. It reads or
writes the SQLite file only while the server is stopped. It never loads server code: the project does not
reference any Weir assembly (no `WebApplicationFactory`), it is only built *after* the host so the binary exists.

## Running it

```bash
dotnet build apps/server/Weir.slnx                                   # builds the host first, then this project
dotnet test apps/server/tests/Weir.Contract.Tests --filter Area=activity   # one area
dotnet test apps/server/tests/Weir.Contract.Tests                          # every area
```

| What | How |
| --- | --- |
| One area | `--filter Area=<area>` (the folder name, lower case) |
| Leave these tests out of a wider run | `--filter "Category!=Contract"` |
| A published server instead of the built one | `WEIR_CONTRACT_SERVER=/path/to/Weir.dll` (or an executable) |
| Keep each server's data folder and log after the run | `WEIR_CONTRACT_KEEP_DATA=1` |
| A fixed session secret | `WEIR_SESSION_SECRET` (a default is used otherwise) |

A server that does not become ready fails the test with the end of its own log. Each server writes its output to
`<data folder>/contract-logs/server-<n>.log`.

## How the harness works

Everything is in `Harness/`. Areas live in folders beside it (`Activity/`), one folder per area.

- **`WeirServer`** starts one real server (`dotnet Weir.dll --host 127.0.0.1 --port <free port>`) with a fresh
  `WEIR_HOME`, waits for `/ready`, and stops it by killing only the process it started and that process's children.
  `StartNewAsync(environment)` makes a server; `RestartAsync` stops and starts it again on a new port (create
  clients afterwards); `StopForDatabaseAsync()` stops it, opens the SQLite file, and restarts it when disposed
  (`StopForDatabaseAsync(restart: false)` leaves it stopped, to look at the file after a shutdown).
  Every server has its own folder and port, so test classes and areas run side by side. The harness reaches a server
  on `127.0.0.1`, the address it binds, and so do the fakes. `ServerStartGate` lets only as many servers start at
  the same moment as the machine has processors (never fewer than two), so a small CI runner is not asked to start a
  dozen at once; a server that is ready gives its place back. Disposing a server waits until nothing holds its
  database files before it deletes the folder.
- **`ServerEnvironment`** is the quiet default environment: no workers, no watcher, no periodic enqueue, high
  sign-in limits, artwork off. Pass `environment` to add or override. `ServerEnvironment.OperatorDefaults` is what
  an installed Weir runs with instead (ten workers, the watcher, periodic scans, the work-file sweeps), for tests of
  the whole product.
- **`ServerFixture`** is the per-class server: `IClassFixture<ServerFixture>` gives a class one server for all its
  tests. Derive from it and override `Environment` to give a class different settings. `SeededServerFixture` is the
  same with rows seeded into the stopped database before the first test (override `Seed`).
- **`WeirClient`** is a browser-like session (its own cookie jar) with CSRF and sign-in: `EnsureAdminAsync()`,
  `LoginAsync(...)`, `GetAsync(path, query...)`, `PostWithCsrfAsync/PutWithCsrfAsync/PatchWithCsrfAsync`
  (token in the body), `DeleteWithCsrfAsync` (token in `X-CSRF-Token`), `OpenStreamAsync` for SSE. Responses are
  `WeirResponse` (`Status`, `Header(name)`, `RawHeader(name)` as the server wrote it, `Text`, `Bytes` as sent (the
  client does not decompress), `SetCookieValue(name)`, `Fields`/`Elements` as JSON). A client also has `HeadAsync`,
  `OptionsAsync`, `AttemptLoginAsync` (returns whatever the server answers) and a cookie jar to read and change
  (`Cookie`, `SetCookie`, `ClearCookies`). A client sends no
  `Origin` or `X-Requested-With` unless asked, like the Python client: `server.CreateClient(headers)` or
  `CreateAdminClientAsync(headers)` set headers for every request (a browser-like client passes both), and
  `RequestAsync`, `GetAsync(path, headers, query...)` and the `...WithCsrfAsync` methods take headers for one request.
- **`SseReader`** reads a stream block by block: `NextBlockAsync`, `NextEventAsync`, `NextEventNamedAsync`. Event data is
  any JSON (`JsonNode`). Each call fails after 10 seconds of silence unless it is given a longer `idleTimeout`.
- **`StoppedDatabase` and `SeedSql`**: plain SQL against the shared schema for rows no API creates. `SeedSql.UtcText`
  writes a timestamp the way the schema stores it. `SeededAccounts` holds the admin `alice` and the viewer `bob`
  with precomputed Argon2 hashes (the harness has no hasher); `LibraryBodies.Unchanged` is the body of a whole-library save.
- **`Poll.UntilAsync`** waits for an outcome with a deadline; never sleep for a fixed time.
- **`ServerLedger`** records every server this run started in the temp folder. The next run stops servers whose
  test run died before teardown; it never touches a server whose run is still alive, so runs side by side are safe.
- **`ContractAreaAttribute`** puts a class in an area (`[ContractArea("activity")]`) and in `Category=Contract`.

## Porting an area

1. Make `Weir.Contract.Tests/<Area>/`, namespace `Weir.Contract.Tests.<Area>`, one test class per Python module,
   `[ContractArea("<area>")]`.
2. Use `IClassFixture<ServerFixture>` where the Python module used the shared `server`; start a server of your own
   (`await using var server = await WeirServer.StartNewAsync(env)`) where it used `server_factory`.
3. Port **test for test**. Every Python test gets a C# test with the same meaning and every assertion. Nothing is
   skipped, weakened or merged. Name it from the Python name without `test_`, in sentence form
   (`test_a_filtered_range_exports_as_csv_and_json` becomes `A_filtered_range_exports_as_csv_and_json`).
4. **Black-box only.** Drive the server through HTTP. Where no API sets up the state, seed SQLite while the server
   is stopped (`await using var database = await server.StopForDatabaseAsync()`); create clients *after* the block,
   because the server comes back on a new port. Never reference a Weir assembly.
5. Keep a side-by-side list (Python test, C# test) with matching counts in the pull request, and leave the Python
   tests in place until the C# area is green in CI.
6. Share only stateless helpers. Area-specific helpers (seed rows, settings bodies) stay in the area's folder.
