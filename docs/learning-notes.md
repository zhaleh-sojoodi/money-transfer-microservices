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

## Step 12 — Publishing `TransferRequested` with MassTransit + transactional outbox

### The outbox pattern
**Problem — "dual write"**: saving to Postgres and publishing to RabbitMQ are two systems with no shared transaction.
- Save ✅ then publish 💥 → transfer stuck in `Pending` forever.
- Publish ✅ then save 💥 → RiskWorker approves a transfer that doesn't exist.
- try/catch can't fix it: a crash between the two lines runs no catch block.

**Solution**: save the message **in the same DB transaction** as the transfer (`outbox_message` table). A background delivery service sends it to RabbitMQ later.
| What fails | Transfer saved? | Message sent? |
|---|---|---|
| nothing | ✅ | ✅ shortly after |
| DB save | ❌ | ❌ never |
| RabbitMQ down | ✅ | ⏳ delayed, sent when it's back |
| app crashes after commit | ✅ | ⏳ sent after restart |
- RabbitMQ is **not involved** at save time → the API keeps accepting transfers even if RabbitMQ is down.
- **At-least-once**, not exactly-once: the delivery service can send twice (crash after send, before marking sent) → **consumers must be idempotent**. MassTransit's **inbox** (`inbox_state`) skips already-processed message ids on the consumer side.

### Three kinds of "retry"
| | Where | Protects against | Gives up? |
|---|---|---|---|
| ① Outbox delivery | publisher (TransferApi) | broker unavailable when sending | no — waits until it can send |
| ② Redelivery | broker | consumer crashed before ack | no — unacked messages are kept |
| ③ Consumer retry | consumer (MassTransit) | temporary errors while processing | yes → `<queue>_error` |

**Azure Service Bus comparison**: SDK send retry ≠ outbox (it gives up and throws → same dual-write problem).
Redelivery = **peek-lock** + lock expiry (vs RabbitMQ ack). Poison messages: `MaxDeliveryCount` → built-in **DLQ** (RabbitMQ: DLX or MassTransit `_error` queue).
ASB has **duplicate detection** by MessageId, but consumers still need to be idempotent.

### Implementation
**Packages** — `MassTransit.RabbitMQ` + `MassTransit.EntityFrameworkCore` **8.4.1**
- ⚠️ MassTransit 8.5.x requires **EF Core 9** (NU1107 conflict with EFCore.NamingConventions 8.x). 8.4.1 = last version supporting EF Core 8.
- Both MassTransit packages must always have the same version. Never v9 (commercial licence).

**`Program.cs`**
```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<TransferDbContext>(o =>
    {
        o.UsePostgres();     // Postgres-specific locking SQL for the delivery service
        o.UseBusOutbox();    // Publish() from controllers/services goes through the outbox
    });
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));
        cfg.ConfigureEndpoints(context);
    });
});
```
**`appsettings.Development.json`**: `"RabbitMq": "amqp://transfer:transfer@localhost:5672"`

**`TransferDbContext.OnModelCreating`**: `AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()`
→ migration `AddMassTransitOutbox` creates `inbox_state`, `outbox_message`, `outbox_state`.

**`TransferService.CreateAsync`**: inject `IPublishEndpoint`; call `Publish(new TransferRequested(...))` **before** `SaveChangesAsync()` — with the outbox, Publish only adds the message to the DbContext; SaveChanges commits both.

### Verified manually
- POST → 201 → message `urn:message:Contracts:TransferRequested` arrived in RabbitMQ with the right fields.
- `outbox_message` count = 0 afterwards (delivered, then cleaned up).
- MassTransit creates an exchange named after the message type: **`Contracts:TransferRequested`** (fanout).
- No consumer yet → RabbitMQ **drops** messages sent to an exchange with no bound queue. For the check I bound a temporary queue, then deleted it.

**Gotcha**: `dotnet ef migrations add ... --no-build` then `dotnet run --no-build` → the new migration isn't compiled into the DLL → table missing → 500. Build after adding a migration.

---

## Step 13 — Integration tests get their own RabbitMQ container

**Problem**: after adding MassTransit, the tests still published to the **dev** RabbitMQ (`localhost:5672` from `appsettings.Development.json`) — test messages mixed with dev traffic, and tests failed on machines without RabbitMQ running.

