# Learning Notes

My step-by-step log of building this project, with the concepts behind each step.
Newest steps at the bottom.

---

## Step 1 — Solution structure

**What we did**
- Replaced the default template project with `src/TransferApi` (.NET 8, controllers).
- Planned layout:
  ```
  src/TransferApi/            (done)
  src/Contracts/              (later — shared event records)
  src/RiskWorker/             (later)
  src/NotificationWorker/     (later)
  tests/TransferApi.IntegrationTests/  (later)
  ```

**Commands**
```powershell
dotnet new webapi -n TransferApi -o src/TransferApi --framework net8.0 --use-controllers
dotnet sln money-transfer-microservices.slnx add src/TransferApi/TransferApi.csproj
```

**Notes**
- `--framework net8.0` is needed because the .NET 10 SDK is installed; without it the project would target net10.0.
- `--use-controllers` gives a `Controllers/` folder instead of minimal APIs.

---

## Step 2 — Domain model

**Files**
- `Domain/Entities/Transfer.cs`
- `Domain/Enums/TransferStatus.cs` (`Pending`, `Approved`, `Rejected`)

**Concepts**
- **Constructor enforces the rules**: an invalid `Transfer` can't be created (amount > 0, max 2 decimals, accounts differ, 3-letter currency).
- **Private setters**: nothing outside the class can change the data. State changes will be methods like `Approve()` / `Reject()`.
- **`private Transfer() { }`**: only for EF Core, which uses it when loading rows from the database.
- **Id generated in the app** (`Guid.NewGuid()`), so we know it before saving — useful for the `Location` header and events.
- **UTC everywhere**: `DateTime.UtcNow`. Npgsql maps it to `timestamptz` and rejects non-UTC values.

---

## Step 3 — Controller and request DTO

**Files**
- `Controllers/TransferController.cs` — endpoints written by me (practice)
- `Domain/Requests/CreateTransferRequest.cs`

**Concepts**
- **`[ApiController]`**: automatic validation → 400 ProblemDetails before the action runs; binds complex types from the JSON body.
- **`[Route("transfers")]`**: all actions live under `/transfers`.
- **Request DTO vs entity**: the client must not set `Id`, `Status` or timestamps, so input is a separate `CreateTransferRequest`.
  ASP.NET also can't build a `Transfer` from JSON (validating constructor + private setters).
- **Data annotations** (`[Required]`, `[StringLength]`, `[Range]`, `[RegularExpression]`) run automatically.
  They can't check `from != to` or "max 2 decimals" — the entity constructor throws `ArgumentException` for those, so catch it and return `BadRequest`.
- `CreatedAtAction(nameof(GetTransfer), new { id }, body)` → 201 with a `Location` header.
- `[HttpGet("{id:guid}")]` → non-Guid ids give 404 automatically.

---

## Step 4 — Database setup (EF Core + PostgreSQL)

**Packages** (all 8.x to match .NET 8)
- `Npgsql.EntityFrameworkCore.PostgreSQL` — EF Core provider for Postgres
- `EFCore.NamingConventions` — `CreatedAtUtc` → `created_at_utc`
- `Microsoft.EntityFrameworkCore.Design` — needed by the `dotnet ef` tool
- `dotnet-ef` as a **local tool** (`dotnet-tools.json`) → anyone cloning runs `dotnet tool restore` and gets the same version

**Files**
| File | Purpose |
|---|---|
| `Data/TransferDbContext.cs` | The app's gateway to the DB. `DbSet<Transfer> Transfers` ↔ `transfers` table |
| `Data/Configurations/TransferConfiguration.cs` | Column rules for `Transfer` |
| `Program.cs` | Registers the DbContext (`UseNpgsql` + `UseSnakeCaseNamingConvention`); runs `Migrate()` on startup in Development |
| `appsettings.Development.json` | `ConnectionStrings:TransferDb` |
| `docker-compose.yml` | Postgres 16 container |

### Why `TransferConfiguration`?
Without it EF guesses column types: `decimal` → `numeric` (no precision), `string` → unlimited `text`, enum → `integer`.
The configuration sets them exactly: `numeric(18,2)`, `char(3)`, `varchar(34)`, status as text.