**Fix**: `Testcontainers.RabbitMq` 4.16.0, same pattern as Postgres:
```csharp
private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.1").Build();

builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq.GetConnectionString()); // amqp://guest:guest@127.0.0.1:<random port>/

public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync()); // parallel start
```
- Dispose order: **API first** (`base.DisposeAsync()`), then the containers — so the API doesn't try to reach containers that are already gone.
- Using a connection string (`amqp://user:pass@host:port`) for RabbitMQ made this a one-line override.

**Verified**: stopped both dev containers (`docker stop rabbitmq transfer-db`) → all 9 tests still passed.

**Learned about messages and RabbitMQ**
- Lifecycle today: `outbox_message` (ms) → sent to exchange → **no queue bound → dropped**. Outbox empty = delivered, not "never saved".
- Exchanges only forward; **queues** store. A consumer's queue is created by MassTransit when the consumer service first starts — after that, messages wait there even while the consumer is stopped.
- Transfers created before a consumer existed stay `Pending` forever → in production, **deploy consumers before publishers**.
- The retrying (outbox delivery) is done by **TransferApi**, not RabbitMQ. RabbitMQ never reads my database. If TransferApi is down, rows simply wait.
- RabbitMQ management statistics (`message_stats.publish_in`) are **in memory, sampled, reset on restart** — a count, not who/what. Don't treat them as exact.
- psql: SQL needs `;`; double quotes = names, single quotes = values. `"SELECT ..."` is a syntax error.
- Ports: `5199` was just a port I chose with `dotnet run --urls`; normal dev ports are in `launchSettings.json` (http 5243 / https 7032). TransferApi runs on Windows, the infrastructure in Docker, connected via `localhost` + published ports.

---

## Step 14 — RiskWorker: consumer and risk rule

**Project**: `src/RiskWorker` — **Worker Service** (`dotnet new worker --framework net8.0`), references Contracts, `MassTransit.RabbitMQ` 8.4.1 (same version as TransferApi). No database (stateless), no HTTP.
```powershell
dotnet new worker -n RiskWorker -o src/RiskWorker --framework net8.0
dotnet sln money-transfer-microservices.slnx add src/RiskWorker/RiskWorker.csproj
dotnet add src/RiskWorker reference src/Contracts/Contracts.csproj
dotnet add src/RiskWorker package MassTransit.RabbitMQ --version 8.4.1
```

**Why `Worker.cs` was deleted**
- The template's `Worker` is a `BackgroundService` with a `while` loop — for jobs *I* drive (timers, polling).
- MassTransit already runs its own background service that connects to RabbitMQ, reads, acks, retries and reconnects. I only write **what to do with one message** → a **consumer** ("a controller for messages").

**Files**
| File | Role |
|---|---|
| `Services/IServices/IRiskEvaluator.cs` | Interface + `RiskDecision(IsApproved, Reason)` record with `Approve()` / `Reject(reason)` |
| `Services/RiskEvaluator.cs` | The rule: `Amount > 10,000` → reject "Amount exceeds the limit of 10,000."; otherwise approve (10,000 itself is approved). No MassTransit code → easy to unit test |
| `Consumers/TransferRequestedConsumer.cs` | `IConsumer<TransferRequested>`: read message → evaluate → `context.Publish(TransferApproved / TransferRejected)` → log |

**`context.Message`** = the same `TransferRequested` TransferService published — same **data**, new **object**.
Separate processes can't share objects; only JSON travels: serialize (TransferApi) → `outbox_message.body` → RabbitMQ → deserialize (RiskWorker).
Both sides need the exact same Contracts type (`urn:message:Contracts:TransferRequested`).

**Creating vs publishing**: `new TransferApproved(...)` only builds an object in memory. `context.Publish(...)` sends it as a message to the `Contracts:TransferApproved` exchange.

**Consuming doesn't replace the message** — two separate messages:
- `TransferRequested` (own MessageId) is **consumed**, then **acked** → RabbitMQ deletes it.
- `TransferApproved` is a **new** message (own MessageId) in a different exchange, linked by `InitiatorId` / `ConversationId`.
- Analogy: read a letter, post a reply, throw the original away.

### `_publishEndpoint.Publish` (TransferService) vs `context.Publish` (consumer)
| | TransferService | TransferRequestedConsumer |
|---|---|---|
| Called on | injected `IPublishEndpoint` | `ConsumeContext` of the incoming message |
| Triggered by | HTTP request | incoming message |
| Goes first to | `outbox_message` (same DB transaction) | RabbitMQ — held until `Consume` succeeds (in-memory outbox) |
| Protects against | dual write | duplicate publishes on retry |
| `ConversationId` | **new** | **copied** from incoming message |
| `InitiatorId` | — | incoming message's MessageId |

### Message ids — created by MassTransit, not by me
| Id | Set by | When |
|---|---|---|
| `MessageId` | MassTransit | new for every message |
| `ConversationId` | MassTransit | new outside a consumer; copied by `context.Publish` |
| `InitiatorId` | MassTransit | id of the message that caused this one |
- They live in the message **headers** (envelope), not in my records.
- `TransferId` is **my business id** (in the body) — my code uses it to find the transfer. `ConversationId` is free tracing: filter logs by it to follow one transfer across all services.
- Can be set manually (`Publish(msg, ctx => ctx.ConversationId = id)`) but rarely needed.

### When is `TransferRequested` removed? — twice
```
TransferApi
 ① SaveChanges → outbox_message row
 ② delivery service sends it to RabbitMQ
 ③ RabbitMQ confirms
 ④ TransferApi DELETES the outbox_message row            ← removal #1 (Postgres)
RabbitMQ
 ⑤ exchange routes it into queue transfer-requested
RiskWorker
 ⑥ delivered to RiskWorker ("unacked": still in queue, hidden from others)
 ⑦ Consume() → evaluate → context.Publish(TransferApproved) — HELD by in-memory outbox
 ⑧ Consume() returns without exception
 ⑨ MassTransit sends the held TransferApproved
 ⑩ MassTransit sends "ack" for TransferRequested
 ⑪ RabbitMQ DELETES it from the queue                    ← removal #2 (RabbitMQ)
```
| Situation | What happens to `TransferRequested` |
|---|---|
| `Consume` throws | no ack → retry → after last retry moved to `transfer-requested_error` |
| crash during `Consume` | no ack, connection drops → redelivered |
| crash between ⑨ and ⑩ | redelivered → processed again → **`TransferApproved` published twice** → receivers must be idempotent |
- I never write delete code: the ack is automatic when `Consume` returns normally.
- **In-memory outbox** decides the order: without it, `TransferApproved` is sent at ⑦, and every retry of a failing `Consume` sends it again.

### `Program.cs` (RiskWorker)
```csharp
builder.Services.AddScoped<IRiskEvaluator, RiskEvaluator>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();          // TransferRequestedConsumer -> queue "transfer-requested"
    x.AddConsumer<TransferRequestedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));  // then -> transfer-requested_error
        cfg.UseInMemoryOutbox(context);             // publish only after Consume succeeds
        cfg.ConfigureEndpoints(context);            // create queue + bindings per consumer
    });
});
```
- `appsettings.Development.json`: `ConnectionStrings:RabbitMq` = `amqp://transfer:transfer@localhost:5672`.
- Order matters: retry wraps the outbox → each attempt gets a fresh outbox; only the successful attempt publishes.

**RabbitMQ topology MassTransit created**
```
exchange Contracts:TransferRequested ──► exchange transfer-requested ──► queue transfer-requested ──► RiskWorker
exchange Contracts:TransferApproved   (no queue yet → dropped)
exchange Contracts:TransferRejected   (no queue yet → dropped)
```
- MassTransit binds the message-type exchange to an exchange with the queue's name, which then feeds the queue (exchange-to-exchange binding).
- The queue is **durable**: it stays after RiskWorker stops, and collects messages until RiskWorker starts again.

**Verified manually**: POST 50.00 → "Transfer … approved"; POST 20,000.00 → "Transfer … rejected: Amount exceeds the limit of 10,000."; `transfer-requested` queue: 0 messages left.

---

## Step 15 — RiskWorker unit tests + seeing messages in the RabbitMQ UI

**Project**: `tests/RiskWorker.UnitTests` (xUnit, reference to RiskWorker). **No new packages** — the test harness is part of MassTransit.

**Tests (4)**
| Test | Checks |
|---|---|
| `Evaluate_AmountAtLimit_Approves` | 10,000.00 → approved, no reason (boundary: rule is `>`) |
| `Evaluate_AmountAboveLimit_RejectsWithReason` | 10,000.01 → rejected, exact reason text |
| `Consume_AmountWithinLimit_PublishesTransferApproved` | harness: consumed, `TransferApproved` published for the same TransferId, no `TransferRejected` |
| `Consume_AmountAboveLimit_PublishesTransferRejectedWithReason` | harness: `TransferRejected` with the reason, no `TransferApproved` |