- Nobody calls it directly. `ApplyConfigurationsFromAssembly(...)` in `OnModelCreating` finds every `IEntityTypeConfiguration<T>` and runs it.
- EF uses it **when generating migrations** (decides the SQL) and **at runtime** (e.g. converts `TransferStatus.Pending` ↔ `'Pending'`).
- Separate class per entity keeps `OnModelCreating` clean as entities are added.
- Kept out of the entity on purpose: the entity holds business rules, not database details.

### Docker Compose
- `docker-compose.yml` is a config file, not code. It starts several containers with one command.
- "Service" in Compose = any container (my app *or* infrastructure). The database is a Compose service, but not one of my microservices.
- `ports: "5432:5432"` = my PC's port : the container's port → the app connects to `localhost:5432`.
- Named **volume** keeps the data when the container is removed.
- Future shape: `transfer-db`, `notification-db` (separate DBs — never share databases between services), `rabbitmq` (shared message broker), later the apps themselves.

### Migrations
Two separate steps:

1. **Create** a migration — **no database needed**
   ```powershell
   dotnet ef migrations add InitialCreate --project src/TransferApi --output-dir Data/Migrations
   ```
   EF builds the model from my code, compares it with `TransferDbContextModelSnapshot.cs`, and writes the difference as C# (`Up()` / `Down()`).
   A migration is a *code diff of my model*, not a copy of the database.

2. **Apply** it — **database needed**
   ```powershell
   docker compose up -d
   dotnet ef database update --project src/TransferApi
   # or just run the app — Migrate() in Program.cs applies it in Development
   ```
   EF checks the `__EFMigrationsHistory` table and runs only migrations not yet applied.

**Everyday flow**: change model → `migrations add <Name>` → read the generated file → apply.

**Useful**
- `dotnet ef migrations script --project src/TransferApi` — see the SQL without running it
- `dotnet ef migrations remove --project src/TransferApi` — undo the last (unapplied) migration

**Generated table**
```
transfers
  id              uuid              PK (pk_transfers)
  from_account    varchar(34)
  to_account      varchar(34)
  amount          numeric(18,2)
  currency        character(3)
  status          varchar(20)
  created_at_utc  timestamptz
  updated_at_utc  timestamptz
```

### Inspecting the database
```powershell
docker exec -it transfer-db psql -U transfer -d transfer_db
```
Inside psql: `\dt` (list tables), `\d transfers` (describe table), `\q` (quit).

---

## Step 5 — Running Postgres and applying the migration

**Commands** (from the repo root, where `docker-compose.yml` is)
```powershell
docker compose up -d                                  # create + start the transfer-db container
docker ps                                             # STATUS "Up", PORTS 0.0.0.0:5432->5432
docker logs transfer-db                               # "database system is ready to accept connections"
dotnet ef migrations list --project src/TransferApi   # unapplied ones show "(Pending)"
dotnet ef database update --project src/TransferApi   # apply pending migrations
docker exec -it transfer-db psql -U transfer -d transfer_db
```

**Docker notes**
- Docker Desktop = the engine that runs containers on Windows (WSL 2). The `docker` command is only a client; if the engine is off you get "docker daemon is not running".
- After installing/updating Docker, restart VS Code so its terminals pick up the new PATH.
- `docker compose up -d`: `up` creates and starts everything in the file, `-d` = detached (runs in the background).
  Compose also creates a network, so containers can later reach each other by service name (`transfer-db`, `rabbitmq`).
- `docker compose down` removes the container but keeps the volume (data). `down -v` deletes the data too.
- `POSTGRES_*` env vars only apply the **first time** the volume is created. Changing the password later needs `down -v`.

**`database update` runs every pending migration, not just the last**
- EF reads `__EFMigrationsHistory`, compares it with `Data/Migrations/`, and runs each missing `Up()` oldest-first (order from the timestamp prefix).
- Running it again does nothing ("already up to date"), so it's safe to repeat.
- `database update <Name>` goes up or down to that migration; `database update 0` undoes all (`Down()` drops tables/columns — data is lost).
- `Database.Migrate()` in `Program.cs` does the same thing at app startup.

**`docker exec -it transfer-db psql -U transfer -d transfer_db`**
| Part | Meaning |
|---|---|
| `docker exec` | run a command inside an **already running** container |
| `-it` | interactive + terminal, so I can type into it |
| `transfer-db` | container name |
| `psql` | Postgres CLI, already inside the image — no install on Windows needed |
| `-U transfer -d transfer_db` | user and database |

- No password asked: inside the container it uses a local socket, which the image trusts. The app (through port 5432) does need the password.
- psql commands start with `\`, no `;`: `\dt`, `\d transfers`, `\l`, `\du`, `\x`, `\q`.
- SQL needs `;`. If the prompt changes to `transfer_db-#`, it's waiting for the `;`.
- Uppercase table names need quotes: `SELECT * FROM "__EFMigrationsHistory";` (Postgres lowercases unquoted names — one reason for snake_case).

**Result**: `\dt` shows `transfers` and `__EFMigrationsHistory`; `InitialCreate` is applied.

---

## Step 6 — Service and repository layers

**Files**
| File | Role |
|---|---|
| `Repositories/IRepositories/ITransferRepository.cs` + `Repositories/TransferRepository.cs` | Data access only: talks to `TransferDbContext` |
| `Services/IServices/ITransferService.cs` + `Services/TransferService.cs` | The use cases: "create a transfer", "get a transfer" |
| `Program.cs` | `AddScoped<ITransferRepository, TransferRepository>()` and `AddScoped<ITransferService, TransferService>()` |

**Flow**: Controller → `ITransferService` → `ITransferRepository` → `TransferDbContext` → Postgres

**Concepts**
- **Repository** hides EF Core. The service doesn't know about `DbContext` or SQL.
- **Service** holds the steps of a use case: build the entity → add → save → (later) publish `TransferRequested`.
  Keeps the controller thin: HTTP in, HTTP out.
- **Interfaces + dependency injection**: classes ask for `ITransferService` in their constructor; ASP.NET creates the real class.
  Makes it easy to swap in fakes for unit tests.
- **`AddScoped`** = one instance per HTTP request. Same lifetime as `DbContext`, so the repository and service share the request's `DbContext`.
- **`AddAsync` vs `SaveChangesAsync`**: `Add` only marks the entity as "Added" in EF's **change tracker**; nothing hits the database until `SaveChanges`, which runs all pending changes in one transaction.
- **`FindAsync(id)`** looks up by primary key and returns `null` if not found. It checks the change tracker first, so it may not hit the DB at all.
- **`CancellationToken`**: lets a request be cancelled (e.g. the client disconnects) so the DB query stops. In the controller, add `CancellationToken cancellationToken` as an action parameter — ASP.NET fills it automatically.

---

## Step 7 — Error handling (400 vs 500)

**The bug**: POST with `amount: 10.555` passed the DTO attributes, but the `Transfer` constructor threw `ArgumentException`.
Nobody turned it into an HTTP response → **500 Internal Server Error** for bad *input*. A 500 means "server is broken" and would trigger alerts.

**The fix** (controller, around the one call that can throw):
```csharp
try
{
    var result = await _transferService.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetTransfer), new { id = result.Id }, result);
}
catch (ArgumentException ex)
{
    ModelState.AddModelError(ex.ParamName ?? string.Empty, ex.Message);
    return ValidationProblem(ModelState);   // same 400 shape as attribute validation
}
```

**Try/catch rule of thumb: only catch where you can do something useful.**
- The service doesn't know HTTP, so it can't make a 400 → no try/catch there.
- Anti-patterns: log-and-rethrow (logged twice), swallow-and-return-null (hides the cause), `catch (Exception)` (hides real bugs).
- Good reasons to catch in a service: translate an infrastructure error into a domain error, compensate/undo, retry.
- Alternatives for later: a **global exception handler** (`IExceptionHandler`, built into .NET 8) or the **Result pattern** (return success/error instead of throwing).