**MassTransit test harness**
```csharp
_provider = new ServiceCollection()
    .AddLogging()                                   // the consumer needs ILogger<T>
    .AddScoped<IRiskEvaluator, RiskEvaluator>()
    .AddMassTransitTestHarness(x => x.AddConsumer<TransferRequestedConsumer>())
    .BuildServiceProvider(true);
_harness = _provider.GetRequiredService<ITestHarness>();
await _harness.Start();

await _harness.Bus.Publish(message);
Assert.True(await _harness.Published.Any<TransferApproved>(x => x.Context.Message.TransferId == message.TransferId));
```
- **In-memory bus** instead of RabbitMQ: no Docker, fast. Records everything consumed and published.
- Right tool for **consumer** tests (decision + what gets published). The TransferApi integration test uses real RabbitMQ because there the point was to test the outbox → RabbitMQ chain.
- `Any<T>()` **waits** until the message appears (or a timeout); `Select<T>()` only reads what's **already** recorded → always `await Any<T>()` before `Select<T>()`, or the test can be **flaky**.
- Boundary testing: test exactly **at** the limit and **just above** it — that's where `>` vs `>=` bugs hide.

**Seeing messages in the RabbitMQ UI (vs Azure Service Bus)**
| Want to see | Service Bus | RabbitMQ UI (`localhost:15672`) |
|---|---|---|
| waiting messages | Active message count | Queues → **Ready** |
| being processed | locked | **Unacked** |
| look inside | Explorer → Peek | queue → **Get messages** with Ack mode **"Nack message requeue true"** (⚠️ "Ack … requeue false" deletes it) |
| failed messages | dead-letter queue | **`<queue>_error`** (created by MassTransit on the first failure; headers `MT-Fault-Message`, `MT-Fault-StackTrace`) |
| throughput | metrics | queue → **Message rates** (publish / deliver / ack) |
- **Successful messages are not kept anywhere** — ack (RabbitMQ) / Complete (Service Bus) deletes them. Evidence of success = ack rate, consumer logs, the result (next message / DB status), tracing (OpenTelemetry, later).
- `<queue>_skipped` = messages that arrived but no consumer in that queue handles their type.

**Who created the RabbitMQ objects**: `transfer-requested` queue + exchange + binding → MassTransit at RiskWorker **startup** (`ConfigureEndpoints`), before any message. Result exchanges → MassTransit on first publish. (Only `Contracts:TransferRequested` was once created by hand during the outbox check — identical to what MassTransit creates.) Delete the queue in the UI → restart RiskWorker → it's back.

---

## Step 16 — TransferApi updates the status

**Design**: TransferApi **consumes** `TransferApproved` / `TransferRejected` (no PUT endpoint). Consumers live in `Consumers/`, next to `Controllers/` — two entry points: HTTP in, messages in.

**Entity — state transitions**
```csharp
public void Approve()
{
    if (Status == TransferStatus.Approved) return;      // duplicate message → no-op (idempotent)
    if (Status != TransferStatus.Pending)
        throw new InvalidOperationException($"Cannot approve a transfer that is {Status}.");
    Status = TransferStatus.Approved;
    UpdatedAtUtc = DateTime.UtcNow;
}
```
`Reject(reason)` works the same way and also sets `RejectionReason`. Setters stay private: these methods are the only way to change the status.

**Service** — `ApproveAsync(Guid transferId, ...)` / `RejectAsync(Guid transferId, string reason, ...)`
- Take the **id**, not a `Transfer`: the consumer has a `TransferApproved` *message*, which is a different type from the `Transfer` *entity*.
- Not found → **throw**, don't return null:
  ```csharp
  var transfer = await _repository.GetByIdAsync(transferId, cancellationToken)
      ?? throw new InvalidOperationException($"Transfer {transferId} not found.");
  ```
  In a consumer: throw → retry → `_error` queue (visible, message kept). Return/skip → ack → message deleted → update silently lost.
  Same `null`, different meaning: controller GET → client asked for something missing → 404; consumer → our system is inconsistent → bug.
- `??` = "if the left side is null, do the right side".

**`async void` — never** (for async methods with no result use `Task`)
- The caller can't await it, and exceptions aren't seen by the caller (can crash the process).
- In a consumer, MassTransit would think `Consume` succeeded → ack → message deleted even if the work failed.
- `Task` vs `Task<Transfer>`: both fine; return data only if a caller uses it.

**Why consumers show "0 references"**: nothing in my code calls `Consume()` — MassTransit does (after `AddConsumer`). Same as controllers. CodeLens also stops counting calls that don't compile.
**Hover/"no definition" broken everywhere** → editor issue: Ctrl+Shift+P → "C#: Restart Language Server" (or "Developer: Reload Window").

**Migration `AddRejectionReason`**
- `RejectionReason` is `string?` **before** generating the migration → column is `NULL`-able (pending/approved transfers have no reason). Non-nullable → `NOT NULL` column → every existing row would need a value.
- `TransferConfiguration`: `builder.Property(t => t.RejectionReason).HasMaxLength(200);` → `character varying(200)` instead of unlimited `text`.
- Generated: `AddColumn<string>("rejection_reason", "transfers", type: "character varying(200)", nullable: true)`; `Down()` drops the column.
- Applied with `dotnet ef database update`; `\d transfers` shows `rejection_reason | character varying(200)`.

**Registering the consumers (`Program.cs`, inside the existing `AddMassTransit`)**
```csharp
x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("transfer-api", false));
x.AddConsumer<TransferApprovedConsumer>();
x.AddConsumer<TransferRejectedConsumer>();
...
cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
cfg.ConfigureEndpoints(context);
```
- `SetKebabCaseEndpointNameFormatter()` (RiskWorker) = the same formatter **without** a prefix. The long form takes a prefix.
- Queue name comes from the consumer **class name**. Two services with a `TransferApprovedConsumer` and no prefix → **one shared queue** → RabbitMQ splits the messages, each service gets half ❌. With a prefix each service has its own queue and gets **every** message (= one subscription per service on a Service Bus topic).
- Renaming a queue later: the old queue stays bound and keeps collecting copies forever → delete it in the UI.
- No `UseInMemoryOutbox` here: these consumers don't publish anything; the entity methods are idempotent.

**Verified end-to-end** (TransferApi + RiskWorker running)
| POST | right after POST | GET a few seconds later |
|---|---|---|
| 50.00 EUR | `Pending` | `Approved`, `updatedAtUtc` changed |
| 20,000.00 EUR | `Pending` | `Rejected`, reason "Amount exceeds the limit of 10,000." |

Queues: `transfer-requested`, `transfer-api-transfer-approved`, `transfer-api-transfer-rejected` — all 0 messages, 1 consumer each.
**Eventual consistency**: the POST answers `Pending` immediately; the final status appears a moment later, once the messages have gone round.

**`context.CancellationToken`** in consumers = the controller's `CancellationToken`, but cancelled when the service **shuts down**. If cancelled before `SaveChangesAsync` completes → no ack → message stays in the queue → redelivered after restart. Safe because `Reject()`/`Approve()` are idempotent.

**Tests added**
| Test | Project | Checks |
|---|---|---|
| `Approve_PendingTransfer_SetsApproved` | UnitTests | Pending → Approved, no reason |
| `Approve_AlreadyApproved_DoesNothing` | UnitTests | duplicate → same status, `UpdatedAtUtc` unchanged (idempotent) |
| `Approve_RejectedTransfer_ThrowsInvalidOperationException` | UnitTests | invalid transition blocked, status stays Rejected |
| `Reject_PendingTransfer_SetsRejectedWithReason` | UnitTests | Pending → Rejected + reason |
| `Reject_EmptyReason_ThrowsArgumentException` | UnitTests | `ParamName == "reason"`, status stays Pending |
| `TransferApproved_PendingTransfer_StatusBecomesApproved` | IntegrationTests | POST → publish `TransferApproved` → GET eventually `Approved` |
| `TransferRejected_PendingTransfer_StatusBecomesRejectedWithReason` | IntegrationTests | POST → publish `TransferRejected` → GET eventually `Rejected` + reason |

- The integration test **plays RiskWorker**: it publishes the result itself with `IBus.Publish` on the test RabbitMQ (no RiskWorker running in tests).
- **Polling**: the status changes asynchronously, so the test GETs every 200 ms until it matches, failing after 10 s with the last status in the message. (Same idea as `Channel` + timeout in the publish test: never `Task.Delay(5000)` and hope.)
- Also assert that a failed action **didn't change state** (status stays Pending/Rejected) — not just that it threw.
- Separate test class → own `TransferApiFactory` (own containers). xUnit runs classes in parallel, so it costs little time.

Total: **21 tests** (TransferApi 8 unit + 9 integration, RiskWorker 4 unit).

---

## Design discussion — NotificationWorker and email (decided: keep it simple for now)

**Why a Worker**: no HTTP API; it only listens to RabbitMQ and writes to its own database.
**What it does in this project**: builds the notification text and **stores** it (CLAUDE.md). It does **not** send email — that would be a later feature.