**Why validation exists in both the DTO and the entity**
- DTO attributes → friendly 400s for API clients, before any code runs.
- Entity constructor → the data is always valid, whoever creates it (tests, other endpoints, future consumers).
- Don't drop the 2-decimal check: `numeric(18,2)` **silently rounds** `10.555` → `10.56` — the stored amount would differ from what the client sent.
- Money amounts are 2 decimals (smallest unit = cent). 4+ decimals are for exchange rates / intermediate calculations, not amounts that move.

**Other fixes in this step**
- Return the saved `result` in the 201 body (has `id`, `status`), not the request.
- Pass `CancellationToken` from the action into the service.
- Removed the duplicated `from == to` check from the controller (the entity owns that rule).

**Verified**
| Request | Result |
|---|---|
| valid POST | 201 + `Location` + saved transfer |
| GET by id | 200 |
| amount `10.555` | 400 `errors.amount` |
| same accounts | 400 `errors.toAccount` |
| unknown id | 404 |

---

## Step 8 — Integration tests (xUnit + Testcontainers)

**Project**: `tests/TransferApi.IntegrationTests` — separate from `src/` so test code and packages never ship with the API.
```powershell
dotnet new xunit -n TransferApi.IntegrationTests -o tests/TransferApi.IntegrationTests --framework net8.0
dotnet sln money-transfer-microservices.slnx add tests/TransferApi.IntegrationTests/TransferApi.IntegrationTests.csproj
dotnet add tests/TransferApi.IntegrationTests reference src/TransferApi/TransferApi.csproj
dotnet add tests/TransferApi.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 8.0.32
dotnet add tests/TransferApi.IntegrationTests package Testcontainers.PostgreSql --version 4.16.0
dotnet test money-transfer-microservices.slnx
```

**Packages**
- `Microsoft.AspNetCore.Mvc.Testing` → `WebApplicationFactory<Program>`: runs the whole API **in memory** (no real port) and gives an `HttpClient`.
- `Testcontainers.PostgreSql` → starts a **fresh Postgres container** from code, removes it afterwards. Tests never touch the dev database.

**Files**
| File | Purpose |
|---|---|
| `TransferApiFactory.cs` | Starts the container (`InitializeAsync`), points `ConnectionStrings:TransferDb` at it (`UseSetting`), cleans up (`DisposeAsync`) |
| `TransferControllerTests.cs` | The tests. `IClassFixture<TransferApiFactory>` = one factory (one API + one container) shared by all tests in the class |
| `Program.cs` | `public partial class Program { }` — top-level statements generate a hidden `Program` class; this makes it visible to the tests |

**Concepts**
- **Manual vs automated testing**: Swagger / `.http` file / Postman / `Invoke-WebRequest` = I check by eye. Integration tests send the same requests and `dotnet test` checks them every time.
- **`IAsyncLifetime`**: xUnit's async setup/teardown — `InitializeAsync` before the first test, `DisposeAsync` after the last.
- The factory runs in the **Development** environment, so `Migrate()` in `Program.cs` creates the schema in the test container.
- **Ryuk** (`testcontainers-ryuk-...` in `docker ps`): Testcontainers' cleanup helper. Removes test containers even if the test run crashes, then removes itself.
- Test naming: `Method_Scenario_ExpectedResult`, e.g. `GetTransfer_UnknownId_Returns404`.
- Proved the tests don't use the dev DB: stopped `transfer-db`, tests still passed.

**Tests (6, all passing)**
| Test | Checks |
|---|---|
| `GetTransfer_UnknownId_Returns404` | 404 |
| `CreateTransfer_ValidRequest_Returns201WithLocation` | 201 + `Location` header |
| `GetTransfer_AfterCreate_Returns200WithSameTransfer` | POST → GET `Location` → 200; fields match, currency uppercased, `status` = `"Pending"` (proves it was really saved in Postgres) |
| `CreateTransfer_AmountWithThreeDecimals_Returns400` | `100.555` → 400 |
| `CreateTransfer_SameAccounts_Returns400` | 400 |
| `CreateTransfer_InvalidCurrency_Returns400` | 400 |

**Lesson: `100.000m` is not "3 decimals"**
- `100.000m == 100.00m` is **true** — decimal equality compares the value, not the trailing zeros.
- So `decimal.Round(100.000m, 2) != 100.000m` is false → no exception → 201. The API was right, the test was wrong.
- The rule really means "no fractions of a cent". Use a real third decimal (`100.555m`) to test it.