**Services are independent**: each has its own queue (prefix), process and database. SendGrid down, or NotificationWorker completely down, never stops TransferApi from updating the status — messages just wait in NotificationWorker's queue.

**If email (SendGrid) is added later — don't call it inside the consumer**
- Save to DB + call SendGrid in one `Consume` = **dual write** again: SendGrid down → retries → error queue → email never sent; crash after sending → redelivery → email sent twice.
- Better: consumer saves the notification as `Pending` (DB only) → a `BackgroundService` in the same worker sends Pending rows → `Sent` / retry / `Failed`. (Outbox idea applied to email.) Extra columns: `status`, `sent_at_utc`, `attempts`.
- `IEmailSender` abstraction (fake in tests, **Mailpit** container locally, SendGrid in prod). API key in **user-secrets** / Kubernetes **Secret**, never in git.
- Events only carry account numbers → a real system needs the customer's email (in the event or NotificationWorker's own data) — never by reading another service's DB.

**Alternative design to revisit with SendGrid / real money movement (e.g. Stripe)**
- TransferApi consumes `TransferApproved`/`TransferRejected`, updates the status **and publishes `TransferStatusChanged` in the same transaction** (like `CreateAsync`). NotificationWorker consumes **that** instead of RiskWorker's events.
- Benefit: the customer is only notified once the transfer record is really updated (TransferApi = source of truth); no notification if the update fails.
- ⚠️ Inside a **consumer**, the injected `IPublishEndpoint` is the consume context — `UseBusOutbox()` does **not** apply → Publish would go straight to RabbitMQ (dual write). Needs the EF outbox on consumer endpoints:
  ```csharp
  x.AddConfigureEndpointsCallback((context, name, cfg) => cfg.UseEntityFrameworkOutbox<TransferDbContext>(context));
  ```
  (also enables the **inbox** → duplicate messages skipped before the consumer runs).
- Risk *decides*; a separate payment step *executes* money movement; notify only after the final status is committed.

---

## Step 17 — NotificationWorker: project + its own database container

**Project**: `src/NotificationWorker` (Worker Service, net8.0), reference to Contracts, `MassTransit.RabbitMQ` 8.4.1. Template `Worker.cs` deleted + `AddHostedService<Worker>()` removed.

**Second Postgres container** (`docker-compose.yml`)
```yaml
notification-db:
  image: postgres:16
  container_name: notification-db
  environment:
    POSTGRES_DB: ${NOTIFICATION_DB_NAME:?...}
    POSTGRES_USER: ${NOTIFICATION_DB_USER:?...}
    POSTGRES_PASSWORD: ${NOTIFICATION_DB_PASSWORD:?...}
  ports:
    - "5433:5432"     # host 5433 → container 5432
  volumes:
    - notification-db-data:/var/lib/postgresql/data
```
- **Port mapping `host:container`**: inside, every Postgres listens on 5432. On my PC only one thing can use 5432 (`transfer-db`), so this one is published on **5433**. Connection string: `Host=localhost;Port=5433`.
- **Separate server, not just a separate database** → TransferApi physically can't reach NotificationWorker's data ("never share databases").
- New values in `.env` (`NOTIFICATION_DB_NAME/USER/PASSWORD`) and names in `.env.example`.
- `docker compose up -d` only created the new container + volume; existing containers untouched.
- `pg_isready` = quick "is Postgres accepting connections?" check.
- GUI tools: same as before but **port 5433**, database `notification_db`, user `notification`.

**Duplicates decision: A — unique index on `transfer_id` + check before insert** (database-level idempotency; works with any broker).

**Docker basics**
- `docker …` = one container by name (`docker stop rabbitmq`, `docker exec -it …`, `docker logs …`). `docker compose …` = everything in `docker-compose.yml` (run from its folder).
- `docker compose up -d` (create/start what's missing or changed), `ps`, `logs -f <svc>`, `stop`/`start`, `down` (removes containers, **keeps volumes**), ⚠️ `down -v` (**deletes data**), `config` (check file with `.env` filled in).
- **Image** = read-only template (like a class); **container** = running instance (like an object). 3 containers, 2 images: `transfer-db` and `notification-db` both from `postgres:16`, but with their own settings, ports and volumes → two independent servers.

**EF Core in a worker**
- Packages (same versions as TransferApi, because MassTransit 8.4.1 = EF Core 8): `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11, `EFCore.NamingConventions` 8.0.3, `Microsoft.EntityFrameworkCore.Design` 8.0.11. `dotnet-ef` is already a local tool for the whole repo.
- `Data/NotificationDbContext.cs`, `Data/Configurations/NotificationConfiguration.cs` — same structure as TransferApi.
- **Unique index**: `builder.HasIndex(n => n.TransferId).IsUnique();` → `ix_notifications_transfer_id UNIQUE` → the database rejects a second notification for the same transfer.
- `appsettings.Development.json`: `ConnectionStrings:NotificationDb` with **`Port=5433`**.
- `Program.cs` (worker version — no `app.Environment`):
  ```csharp
  var host = builder.Build();
  if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
  {
      using var scope = host.Services.CreateScope();
      scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.Migrate();
  }
  host.Run();
  ```
- `dotnet ef` needs the DbContext **registered** in `Program.cs` to find it — the entity alone isn't enough.
- Migration `InitialCreate` → table `notifications` (id, transfer_id, type varchar(20), recipient varchar(34), message varchar(500), created_at_utc timestamptz).

## Step 18 — NotificationWorker: duplicate handling (check + unique index)

**Inserting a row is not naturally idempotent** (unlike `Approve()`): a duplicate message would create a second notification → customer notified twice.

**Two layers**
1. **Check first** (the usual duplicate — redelivery after a crash):
   ```csharp
   var existing = await _notificationRepository.GetByTransferIdAsync(transferId, cancellationToken);
   if (existing is not null)
       return existing;
   ```
2. **Unique index as safety net** (the race — two copies processed at the same moment, both pass the check):
   ```csharp
   // NotificationRepository.TryAddAsync
   try
   {
       await _dbContext.SaveChangesAsync(cancellationToken);
       return true;
   }
   catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
   {
       _dbContext.Entry(notification).State = EntityState.Detached;
       return false;   // the other copy already inserted it
   }
   ```
```
message ─► exists? ── yes ─► done (ack)
             │ no
             ▼
          INSERT ── unique violation (race) ─► done (ack)
             │ ok
             ▼
           done (ack)
```
- Every path ends in "done": a duplicate never causes retries, error-queue entries or a second row.
- **Without using the check result**: the duplicate insert hits the unique index → exception → 3 retries → error queue → a normal duplicate looks like a failure.
- `when (...)` = **exception filter**: only the duplicate-key error (Postgres SQLSTATE `23505`) is caught; any other DB error still throws → retry/error queue still work for real problems.
- Lives in the **repository** because it's Postgres-specific — "translate an infrastructure error into a domain meaning" (a good reason for try/catch).
- `Detached`: the rejected entity stays tracked in `Added` state otherwise, and a later `SaveChanges` on the same DbContext would try to insert it again.
- `PostgresException` / `PostgresErrorCodes` → `using Npgsql;` (comes with the Npgsql EF provider).

## Step 19 — NotificationWorker: consumers, MassTransit, full end-to-end

**Consumers** (`Consumers/TransferApprovedConsumer.cs`, `TransferRejectedConsumer.cs`)
- Names `<MessageName>Consumer` — the kebab formatter strips the `Consumer` suffix for the queue name (a typo like `Cosumer` → odd queue name). Same class names as in TransferApi are fine: different namespaces + different queue prefixes.
- **Recipient = `FromAccount`** (the sender wants to know the outcome; events carry accounts, not emails).
- **Message** built from the event (event-carried state transfer — no call to TransferApi):
  - approved: `$"Your transfer of {Amount:0.00} {Currency} to {ToAccount} was approved."`
  - rejected: `"... was rejected: {Reason}"`
  - `{Amount:0.00}` = always two decimals.
- Text is built in the consumers for now (option 1); could move to the service (`NotifyApprovedAsync`) to keep consumers thinner.

**Don't catch the `Notification` constructor's `ArgumentException`**
- Data comes from our own services → invalid data = a **bug** → let it throw → error queue (kept, visible, can be resent).
- Catching and returning null → ack → message deleted → notification silently lost.
- TransferApi controller is different: data from **users** → expected → catch → 400.

**`Program.cs`**
```csharp
builder.Services.AddMassTransit(x =>
{
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("notification-worker", false));
    x.AddConsumer<TransferApprovedConsumer>();
    x.AddConsumer<TransferRejectedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")!));
        cfg.UseMessageRetry(r =>
        {
            r.Interval(3, TimeSpan.FromSeconds(5));   // temporary problems
            r.Ignore<ArgumentException>();            // permanent problems → straight to _error
        });
        cfg.ConfigureEndpoints(context);
    });
});
```
- Retry is for **temporary** problems (DB briefly down). **Permanent** ones (bad data) fail identically every time → `Ignore<T>()` skips retries.
- `appsettings.Development.json`: `ConnectionStrings:RabbitMq` (same broker as the other services).

**Verified end-to-end — all three services running**
| POST | TransferApi | notification_db |
|---|---|---|
| 50.00 EUR from NL01… | `Approved` | `TransferApproved`, recipient NL01…, "Your transfer of 50.00 EUR to NL02BANK0002 was approved." |
| 20,000.00 EUR from NL03… | `Rejected` | `TransferRejected`, recipient NL03…, "… was rejected: Amount exceeds the limit of 10,000." |

RabbitMQ queues (all 0 messages, 1 consumer each):
```
transfer-requested                      → RiskWorker
transfer-api-transfer-approved          → TransferApi
transfer-api-transfer-rejected          → TransferApi
notification-worker-transfer-approved   → NotificationWorker
notification-worker-transfer-rejected   → NotificationWorker
```
→ one `TransferApproved` reached **both** TransferApi and NotificationWorker (publish once, react many).

## Step 20 — NotificationWorker integration tests

**Project**: `tests/NotificationWorker.IntegrationTests` (xUnit, reference to NotificationWorker, `Testcontainers.PostgreSql` 4.16.0). The MassTransit harness comes with MassTransit (via the project reference).

**Setup = real Postgres + in-memory bus**
- `PostgresFixture` (`IClassFixture`): starts `postgres:16`, applies the **real migrations** (`MigrateAsync`) → the unique index exists exactly as in production. `CreateDbContext()` for checking rows.
- Each test class builds a `ServiceCollection` with the real DbContext / repository / service + `AddMassTransitTestHarness(x => x.AddConsumer<…>())`. Only RabbitMQ is replaced.
- `await _harness.InactivityTask` = wait until the bus has finished processing everything, then check the database.
- Tests share one database → each uses a **new `TransferId`**, so they don't see each other's rows.

**Tests (4)**
| Test | Checks |
|---|---|
| `TransferApproved_SavesApprovedNotification` | 1 row: type, recipient = FromAccount, exact text |
| `TransferRejected_SavesNotificationWithReason` | text includes the reason |
| `SameMessageTwice_SavesOnlyOneNotification` | publish twice → **1 row**, and **no consumer faulted** (`Consumed.Any(x => x.Exception != null)` is false) |
| `TryAddAsync_SecondInsertForSameTransfer_ReturnsFalse` | two DbContexts = two copies; first `true`, second `false`, first row kept → unique-index safety net works on real Postgres |

- The race test is run **sequentially** (first insert, then second) — deterministic, and it still hits the exact path a real race would: unique violation → caught → `false`. Real concurrency would make the test flaky.
- The duplicate test checks **behaviour** (1 row, no error), not which layer stopped it — both the check and the unique index would pass it.

Total: **25 tests** (TransferApi 8 unit + 9 integration, RiskWorker 4 unit, NotificationWorker 4 integration).

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
- [x] TransferApi publishes `TransferRequested` (MassTransit v8 + outbox)
- [x] Integration tests: own RabbitMQ container instead of the dev one
- [x] RiskWorker (consumer + rule + Program.cs, verified manually)
- [x] RiskWorker unit tests (evaluator + consumer with MassTransit test harness)
- [ ] TransferApi consumers for approved/rejected
  - [x] entity `Approve()` / `Reject()`, service methods, consumers written
  - [x] `RejectionReason` column + migration
  - [x] register consumers in `Program.cs` (queue-name prefix `transfer-api-…`, retry)
  - [x] manual end-to-end check
  - [x] entity unit tests for `Approve()` / `Reject()` + integration test for the consumers
- [x] NotificationWorker (own database `notification_db`, queues `notification-worker-…`) — consumes `TransferApproved` / `TransferRejected` (original design), verified end-to-end
- [x] NotificationWorker tests (Testcontainers Postgres + MassTransit harness; duplicate → 1 row; race → `TryAddAsync` false)
- [ ] Later (optional): SendGrid via Pending/Sent + background sender; switch NotificationWorker to TransferApi's `TransferStatusChanged` (see design discussion)
- [ ] NotificationWorker
- [ ] Publishing `TransferRequested` — look into the MassTransit **transactional outbox** (avoids the "dual write" problem: DB save succeeds but the message is lost)