**Tips**
- Use `CreateTransferRequest` in tests (not anonymous objects): renaming a property breaks the build instead of silently sending wrong JSON.
- A test's name is a promise — `...Returns201WithLocation` must also assert the `Location` header.
- Read a JSON body without a DTO: `ReadFromJsonAsync<JsonElement>()` then `body.GetProperty("status").GetString()`.

**Version gotcha**: all EF Core packages must be the same version (`Design` 8.0.32 vs Npgsql's EF 8.0.11 caused an MSB3277 conflict warning in the test project → pinned `Design` to 8.0.11).

---

## Step 9 — Unit tests for the `Transfer` entity

**Project**: `tests/TransferApi.UnitTests` (xUnit, net8.0, reference to TransferApi). No extra packages, no Docker.
```powershell
dotnet new xunit -n TransferApi.UnitTests -o tests/TransferApi.UnitTests --framework net8.0
dotnet sln money-transfer-microservices.slnx add tests/TransferApi.UnitTests/TransferApi.UnitTests.csproj
dotnet add tests/TransferApi.UnitTests reference src/TransferApi/TransferApi.csproj
```

**Tests** (`TransferTests.cs`)
| Test | Checks |
|---|---|
| `Constructor_ValidInput_CreatesPendingTransfer` | Id generated, amount kept, `"eur"` → `"EUR"`, status `Pending`, `CreatedAtUtc` is UTC |
| `Constructor_AmountWithThreeDecimals_ThrowsArgumentException` | `Assert.Throws<ArgumentException>` + `ParamName == "amount"` |

**Concepts**
- Unit test = one class, no I/O. The entity has no dependencies, so **no mocks needed** — just `new Transfer(...)`.
- **Arrange / Act / Assert**: set up → call the code → check the result.
- `Assert.Throws<T>(() => ...)` checks that code throws exactly `T` and returns the exception so you can inspect it.
- Speed: unit tests ~200 ms vs integration ~7 s (container start). That's why there should be many unit tests for logic and fewer integration tests for the wiring.
- Separate projects (`UnitTests` vs `IntegrationTests`) → can run just the fast ones: `dotnet test tests/TransferApi.UnitTests`.

---

## Step 10 — RabbitMQ container, .env secrets, git

**Design decisions**
- RabbitMQ **carries** messages; the workers do the processing. Like a post office: it delivers letters, it doesn't read them.
- Why a broker instead of HTTP calls: the API answers `201 Pending` immediately; if a worker is down, messages wait in the queue; the publisher doesn't know who listens.
- **TransferApi will update the status by consuming `TransferApproved` / `TransferRejected` — not via a PUT endpoint.**
  A PUT would break "services communicate only through RabbitMQ", couple RiskWorker to TransferApi's address,
  and let any client approve its own transfer. A consumer is "a controller for messages" and reuses the same service/repository.
- Coming with that: `Approve()` / `Reject()` on the entity (only from `Pending`), idempotent consumers (at-least-once delivery → a message can arrive twice), maybe a `RejectionReason` column.

**RabbitMQ terms**
- **Message/event**: past-tense fact, e.g. `TransferRequested`. **Publish** = send to RabbitMQ.
- **Exchange**: sorting office; decides which queues get a copy. **Queue**: mailbox for one consumer. **Consumer**: code that handles messages.
- **MassTransit** creates exchanges/queues for me, plus retries and error queues.

**RabbitMQ vs Azure Service Bus** — same job (message broker); RabbitMQ is self-hosted open source, Service Bus is managed by Azure.
| RabbitMQ | Service Bus |
|---|---|
| Queue | Queue |
| Exchange → queue per consumer | Topic → subscription per consumer |
| Bindings / routing keys | Subscription filters |
| Dead-letter exchange | Dead-letter queue (built in) |
MassTransit supports both: switching is mostly `UsingRabbitMq(...)` → `UsingAzureServiceBus(...)`.

**docker-compose: RabbitMQ**
```yaml
rabbitmq:
  image: rabbitmq:4.1-management   # pinned minor version; 3.x is no longer supported
  hostname: rabbitmq               # RabbitMQ names its data folder after the hostname — needed for the volume to work
  ports:
    - "5672:5672"     # AMQP — apps/MassTransit connect here
    - "15672:15672"   # management UI — http://localhost:15672
  volumes:
    - rabbitmq-data:/var/lib/rabbitmq
```
- `RABBITMQ_DEFAULT_USER/PASS` create the login on first start (default would be `guest`/`guest`). Used for the UI and by the apps.

**Secrets: `.env`**
- Compose automatically reads `.env` next to `docker-compose.yml`; the file uses `${VAR}`.
- `${VAR:?message}` → `docker compose` stops with a clear error if the variable is missing.
- `.env` = real values, **git-ignored**. `.env.example` = the variable names, **committed**.
- Dev-only throwaway values are OK to commit, but never real passwords, cloud keys or tokens — bots scan GitHub, and history keeps them even after deletion.
- Later: .NET **user-secrets** for connection strings, **Kubernetes Secrets** in the cluster.



---

## Step 11 — Contracts project (shared event records)

**Project type: Class Library** — no `Program.cs`, can't run by itself, builds to a DLL that other projects reference.
| Type | Template | Runs by itself? | Used for |
|---|---|---|---|
| Web API | `webapi` | yes (HTTP) | TransferApi |
| xUnit | `xunit` | via `dotnet test` | test projects |
| Class Library | `classlib` | no | Contracts |
| Worker Service | `worker` | yes (background, no HTTP) | RiskWorker, NotificationWorker |

```powershell
dotnet new classlib -n Contracts -o src/Contracts --framework net8.0
dotnet sln money-transfer-microservices.slnx add src/Contracts/Contracts.csproj
dotnet add src/TransferApi reference src/Contracts/Contracts.csproj
```

**Events** (`namespace Contracts`, one file each)
| Event | Published by | Consumed by | Fields |
|---|---|---|---|
| `TransferRequested` | TransferApi | RiskWorker | TransferId, FromAccount, ToAccount, Amount, Currency, RequestedAtUtc |
| `TransferApproved` | RiskWorker | TransferApi, NotificationWorker | same details + ApprovedAtUtc |
| `TransferRejected` | RiskWorker | TransferApi, NotificationWorker | same details + Reason + RejectedAtUtc |

**Why a shared project**
- Services share **no** code or databases — except the message shapes.
- MassTransit identifies a message by **namespace + type name** (`Contracts:TransferRequested`). Same fields in a different namespace = never delivered. One shared project guarantees a match.
- All services reference Contracts, never each other. In bigger companies it's often a NuGet package.

**Design rules**
- Records only: immutable, no logic, no packages (not even MassTransit), no reference to any service.
- Past-tense names = facts that already happened (`ApproveTransfer` would be a *command*).
- **Event-carried state transfer**: result events carry the transfer details, so NotificationWorker can write its message without reading TransferApi's DB or calling it.
- Timestamps in UTC.
- **Evolving events: only add fields, never rename/remove** — old messages may still sit in a queue, old service versions may still run.

---

## To do / open items
- [x] Delete the old `money-transfer-microservices/` template folder
- [x] Start Docker Desktop → `docker compose up -d` → apply the migration → check the table in psql
- [ ] Use `TransferDbContext` in my controller endpoints (practice)
- [x] Integration tests (xUnit + Testcontainers)
- [x] Unit tests for the `Transfer` entity rules (`tests/TransferApi.UnitTests`)
- [x] Decide: TransferApi consumes `TransferApproved` / `TransferRejected` to update the status (no PUT endpoint)
- [x] RabbitMQ in docker-compose, credentials in `.env`, `git init` + `.gitignore`
- [x] First commit + push to GitHub
- [x] `Contracts` project with the event records
- [ ] TransferApi publishes `TransferRequested` (MassTransit v8 + outbox)
- [ ] RiskWorker
- [ ] TransferApi consumers for approved/rejected
- [ ] NotificationWorker
- [ ] Publishing `TransferRequested` — look into the MassTransit **transactional outbox** (avoids the "dual write" problem: DB save succeeds but the message is lost)
