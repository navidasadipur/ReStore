# Porting AccountsApi's CQRS / clean-architecture layering into ReStore

Status: **Phases 0–8 implemented** — project split, shared kernel, filters,
entity/key renames, feature slices and API shell. React contract migration (Phase 8a) is next. Remaining React work
is deferred to the dedicated client phase after the backend contracts settle.
Sources read: `D:\Projects\AccountsApi` (branch `develop`, HEAD `389ad5e`) and
`D:\Learn\ReStore` (branch `develop`, HEAD `543df08`).

Goal: ReStore's backend adopts the same shape as AccountsApi —
`Domain / Application / Infrastructure / Api`, MediatR CQRS through base
handlers, a generic repository pair over a `RequestConfig`, per-entity
`Controls`, Mapster instead of AutoMapper — and business envelopes at HTTP 200.
Educational error endpoints and HTTP/provider protocols retain their explicit
status contracts; see §8.8.

## Decisions (locked by the developer)

| # | Decision | Consequence |
| --- | --- | --- |
| D1 | **Envelope-only responses, always 200.** Actions return `InsertReturn` / `PaginatedResponseListDto<Dto>` / `DtoReturnOne<Dto>`; errors travel in the body via `Result`/`Error`. | The React client (`client/src/app/api/agent.ts`) will need a later frontend phase. Nothing breaks during the kernel phase, because no controller is migrated yet. |
| D4 | **Mirror AccountsApi's layout**: `SourceApi/ReStore.Api`, `SourceApi/ReStore.Domain`, `SourceApi/ReStore.Application`, `SourceApi/ReStore.Infrastructure`, `Test/ReStore.Test`. | `Dockerfile`, `build.yml`, `.dockerignore`, `client/.env.production`, `.vscode/*.json`, solution and deploy docs all need path edits. See §7. |
| D8 | **First pass stops at the shared kernel.** Three libraries + test project + base handlers + generic repositories + Controls + Mapster wiring. No controller is converted to CQRS yet. | The kernel is only proven by tests in this phase; Phase 2 (Products) must follow promptly or it rots. |
| D2 | **Mapster replaces AutoMapper.** During the kernel phase `TypeAdapterConfig` is registered *alongside* AutoMapper; AutoMapper is removed in the last phase. | `API/RequestHelpers/MappingProfiles.cs` and the `IMapper` usages get rewritten at slice time. |
| D5 | **No message-language rule.** The instruction is deleted: each message is written in whatever language fits the feature. Kernel messages are English; feature phases decide case by case. | Deviation 1 is withdrawn — no enforced convention either way. |
| D6 | **Base handlers use constructor injection** instead of the `IIocContainers` service locator. | Behaviour-neutral — see §5.10 for the one lifetime caveat. Pipeline and `virtual` hooks unchanged. |
| D7 | **`ChangeFields` partial-update plumbing is kept.** | `BaseUpdateCommand.ChangeFields` and the `Controls.CheckOnUpdate` filter stay, even though ReStore's forms currently send every column. |
| D9 | **Redis and RabbitMQ come in now — as fail-soft seams with a commented registration.** Interfaces + real adapters + settings exist in code; the real adapters are *not* DI-registered. No-op implementations are registered in their place. | Nothing connects anywhere; the app cannot fail because the servers are missing. See §9 for the switch-on runbook. |
| D10 | **The filter engine is in the kernel.** `FilterInfo` + all 14 `eFilterType` values, translated to an expression by `FilterExpressionBuilder` (reverses deviation 2). | Clients can drive column filters and OR/AND groups, not just the free-text phrase. |
| D11 | **An unknown sort or filter column fails the request**, naming the column (reverses deviation 3). | A client typo is reported instead of silently changing the result set. |
| D12 | **A dedicated phase renames the entities and primary keys** of every table, before the feature slices. | `[CheckKeyExists]` lands there too (deviation 1 moves there). Slices are then written against the final names. See §8. |
| D13 | **Preserve the intentionally added frontend error demonstrations.** | `BuggyController` and its deliberate status/exception responses remain unchanged; the feature-envelope policy must not rewrite them. |

---

## 1. What AccountsApi's architecture actually is

Seven mechanisms, in the order a request meets them. §1.8 then answers the
obvious objection ("we don't have Redis/Elastic/Vault/Keycloak installed").

### 1.1 Controller is a one-liner
```csharp
[HttpPost]
[Authorize(Policy = "DebugPolicy", Roles = "MainUser,Managment")]
public async Task<InsertReturn> RenewAccount([FromBody] RenewAccountCommand request, CancellationToken ct)
    => await _mediator.Send(request, ct);
```
Route is `api/[controller]/[Action]`, so the action name *is* the URL segment.
Controllers carry `[GetUserFromToken]` + `[ErrorHandeller]` as class-level
filters and return the envelope itself — they never build status codes.

### 1.2 Commands / queries are plain data classes
- `BaseAddCommand` (empty marker), `BaseUpdateCommand` (`int Id` +
  `List<string>? ChangeFields` — partial update by column list),
  `BaseDeleteCommand` (`int Id`), `InactiveCommand` (`bool Inactive`),
  `AddFileCommand` (`Stream`, `ContentType`, `FileName`).
- `BaseQuery` (`Id`, `PagingInfo`, `List<OrderInfo>`, `QueryFilter`,
  `ExportInfo`, `FullSearchPhrase`, `MoveData`), `BaseDetailQuery` (required
  `Id`), `BaseInternalQuery` (paging disabled, no tenant filter).
- Every command/query implements `IRequest<T>` where `T` is an envelope:
  `InsertReturn`, `UpdateReturn`, `DeleteReturn`,
  `PaginatedResponseListDto<Dto>`, `DtoReturnOne<Dto>`.
- `[CheckKeyExists(nameof(IMemoryQueryRepository))]` on a command property makes
  the base add-handler verify that foreign key exists and append the property
  name to the failure message.

### 1.3 Return envelopes replace exceptions as control flow
`BaseReturn` = `{ Result: ReturnResult(Failed|Success|Warning), Error,
ErrorCode, Warnings, AdditionalInfo, Debugger }`; `Result` defaults to
`Success`. Domain failures are `DomainResponse(false, "…")`, and `AddTo()`
concatenates several failures into one message. `.Result` is tested at every
step of every base handler.

### 1.4 Base handlers are template methods
`BaseAddCommandHandler<Entity, Command>` runs a fixed pipeline:
```
BeginHandle → CheckOp (account expiry, docs, CheckCommand, CheckKeyExistsFields)
→ MapEntityFromRequest (Mapster) → SetDefaultEntity (AccountId, Inactive)
→ CheckEntity (Controls.CheckOnAdd + CheckEntityOnAdd)
→ Add (repo.AddAsync) → SetResult → metrics + request log + message bus
```
with `virtual` hooks (`MapEntityFromRequest`, `CheckCommand`, `Add`,
`SetResult`, `BeginHandle`, `LogRequest`, …) that concrete handlers override.
`Update` / `Delete` / `InActive` mirror it; `BaseQueryHandler` +
`BaseDetailQueryHandler` cover the read side.

### 1.5 Controls hold per-entity validation
`IControls<Entity>`: `CheckOnAdd`, `CheckOnUpdate<Command>`,
`CheckOnDelete<Command>`, `IsKeyExists`. `BaseControls<Entity>` implements two
generic things — reflection over `[ControlAttribute]` (`Required`,
`MinLength`, `MaxLength`, `RegexPattern`, numeric `MinVal/MaxVal`,
`Mobile`/`NationalCode` field types), and loading the current row via
`IGenericQueryRepositoryBase` (caching it in `LastEntityInfo` for the derived
override). `BranchControls` then adds the real rules ("a system-default row
cannot be renamed", "province/city must exist").

### 1.6 Repositories are generic and expression-driven
`IGenericQueryRepositoryBase<Entity>`: `GetByIdAsync`, `GetByIdsAsync`,
`ListAsync`, `IsKeyExsists`, `IdPropertyName`.
`IGenericCommandRepositoryBase<Entity>`: `AddAsync`, `AddRangeAsync`,
`UpdateAsync(entity, changeFields, afterSave)`, `DeleteAsync(entity, afterSave)`
— the `afterSave` callback receives the transaction plus the changed-field list,
which is how cross-service guards and change logging run inside the
transaction.

The important trick: the handler supplies
`Expression<Func<Entity, object>> SelectExpr`, the repository does
`query.Select(selectExpr)` producing **anonymous** objects, then
`IMapperCustome.MapAnonymous<Entity>(obj)` maps that partial shape back onto an
`Entity`; the handler maps `Entity → Dto` with Mapster. `System.Linq.Dynamic.Core`
supplies string-based `Where`/`OrderBy`, so client-supplied filters and sort
columns stay data, not code.

Persistence is split into `QueryAccountsDbContext` (no-tracking, read
connection string, `SaveChanges` throws) and `CommandAccountsDbContext`.
**`UnitOfWork.cs` is entirely commented out — do not port it.**

### 1.7 Composition root and cross-cutting
`IocSetting.AddIoc(services, configuration)` registers Mapster
(`TypeAdapterConfig.GlobalSettings`, `Default.IgnoreNullValues(true)`,
`config.Scan(assembly)`, `ServiceMapper`), Vault settings, Serilog, Prometheus,
Redis/Elastic/MassTransit, every repository, every control.
`IIocContainers` is a service locator (`_ioc.CreateObject<T>()`) that the base
handlers use instead of constructor injection.

### 1.8 Which of these are load-bearing, and which are decoration

Answering the obvious objection: **Redis, Elastic, Vault, Keycloak, RabbitMQ and
Seq are not installed here, and none of them is needed to port this
architecture.** But they are not equally removable *from the upstream code*, so
it matters where each one sits.

A grep of `Handlers/BaseHandlers/*.cs` for `_ioc.CreateObject<T>()` returns 13
services that the six base handlers resolve **by name**:

| Service | Refs | Backing | ReStore |
| --- | --- | --- | --- |
| `IMessageBroker` | 6 | MassTransit + RabbitMQ | dropped — the worst offender, it is in the constructor *and* awaited in `finally` |
| `IMapperCustome` | 5 | Mapster | **kept** (kernel) |
| `IOpLogger` | 5 | Serilog/Seq wrapper | reduced to `ILogger` |
| `ISessionInfoReader` | 5 | JWT/`HttpContext` claims | replaced by `IBuyerIdentity` + `IIdentityService` (later phases) |
| `IExpressionMethods` | 4 | pure reflection/LINQ helpers | re-implemented if needed, no infra |
| `IDebuggerHelper` | 4 | in-memory debug list | dropped |
| `IShardCache` | 3 | **Redis** (`IDataCache`) | dropped with the whole account-expiry concept — ReStore has no subscriptions |
| `IMessCache` | 3 | **database** table `SysMessEntity` (Persian error texts) | dropped with the Persian messages |
| `IRequestHelper` | 3 | request-log wrapper | dropped (deviation 6) |
| `IInsert/Update/Delete/QueryMetrics` | 4 | Prometheus counters | dropped |

Everything else on the external-infrastructure list is *not* on the kernel path:

| Dependency | Where it actually is upstream | Blocks the port? |
| --- | --- | --- |
| **Vault** (`VaultSharp`) | `Program.cs` — a hard startup dependency: `new SettingReader(url, token)` must succeed or the app throws, and `Setting.Instance` is read everywhere | No. ReStore already loads from `appsettings` + env vars; we port nothing from here |
| **Keycloak** (`IIdsManagment`) | only concrete *feature* handlers (`Account`, `Person`, `Branch`, `Logout`) — never the base handlers. In `BranchCommandHandlers` it is even assigned to an unused `var ids` | No. ReStore uses ASP.NET Identity + its own `TokenService` |
| **Redis** | `DataCache` (`IDatabase`), and through it `ShardCache` / `CounterCache` / `UserInfoCache` | No, once `IShardCache` is gone (§1.8 table above) |
| **Elasticsearch** | exactly one live call, `GoodsQueryHandlers.cs:47` (the `GoodsCommandHandlers` call is commented out) | No |
| **Seq / OpenObserve** | `IocSetting.AddService` logging sink | No — standard `ILogger` works |
| **Prometheus** | in-process counters + `UseMetricServer()`; **no server required** | No |
| Refit, AWSSDK.S3, CsvHelper, YamlDotNet, ImageSharp, Kavenegar | feature-specific (Moadian API, exports, SMS OTP) | No |

**So the kernel's dependency set is six packages and zero required servers:**

| Project | Packages |
| --- | --- |
| `ReStore.Domain` | `Microsoft.Extensions.Identity.Stores` (for `User : IdentityUser<int>`, `Role : IdentityRole<int>`) |
| `ReStore.Application` | `MediatR` 13.1.0 |
| `ReStore.Infrastructure` | `Mapster` 7.4.0, `Mapster.DependencyInjection` 1.0.1, `System.Linq.Dynamic.Core` 1.7.1, `Microsoft.EntityFrameworkCore` (+ `.Sqlite`), **`StackExchange.Redis` 2.10.1**, **`MassTransit` 8.5.7 + `MassTransit.RabbitMQ` 8.5.7** — Npgsql already comes from the web project |
| `ReStore.Test` | `xunit`, `FluentAssertions`, `Moq` |

Everything runs against the local Postgres/SQLite that ReStore already uses.
The two infrastructure packages are the ones you asked to keep (D9): they are
referenced and their adapters are written, but **no code path connects to Redis
or RabbitMQ** — see §9.

The shape that makes this cheap is `interface-in-Application,
implementation-in-Infrastructure`. Upstream uses it to bind itself to Vault,
Redis and RabbitMQ; we use the same seam in the opposite direction: the
interface is ready, the adapter is written, the registration is a comment. When
the server grows the two services, switching on is four uncommented lines and
two settings keys — no handler, command or DTO changes.

The one trap worth naming: **do not copy the base handlers verbatim.** The
*pipeline* and the *hook set* are what we copy; the 13 `CreateObject<T>()` calls
are what we strip. Copying `BaseAddCommandHandler.cs` as-is drags MassTransit,
Redis, Seq and Prometheus into a project that must build without them. By
contrast the two generic repositories (§5.12) really can be copied almost
verbatim — their only infrastructure coupling is the `IDebuggerHelper` and the
`IIocContainers` constructor.

---

## 2. Gap table

| Concern | AccountsApi | ReStore today |
| --- | --- | --- |
| Projects | 4 layers + Jobs + AI metadata | 1 web project (`API`) |
| Messaging | MediatR 13.1.0, CQRS slices | none, controllers call EF |
| Mapping | Mapster 7.4.0 + `MapAnonymous` | AutoMapper 12.0.1 |
| Data access | generic repos + `RequestConfig` + Dynamic LINQ | `StoreContext` directly, `IQueryable` extensions |
| Validation | `IControls<T>` + `[ControlAttribute]`, Persian | DataAnnotations + `ModelState` |
| Errors | envelopes, `Result` flags, always 200 | `ActionResult` + `ProblemDetails` + status codes |
| Paging | `RequestConfig.PagingRequest`, `TotalCount` in envelope | `PagedList<T>` + `Pagination` header |
| Multi-tenancy | `BaseEntity` + `AccountId` auto-filter | single tenant |
| Audit/log | request log + message bus + `EntityChangeEntry` | none |
| Config | Vault (`VaultSharp`), `Setting.Instance` | env vars + `appsettings` |
| Observation | Serilog→Seq, Prometheus `/metrics` | default logging + health checks |
| DbContext | command/query split, read replica | one `StoreContext : IdentityDbContext<User, Role, int>`, Npgsql |
| Tests | xUnit + FluentAssertions + Moq | only `tools/ReStore.Deploy.Tests` (deploy agent) |

---

## 3. Target layout (D4 = mirror)

```
D:\Learn\ReStore\
  ReStore.sln                       7 projects: ReStore.Api + 3 libraries
                                    + ReStore.Test + the 2 deploy tools
  SourceApi/
    ReStore.Api/                    # was API/
      ReStore.Api.csproj            # keeps UserSecretsId 034ef10d-…
      Program.cs  appsettings*.json  Properties/  wwwroot/
      Controllers/                  # unchanged in Phase 0 except using fixes
      Middleware/  Mcp/  Extensions/  RequestHelpers/  DTOs/  Services/
    ReStore.Domain/
      Common/        BaseEntity, [ControlAttribute]
      Entities/      Product, Basket, BasketItem, Address, User, Role,
                     UserAddress, Order aggregate
      Interfaces/Common/      DomainResponse, DomainResponseOne,
                              DomainResponseList, RequestConfig, RequestPaging
      Interfaces/Repository/  IGenericRepositoryBase + non-generic bases
    ReStore.Application/
      Common/        BaseCommand, BaseQuery
      DTOs/          BaseDto, IBaseDtoSetter, envelopes
      Interfaces/    IControls
      Controls/      BaseControls
      Handlers/BaseHandlers/   6 base handlers
      (Commands/Queries/Handlers feature folders arrive in Phase 2+)
    ReStore.Infrastructure/
      Persistence/   StoreContext, DbInitializer, Migrations/
      Repositories/  GenericQueryRepositoryBase, GenericCommandRepositoryBase
      Mapping/       MapsterConfig, MapperCustome
      IoC/           ServiceRegistration
  Test/
    ReStore.Test/                   # xUnit + FluentAssertions + Moq
  client/  tools/  deploy/  docs/
```

`client/.env.production` must point `BUILD_PATH` at
`../SourceApi/ReStore.Api/wwwroot`.

---

## 4. Phase 0 — the move (no behaviour change)

This has to happen first, because `ReStore.Infrastructure` cannot reference the
DbContext while it still lives in the web project.

### 4.1 File moves
| From | To |
| --- | --- |
| `API/Entities/**` (10 files) | `SourceApi/ReStore.Domain/Entities/**`, namespace `API.Entities*` → `ReStore.Domain.Entities*` |
| `API/Data/StoreContext.cs` | `SourceApi/ReStore.Infrastructure/Persistence/` |
| `API/Data/DbInitializer.cs` | `SourceApi/ReStore.Infrastructure/Persistence/` |
| `API/Data/Migrations/**` (7 files) | `SourceApi/ReStore.Infrastructure/Persistence/Migrations/`, namespace `API.Data.Migrations` → `ReStore.Infrastructure.Persistence.Migrations` |
| everything else under `API/` | `SourceApi/ReStore.Api/`, `API.csproj` → `ReStore.Api.csproj` |
| — | new: `ReStore.Domain.csproj`, `ReStore.Application.csproj`, `ReStore.Infrastructure.csproj`, `ReStore.Test.csproj` |

Use `git mv` for these when the work is executed, so history follows.

### 4.2 Editing the solution and configs
- `ReStore.sln`: rewrite the `API` project entry to the new path, add the three
  libraries + the test project.
- `Dockerfile`: `COPY API/API.csproj API/` → copy *all* `SourceApi/*/*.csproj` and
  `dotnet restore SourceApi/ReStore.Api/ReStore.Api.csproj`; `COPY API/ API/` →
  `COPY SourceApi/ SourceApi/` (the publish stage needs the referenced projects too);
  `dotnet publish SourceApi/ReStore.Api/ReStore.Api.csproj`;
  `ENTRYPOINT ["dotnet","ReStore.Api.dll"]`.
- `build.yml`: `dotnet build API/API.csproj` → `SourceApi/ReStore.Api/ReStore.Api.csproj`;
  add a step running `Test/ReStore.Test` once it exists (CI has no application
  test gate today — this is the moment to add one).
- `.dockerignore`: `API/wwwroot` → `SourceApi/ReStore.Api/wwwroot`.
- `client/.env.production`: `BUILD_PATH=../API/wwwroot` → `../SourceApi/ReStore.Api/wwwroot`.
- `.vscode/tasks.json` (3 entries) and `.vscode/launch.json`: repoint to
  `SourceApi/ReStore.Api/ReStore.Api.csproj`; the `program` path is also stale
  (`API/bin/Debug/net7.0/API.dll`) — fix to net9.0 while there.
- `docker-compose.yml` (env vars only — no path change), `deploy/DEPLOY.md`,
  `README.md`, `deploy/AGENT.md`: prose references to `API/…` need updating.
- `artifacts/` is **untracked local scratch** (`git ls-files artifacts` is
  empty; it holds `dotnet-ef` 9.0.11 under `artifacts/ef-tools` and a published
  `api-net9/` snapshot). No git action needed; local `dotnet ef` invocations
  become `--project SourceApi/ReStore.Infrastructure --startup-project SourceApi/ReStore.Api`.
- `API/appsettings.json` + `API/appsettings.Development.json` are **tracked**;
  they move with the web project unchanged.

### 4.3 Controller touch in Phase 0
Controllers keep their EF code verbatim; only the `using` lines change
(`API.Entities` → `ReStore.Domain.Entities`, `API.Data` →
`ReStore.Infrastructure.Persistence`). Anything else in Phase 0 is a bug.

### 4.4 Gate for Phase 0
- `dotnet build ReStore.sln -c Release` succeeds.
- `dotnet ef migrations list --project SourceApi/ReStore.Infrastructure --startup-project SourceApi/ReStore.Api`
  lists exactly `20230515130717_PostGresInitial`,
  `20230531143443_makePictureUrlNullable`, `20230603090725_PublicIdAdded`.
- A fresh Postgres container applies those three, and no
  `__EFMigrationsHistory` row is added on an existing DB.
- The four smoke checks already documented in `deploy/DEPLOY.md` still hold:
  `GET /api/products` → 18 seeded products **with the `Pagination` header**;
  `POST /api/account/login` `admin`/`Pa$$w0rd` → JWT; `/api/orders`
  anonymously → 401; `/api/payments` behaves as before.
- `client` still builds against the new `BUILD_PATH`.
- `docker compose up --build` (local stack) serves the SPA.

### 4.5 Phase 0 — executed (result log)

Branch `develop` at `543df08`, working tree not committed.

**Structure now (7 projects in `ReStore.sln`):**

| Project | TFM | Contents |
| --- | --- | --- |
| `SourceApi/ReStore.Domain` | net9.0 | `Entities/` (12 files, incl. `OrderAggregare/`), `Microsoft.Extensions.Identity.Stores` only |
| `SourceApi/ReStore.Application` | net9.0 | empty shell + MediatR 13.1.0 (kernel lands in Phase 1) |
| `SourceApi/ReStore.Infrastructure` | net9.0 | `Persistence/` (StoreContext, DbInitializer, 7 migration files), Mapster/Redis/MassTransit/EF/Npgsql packages |
| `SourceApi/ReStore.Api` | net9.0 | was `API/`; controllers, DTOs, extensions, MCP, middleware, services, `wwwroot`, appsettings, UserSecretsId preserved |
| `Test/ReStore.Test` | net9.0 | xunit + FluentAssertions + Moq + Sqlite; 8 tests |
| `tools/ReStore.Deploy`, `tools/ReStore.Deploy.Tests` | net8.0 | untouched |

**Namespace rewrite:** two passes, 220 references —
`API.Entities*` → `ReStore.Domain.Entities*` and
`API.Data*` → `ReStore.Infrastructure.Persistence*` (163 refs / 36 files), then
`API.Controllers`, `API.DTOs`, `API.Extensions`, `API.Mcp`, `API.Middleware`,
`API.RequestHelpers`, `API.Services` → `ReStore.Api.*` (57 refs / 33 files).
A Persian `[Description]` string in `ProductMcpTools` that happens to contain the
word "API" was deliberately left untouched.

**Deviations from the plan, all deliberate:**

1. **Resolved:** the web project's `API.*` namespaces were swept to
   `ReStore.Api.*` as a separate step after the move, so the project name and
   its namespaces now agree.
2. `[Owned]` on `ShippingAddress` and `ProductItemOrdered` could not survive:
   `Microsoft.EntityFrameworkCore` is not referenced by `ReStore.Domain`, which
   is the whole point of that project. Ownership moved to fluent
   `builder.Entity<Order>().OwnsOne(...)` / `builder.Entity<OrderItem>().OwnsOne(...)`
   in `StoreContext.OnModelCreating`, and proven schema-neutral by the probe in
   the gates below.
3. The test project was created in Phase 0 (it was Phase 1 item 16) so that the
   move itself had an executable gate.
4. Solution folders `SourceApi` and `Tests` were added for the new projects;
   `Build .NET 9 API` in `build.yml` gained a `Test API kernel` step, because CI
   previously had no application test gate at all.
5. `.vscode/launch.json` also had a stale net7.0 program path; fixed to net9.0
   while repointing it.

**Gates — all passed:**

| Gate | Result |
| --- | --- |
| `dotnet build ReStore.sln -c Release` | succeeded, 0 errors, no new warnings (the ~60 CS86xx warnings are pre-existing in untouched controllers/DTOs) |
| `dotnet ef migrations list` | exactly `20230515130717_PostGresInitial`, `20230531143443_makePictureUrlNullable`, `20230603090725_PublicIdAdded` |
| EF wants no new migration? | **Yes.** `dotnet ef migrations add Phase0Probe` produced a migration with empty `Up` **and** `Down`; probe deleted and the snapshot restored to byte-identical (`SHA256 7EEE85A8…`) |
| `dotnet test Test/ReStore.Test` | 8/8 pass — migration IDs, migration namespace/assembly, snapshot tables, baseline `CreateTable` set, runtime-vs-snapshot **table-set parity**, both owned navigations still owned and stored in the owner's table, and `ReStore.Domain` has no EF reference |
| App starts and serves | `/health/app` 200 Healthy · `/api/orders` 401 · `/swagger/v1/swagger.json` 200 (27 KB, all controllers discovered) · `/` 200 (SPA `index.html` from the moved `wwwroot`) · `/api/buggy/bad-request` 400 · `/api/products` 500 (no database, as expected) |

**Discovered along the way (pre-existing, not caused by the move):** the
snapshot was generated under EF 7 and names owned types
`Owner.Navigation#ClrType`, while the EF 9 runtime model uses the plain CLR
name. The empty probe migration proves EF does not care; the snapshot was left
untouched rather than "fixed".

**Not verifiable in this environment** — run these on your machine:

```bash
# the four documented smoke checks need Postgres
docker compose up -d db
dotnet run --project SourceApi/ReStore.Api -c Release
curl -i http://localhost:5000/api/products          # 18 products + Pagination header
curl -X POST http://localhost:5000/api/account/login \
     -H 'Content-Type: application/json' \
     -d '{"username":"admin","password":"Pa$$w0rd"}'  # JWT
curl -i http://localhost:5000/api/orders             # 401

# the Dockerfile changed the most, so build the image once
docker compose build app

# client now writes into SourceApi/ReStore.Api/wwwroot
cd client && npm run build
```

Sandbox notes: MSBuild's node reuse fails here (`-m:1 -nodeReuse:false`
needed), Docker's engine socket and the VSTest host are blocked by named-pipe
restrictions, and NuGet's feeds are unreachable (all packages came from the
local cache). None of these affect CI or a normal machine.

**Leftovers — resolved:** `store.db` moved to
`SourceApi/ReStore.Api/store.db` (still git-ignored via `**/*.db`),
`API/API.csproj.user` deleted, and the emptied `API/` folder removed. No stale
`API/` path remains in the tree.

**Re-verified after the namespace sweep:** build 0 errors, 8/8 tests, and an
identical runtime smoke (`/health/app` 200, `/api/orders` 401, Swagger 200,
`/` 200, `/api/buggy/bad-request` 400).

---

## 5. Phase 1 — the shared kernel (this pass ends here)

Roughly 35–40 files, ~2.5k lines, of which most is faithful copying with
namespaces and Iranian-specific bits removed.

**Domain**
1. `Common/BaseEntity.cs` — `[NotMapped] _SelectId/_SelectValue/_SelectDisplay/_AdditionalData`
   + `GetKeyValue()`. *Drop* the abstract `CommandRepo4Log()`.
2. `Common/ControlAttribute.cs` — `Required`, `MinLength`, `MaxLength`,
   `RegexPattern`, `RegexErrorMessage`, numeric range. Skip
   `ControlFieldType.Mobile/NationalCode`.
3. `Interfaces/Common/DomainResponse.cs` — `BaseDomainResponse`,
   `DomainResponse`, `DomainResponseOne<T>`, `DomainResponseList<T>`, `AddTo()`.
4. `Interfaces/Common/RequestConfig.cs` — `RequestConfig<Entity>`,
   `RequestConfigNonGeneric`, `RequestPaging`, `OrderInfo`, `RequestBody`
   (`Criteria`, `Select`, `Max`).
5. `Interfaces/Repository/IGenericRepositoryBase.cs` — both generics + the
   non-generic bases.

**Application**
6. `Common/BaseCommand.cs` — the five command bases + `BaseReturn` /
   `InsertReturn` / `UpdateReturn` / `DeleteReturn` / `WarningInfo` /
   `ReturnResult`.
7. `Common/BaseQuery.cs` — `BaseQuery`, `BaseDetailQuery`, `BaseInternalQuery`,
   `PagingInfo`, `OrderInfo`, `FilterInfo`/`eFilterType`, `QueryFilter`
   (**no** `ExportInfo`, `eExportType`, `MoveData`).
8. `DTOs/BaseDto.cs` — `BaseDto`, `IBaseDtoSetter<Dto>`, `BaseDtoReturn<Dto>`,
   `PaginatedResponseListDto<Dto>`, `DtoReturnOne<Dto>`, `BaseSelectOptionDto`.
9. `Interfaces/IControls.cs` + `Controls/BaseControls.cs` — reflection
   validator + `IsKeyExists` row loader.
10. `Handlers/BaseHandlers/` — `BaseAdd`, `BaseUpdate`, `BaseDelete`,
    `BaseInActive`, `BaseQuery`, `BaseDetailQuery`. Pipeline order and the
    `virtual` hook set stay identical. Uses constructor injection (D6); the
    metrics / debugger / request-log / message-bus steps become a single
    optional `IAppLogger` call or are dropped (deviation 6 in §6).
    **These six are edited, not copied** — see §1.8: a verbatim paste pulls
    MassTransit, Redis, Seq and Prometheus into a project that must build
    without any of them.

    *Why constructor injection is behaviour-neutral (D6).* Upstream's
    `_ioc.CreateObject<T>()` is a `GetRequiredService` on the same container,
    so the resolved instance and its lifetime are identical either way. The only
    three differences are: (a) upstream resolves a few services *inside* a
    method rather than at construction, which matters only for services used on
    rare paths — all of those (`IMessCache`, `IOpLogger`, `IShardCache`) are
    dropped here; (b) `CreateObject` also calls `SetInitializable` for
    `IInitializable`, an interface ReStore will not have; (c) the single
    string-based lookup — `[CheckKeyExists]` resolving `c.Attr.RepoName` — has
    no equivalent and becomes a hard-coded `typeof(...)`, i.e. stricter, not
    different. **The one real caveat:** every lifetime must be registered
    explicitly because nothing can be resolved lazily by name. Concretely:
    repositories and `IControls<>` are `Scoped`, `IMapper` (Mapster
    `ServiceMapper`) is `Scoped`, `ICache`/`IMessageBus` are `Singleton`. Get a
    lifetime wrong and the symptom is a captured `StoreContext` / "second
    operation on this context" error rather than a missing registration.

**Infrastructure**
11. `Persistence/StoreContext.cs` + `DbInitializer.cs` (moved in Phase 0).
12. `Repositories/GenericQueryRepositoryBase.cs`,
    `GenericCommandRepositoryBase.cs` — ported almost verbatim, minus the
    account filter, `IDebuggerHelper`, Prometheus metrics and query-text
    logging. These two are the risky-to-reinvent part: copy them.
13. `Mapping/MapperCustome.cs` — `MapAnonymous<T>`; depends on Mapster +
    `System.Linq.Dynamic.Core`.
14. `Mapping/MapsterConfig.cs` — `TypeAdapterConfig.GlobalSettings`,
    `Default.IgnoreNullValues(true)`, `Scan(assembly)`.
15. `IoC/ServiceRegistration.cs` — `AddInfrastructure(configuration)`:
    `AddDbContext<StoreContext>`, `IGenericQueryRepositoryBase<>`,
    `IGenericCommandRepositoryBase<>`, `IControls<>` / `BaseControls<>`,
    `IMapper` (Mapster `ServiceMapper`). Called from `Program.cs`; AutoMapper
    stays registered alongside.

**Test**
16. `Test/ReStore.Test/` — xUnit + FluentAssertions + Moq. Because no
    controller uses the kernel in this phase, *the tests are the only proof it
    works*:
    - `GenericCommandRepositoryBase` add/update/delete against an in-memory or
      SQLite `StoreContext` (ReStore already references
      `Microsoft.EntityFrameworkCore.Sqlite`);
    - `GenericQueryRepositoryBase.ListAsync` with a `SelectExpr` + criteria +
      paging, returning correct `TotalCount`;
    - `MapAnonymous` round-trip on a partial projection (this is the
      compile-invisible trap in §7.4);
    - `BaseControls` validation: required/length/regex/range, plus one
      `IsKeyExists` hit and miss;
    - one `UpdateReturn`/`InsertReturn` failure path proving `Result=Failed`
      and no exception escapes.

**Integration seams for Redis + RabbitMQ (D9)** — present, wired, and inert.

17. `Application/Interfaces/ICache.cs` — never throws; every method reports
    availability in its return value:
    ```csharp
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    ```
18. `Application/Interfaces/IMessageBus.cs` — same rule:
    `Task<bool> PublishAsync<T>(T message, CancellationToken ct = default)` where
    `false` means "not delivered, already logged", never an exception.
19. `Application/Common/InfrastructureSettings.cs` — `RedisSettings`
    (`Configuration`, default `"localhost:6379"`, `InstanceName`, `Enabled = false`)
    and `RabbitMqSettings` (`Host`, `VirtualHost`, `Username`, `Password`,
    `Enabled = false`). `Enabled` is the *second* gate, so even an uncommented
    registration stays inert until the flag flips.
20. `Infrastructure/Caching/` — `NullCache : ICache` (all no-ops, returns
    `false`/`null`, logs nothing) and `RedisCacheService : ICache`.
    The Redis adapter is written to the fail-soft contract:
    - `IConnectionMultiplexer` is created **lazily** on first use, never in the
      constructor, with `AbortOnConnectFail = false`, a short
      `ConnectTimeout`/`SyncTimeout` and `ConnectRetry = 3`;
    - every operation is wrapped in `try/catch (Exception)` → log a single
      warning on the transition to unavailable, return cache-miss (`null`/`false`);
    - nothing is retried in a loop, so an outage costs one log line per call, not
      a hang. Compare with upstream `DataCachSetting._db` — a `static IDatabase[]`
      populated at startup, which throws if Redis is absent. Do not copy that.
21. `Infrastructure/Messaging/` — `NullMessageBus : IMessageBus` (logs at
    `Debug`, returns `false`) and `RabbitMqMessageBus : IMessageBus`.
    The RabbitMQ adapter deliberately does **not** use MassTransit's hosted
    service (`AddMassTransit` + `AddBus` starts the bus during `IHost.StartAsync`
    and will fail host startup if the broker is unreachable — exactly the
    exception you do not want). Instead the bus is built and started on first
    publish inside a `try/catch`, with a bounded start timeout; a failure is
    logged and the instance is discarded so the next publish retries. Startup
    therefore has zero broker dependency even when the adapter is switched on.
22. `Infrastructure/IoC/ServiceRegistration.cs` — registers the seams **today**:
    ```csharp
    services.Configure<RedisSettings>(configuration.GetSection("Redis"));
    services.Configure<RabbitMqSettings>(configuration.GetSection("RabbitMq"));
    services.AddSingleton<ICache, NullCache>();          // no-op, never throws
    services.AddSingleton<IMessageBus, NullMessageBus>(); // no-op, never throws

    // TODO(server): install Redis + RabbitMQ, then swap the two lines above for:
    // services.AddSingleton<ICache, RedisCacheService>();
    // services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
    ```
    Handlers from Phase 2 onward may therefore depend on `ICache`/`IMessageBus`
    immediately; they get no-ops until the swap, and cache-miss/degraded
    behaviour after it if the servers are down — never a 500.
23. `appsettings.json` — add the two sections with `"Enabled": false` and empty
    credentials. `deploy/.env.example` and `deploy/DEPLOY.md` get a short
    "when Redis/RabbitMQ are installed, set these four variables" note.
24. Tests: `NullCache`/`NullMessageBus` never throw; `RedisCacheService`
    returns a cache-miss (not an exception) when constructed against an
    unreachable endpoint. That last test is the guarantee behind "can't throw".

### Definition of done for Phase 1
- Solution builds in Release with no new warnings.
- The app still runs from `SourceApi/ReStore.Api` and every endpoint behaves
  exactly as in Phase 0 (kernel is additive).
- The app starts and serves normally **with no Redis and no RabbitMQ on the
  machine** — trivially true while the registration is commented, and asserted
  by the two seam tests in item 24.
- `Test/ReStore.Test` green, CI runs it.
- `docs/cqrs-port-plan.md` §5 checked off; Phase 2 (Products) queued.

### 5.1 Phase 1 — executed (result log)

37 files, ~2,670 lines: Domain 7, Application 19, Infrastructure 11. Plus 40
tests in 3 test files. Working tree not committed.

| Project | What now exists |
| --- | --- |
| `ReStore.Domain` | `BaseEntity` (marker), `IHasInactive`, `ControlAttribute`, `DomainResponse` + `One`/`List`, `RequestConfig` + `RequestPaging`/`OrderInfo`/`RequestBody`, `IGenericQuery/CommandRepositoryBase`, `EntityChangeEntry`. Entities `Product`, `Basket`, `BasketItem`, `Order`, `OrderItem` now derive from `BaseEntity`. |
| `ReStore.Application` | `BaseCommand` (5 command bases + `BaseReturn`/`InsertReturn`/`UpdateReturn`/`DeleteReturn`), `BaseQuery`, `BaseDto` envelopes, `IControls` + `BaseControls` + `ControlAttributeValidator`, `IMapperCustome`, `IAppLogger`, `ICache`, `IMessageBus`, `RedisSettings`/`RabbitMqSettings`, `ExpressionExtensions`, `EntityReflection`, and the six base handlers. |
| `ReStore.Infrastructure` | `GenericQueryRepositoryBase` / `GenericCommandRepositoryBase`, `EntityKeyResolver`, `MapsterCustome` + `MapsterConfig`, `AppLogger`, `NullCache`/`RedisCacheService`, `NullMessageBus`/`RabbitMqMessageBus`, `ServiceRegistration.AddInfrastructure`. |

`Program.cs` now calls `AddInfrastructure(builder.Configuration)` instead of
registering the DbContext itself; `appsettings.json` gained the two disabled
`Redis`/`RabbitMq` sections.

**Deviations from the item list, all deliberate:**

1. **Resolved by D12 — done in Phase 2.** `[CheckKeyExists]` and the non-generic
   repository base (`INonGenericQueryRepositoryBase`) are implemented, now that a
   migration phase exists to carry them. One deliberate change: the attribute
   names a repository `Type` instead of a string, because a by-name lookup is the
   service locator D6 removed.
2. **Resolved — reversed by D10.** `FilterInfo`/`eFilterType` are ported, with
   `FilterExpressionBuilder` translating them into one predicate (13 operators
   plus `ftWordtoWord` treated as `ftEqual`, `ftBetween`, `ftIn`, and
   `InnerFilterId` OR-groups). One simplification: the `QueryFilter` wrapper is
   gone, so the JSON shape is `"filters": [...]` instead of
   `"filters": { "filterInfo": [...] }`. The builder matches columns against the
   **entity**, not the DTO, because that is what the expression is built from —
   a name map belongs there only if DTOs start renaming columns.
3. `BaseControls.GetSelectForKey()` returns null by default, so a control loads
   the **whole** row rather than upstream's key-only projection. Cheaper reads
   are still available by overriding it; correctness first.
4. Redis/RabbitMQ settings are bound **by indexer** in `ServiceRegistration`,
   because `Microsoft.Extensions.Configuration.Binder` has no 9.0.x in the local
   package cache. No behavioural difference.
5. **Resolved — sharpened by D11.** Ordering columns are validated against the
   entity's properties, and an unknown column now **fails the query naming it**
   instead of being skipped
   (`ListAsync_fails_with_the_name_of_an_unknown_ordering_column`). Note what
   this guard is and is not: EF parameterizes values, so it was never a SQL
   injection surface; the real effects are a clear error instead of a silently
   different result set, plus no sorting by a column the API never meant to
   expose.
6. `BaseInActiveHandler` and `IHasInactive` exist but **no entity implements**
   the marker — soft delete is a product decision, not part of the port.

**Gates:**

| Gate | Result |
| --- | --- |
| `dotnet build ReStore.sln -c Release` | succeeded, 0 errors |
| `dotnet test` | **53/53 pass** |
| App starts with the new DI graph | yes — `/health/app` 200, `/api/orders` 401, Swagger 200, `/` 200, `/api/buggy/bad-request` 400, no DI resolution errors |
| Runs without Redis or RabbitMQ | yes — `NullCache`/`NullMessageBus` registered; `RedisCache_degrades_to_a_miss_when_the_server_is_unreachable` proves the real adapter cannot throw either |

The tests worth knowing about, because each pins a behaviour the port depends on:
partial update writes **only** `changeFields`; a failing `afterSave` rolls the
delete back; a `SelectExpr` projection maps back onto the entity and leaves
unselected columns at their defaults; Mapster's `IgnoreNullValues` keeps a column
the command did not mention; a throwing repository becomes `Result=Failed`
instead of an exception; a failing control stops the add before the repository is
touched; the whole filter vocabulary translates to SQL and composes with
`InnerFilterId` OR-groups; an unknown filter or sort column fails with its name,
never silently; and a Redis server at a closed port yields a cache miss instead
of an exception.

One finding from the filter tests that Phase 3 will care about: `Contains`
compiles to a case-**sensitive** substring match (both in .NET and in Npgsql's
`LIKE`), so a case-insensitive search box needs explicit lowering on both sides —
which is exactly what `ProductExtensions.Search` already does.

**Not in the kernel yet, on purpose:** `AddMediatR` is not wired in `Program.cs`
and no controller uses `IMediator`. That is the first step of the Products slice,
together with the first real slice.

---

## 6. Deviations from AccountsApi (all optional / recommended)

| # | AccountsApi does | Proposal | Why |
| --- | --- | --- | --- |
| 1 | Persian messages inside C# | English | ReStore's UI is English (D5) |
| 2 | `IIocContainers` service locator everywhere | constructor injection | Same pipeline shape, testable handlers, no static `IServiceProvider` (D6) |
| 3 | `AccountId` multi-tenancy auto-filter | omitted | single-tenant store |
| 4 | `CommandAccountsDbContext` + `QueryAccountsDbContext` | one `StoreContext` | no read replica |
| 5 | `UnitOfWork` | not ported | commented out upstream too |
| 6 | `IDebuggerHelper`, `IMetrics`, `IRequestHelper`, `IOpLogger`, message bus, `EntityChangeEntry` | dropped in v1 | each is a subsystem; add after the slices are green |
| 7 | Vault, Serilog/Seq, Prometheus, Elastic, Refit, S3, Kavenegar | not ported | no ReStore use case |
| 7b | Redis via `IDataCache` + `static IDatabase[]`; RabbitMQ via MassTransit hosted service | **packaged and adapted now, registered later** (D9) — `NullCache`/`NullMessageBus` are what DI hands out | you want both eventually, but neither may ever throw or block startup |
| 8 | Application files declaring `namespace AccountsApi.Infrastructure.Helppers` | clean namespaces | the layering should be honest |
| 9 | `BaseEntity.CommandRepo4Log()` abstract | omitted | reflection into every entity for an unported subsystem |
| 10 | `ExportInfo` / `MoveData` in `BaseQuery` | omitted | no Excel export, no record navigation |

---

## 7. Risks

### 7.1 The client contract (deferred by D1, not avoided)
`client/src/app/api/agent.ts` depends on status codes (400 →
`data.errors`/`data.title`, 401/403 → toast, 500 → `/server-error`), the
`Pagination` header, and raw payloads. Under D1 the endpoints will eventually
return `{ result, error, data, totalCount, … }` with HTTP 200. That is a
**frontend** task, scheduled separately: rewrite `agent.ts` (unwrap the
envelope, drop the header parsing, map `Result=Failed` to toasts) plus the
Redux slices that consume `PaginatedResponse`. Nothing in Phases 0–1 triggers
it, because no controller is migrated.

### 7.2 Migrations move with the DbContext
Namespace rewrite across 3 migrations + 3 designers + 1 snapshot (~1.8k lines,
mechanical). EF identifies migrations by timestamp/ID, not namespace, so the DB
is unaffected — verify with `dotnet ef migrations list` and by checking no new
row lands in `__EFMigrationsHistory` (§4.4). Do not "fix" this by adding a
baseline migration.

### 7.3 Build and deploy coupling
Every path edit is enumerated in §4.2. The most easily missed one: the
Dockerfile publish stage currently copies only `API/`, which will not contain
the referenced projects any more — it must copy `SourceApi/`.

### 7.4 The projection trick is compile-invisible
`MapAnonymous<Entity>` maps partial anonymous projections back onto entities by
property name. A DTO/projection mismatch compiles fine and fails at runtime
(this is how `BranchListDto.Memory`/`Case` line up with `SelectExpr`). Mitigated
by the `MapAnonymous` round-trip test in Phase 1 and one per `SelectExpr` later.

### 7.5 AutoMapper + Mapster coexistence
Both can be registered simultaneously as long as `config.Scan()` never picks up
AutoMapper profiles. Remove AutoMapper in the last phase (D2).

### 7.6 `IFormFile` must not leak into Application
Use `AddFileCommand`'s `Stream` + `ContentType` + `FileName`; the controller
converts `IFormFile`. Keeps Application free of `Microsoft.AspNetCore.Http`.

---

## 8. Phase 2+ (queued, not part of this pass)

| Phase | Slice | Notes |
| --- | --- | --- |
| 2 | **Rename entities and primary keys** (D12) | `Product`→`ProductEntity` style names, `Id`→`<Entity>Id` keys, folders `OrderAggregare`→`OrderAggregate`; one migration of `RenameColumn`/`RenameTable` so data survives. DTOs keep `[JsonPropertyName("id")]`, so the wire keeps `"id"`. Also lands `[CheckKeyExists]` + the non-generic repository bases. No kernel changes needed — keys already come from EF metadata. |
| 3 | Products | reference slice: `ProductListQuery`/`ProductDetailQuery`/`ProductFiltersQuery`/`Create|Update|DeleteProductCommand`, `ProductControls`, `IImageStorage`; `ProductMcpTools` moves onto `IMediator` with identical tool names |
| 4 | Basket | `IBuyerIdentity` replaces `User.Identity.Name` + `buyerId` cookie handling |
| 5 | Orders — implemented | Dedicated checkout repository owns one serializable transaction and one `SaveChanges`; stock rules live in `OrderControls`. See §8.5. |
| 6 | Account — implemented | `ITokenGenerator` + `IIdentityService`; transactional guest-basket transfer on login, preserving the legacy replacement rule. See §8.6. |
| 7 | Payments — implemented | `IPaymentGateway`, CQRS payment commands, guarded payment-column updates and verified webhook processing; unknown/pending events keep HTTP 200 `EmptyResult()`. See §8.7. |
| 8 | API shell — implemented | Thin Program, explicit application/API registration, feature binding/exception envelopes, and preserved Buggy/authentication/webhook protocols. See §8.8. |
| 8a | **React client contract migration** | After backend Phases 5–8, adapt `agent.ts`, Redux and forms to the final API contracts; reconcile `searchTerm`/`FullSearchPhrase`, filters/sorting, paging metadata and empty-basket handling. Fix pagination to use the effective requested page size rather than `returnCount`. Verify with `npm ci`, `npm run build` and catalog/search/filter/paging, basket, account, checkout and payment flows. No further client changes before this phase. |
| 9 | Docs | `README.md` API section, `docs/architecture.md` |
| 10 | Switch on Redis + RabbitMQ | after the server has them — uncomment two lines, set four variables, verify degraded mode. See §9 |

### 8.1 Phase 2 — executed (result log)

Entities and keys renamed, `[CheckKeyExists]` landed, one data-preserving migration.

| Before | After |
| --- | --- |
| `Product` / `Id` | `ProductEntity` / `ProductId` |
| `Basket` / `Id` | `BasketEntity` / `BasketId` |
| `BasketItem` / `Id` | `BasketItemEntity` / `BasketItemId` |
| `Order` / `Id` | `OrderEntity` / `OrderId` |
| `OrderItem` / `Id` | `OrderItemEntity` / `OrderItemId` |

>150 identifiers across 22 files. **Identity entities were deliberately not
renamed** (`User`, `Role`, `UserAddress`, `Address`): their names are part of the
`IdentityDbContext<User, Role, int>` generic contract and their tables are
framework-owned (`AspNet*`).

Four things this phase taught, each now encoded in the code:

1. **`[Key]` became necessary.** EF's key convention only recognises `Id` or
   `<ClassName>Id`; a `ProductId` inside `ProductEntity` matches neither, so the
   model failed validation with *"requires a primary key to be defined"*.
   `[Key]` (DataAnnotations, not EF) fixes it and keeps `ReStore.Domain` free of
   any persistence framework — which is exactly why AccountsApi uses it too.
2. **Navigation names must not follow the rename.** A scripted rename also turned
   `BasketItemEntity.Product` into `ProductEntity`, which would have made
   `ProductId` a plain column and added shadow foreign keys. Names are back to
   `Product`/`Basket`, with a comment saying why.
3. **`Order → OrderItems` has no explicit FK**, so EF generated a shadow one and
   named it after the principal type → it wanted to rename `OrderItem.OrderId` to
   `OrderEntityOrderId`. Pinned with `HasForeignKey("OrderId")` instead.
4. **Table names are preserved.** EF derives a table name from the DbSet property
   (`Products`, `Baskets`, `Orders`) or the CLR type otherwise, so
   `OrderItemEntity` needed `ToTable("OrderItem")`. `BasketItems` already had
   `[Table]`.

**Migration:** `20261025091004_RenameEntityKeys` — five `RenameColumn`
operations and nothing else. EF was willing to produce this as a rename (same
type, same position), so there is no drop/add and **no data loss**. `ef
migrations list` now shows the four migrations, and `MigrationIntegrityTests`
pins that list so the next schema change has to be deliberate.

**Wire unchanged:** `ProductEntity.ProductId` still carries
`[JsonPropertyName("id")]`, because `ProductsController` serialises the entity
directly and the React client reads `product.id`. The attribute moves to the DTO
when the Products slice lands.

**`[CheckKeyExists]` is in** (deviation 1 closed), with one deliberate change from
upstream: the attribute takes a `Type`, not a repository *name*. Resolving a repo
by name is the service-locator lookup ReStore removed in D6, so
`IKeyExistenceChecker` (Infrastructure) resolves the named type and the handler
calls it before mapping the entity; a failure names the property in braces —
`No ProductEntity with key 7 was found. ({ProductId})`. Two tests cover it: a
missing key blocks the add, an existing one lets it through.

**Incident, recorded because it is easy to repeat:** `dotnet ef migrations remove
--force --no-build` ran against a Debug assembly that predated the migration, so
EF removed the *last migration it knew about* — `PublicIdAdded` — instead of the
new one. The folder was restored with `git checkout -- <migrations dir>` and
verified clean against the commit. **Never combine `migrations remove` with
`--no-build`.**

### 8.2 Phase 3 — executed: the Products slice

The first real slice, and with it the end of AutoMapper (D2): Products was its
only consumer.

| Layer | Added |
| --- | --- |
| Application | `ProductListQuery` / `ProductDetailQuery` / `ProductFiltersQuery` + handlers, `CreateProductCommand` / `UpdateProductCommand` / `DeleteProductCommand` + handlers, `ProductControls`, `ProductListDto` / `ProductDetailDto` / `ProductFilterDto`, `IImageStorage`, `IProductQueryRepository`, `IFileCommand`/`UpdateFileCommand` |
| Infrastructure | `CloudinaryImageStorage` (fail-soft), `ProductQueryRepository` (distinct filters + public-id check), registrations for `IControls<ProductEntity>`, `IProductQueryRepository`, `IImageStorage` |
| Api | `ProductsController` reduced to bind → `IMediator.Send` → envelope; `ProductMcpTools` rewired onto `IMediator` |

**Removed:** `ProductExtensions` (its sort/search/filter logic is now
`GetOrdering` / `FullSearchExpr` / `GetAdditionalCriteria`), `PagedList`,
`MappingProfiles`, `ImageService`, `HttpExtensions` (the `Pagination` header is
gone), the AutoMapper and Cloudinary packages from the Api project.

**Wire contract.** Products now answer with the envelope at HTTP 200 (D1).
`[JsonPropertyName("id")]` moved from the entity to the DTOs, so the *product
shape* is unchanged and only the wrapper is new. `agent.ts` was the only client
file touched: it unwraps the envelope, rebuilds the `PaginatedResponse` the
catalog slice already expects (page number from the request parameters, counts
from the envelope), and reads a product back after create/update so the admin
form can still upsert the full record.

**Kernel addition justified by the slice:** `BaseQueryHandler.GetOrdering` — the
catalog sends `orderBy=priceDesc`, a UI sort key rather than a column name, so
the handler translates it (the alternative was leaking that vocabulary into the
controller).

**MCP tools are pass-throughs, by decision.** `list_products`, `get_product`,
`get_product_filters`, `create_product`, `update_product` and `delete_product`
return exactly what their endpoints return — the same envelopes, the same DTOs,
failures included as `succeeded=false`. There are no MCP-specific response
records: a second shape would be a second contract to keep in step, and it would
drift the first time a DTO changed. Tool names, descriptions and parameters are
unchanged.

**Tests: 12 new, 67 total.** They cover page + total, brand∧type filters,
case-insensitive search, the `priceDesc` translation, detail hit/miss, distinct
filters, create-with-image (url/public id persisted), a price below the control
minimum (nothing uploaded), a failing upload surfacing the storage message,
update replacing the image *and* deleting the previous one, update without a file
keeping it, and delete removing the image after the row.

**Not verified here:** `client/node_modules` is absent and the package feeds are
unreachable, so `agent.ts` was reviewed but **not** compiled. Run
`cd client && npm ci && npm run build` (or `npm start`) before trusting the
catalog UI.

**Mixed mode is expected:** basket, orders, account and payments still use
`StoreContext` directly and still answer with status codes, which is why the
axios error checker keeps its 400/401/403/500 branch. Those slices are
Phases 4–7.

**Controller input: the query/command *is* the parameter.** `GetProducts`,
`GetProduct` and `DeleteProduct` bind `ProductListQuery`, `ProductDetailQuery`
and `DeleteProductCommand` straight from the query string / route, so the action
body is one `Send` — exactly the AccountsApi shape. `ProductParams`,
`PaginationParams` and `MetaData` were deleted with this change; the list query
carries flat `PageNumber`/`PageSize` (query-string friendly) and the handler turns
them into `PagingInfo` in its `BuildPaging` override.

**The one exception is the two commands that carry a file**, and it is a real
constraint rather than habit: the upload arrives as `IFormFile`, the command
carries a `Stream`, and §7.6 keeps ASP.NET Core out of Application. So
`CreateProductDto`/`UpdateProductDto` remain as multipart adapters whose
`ToCommand()` does the conversion. Their DataAnnotations were **removed** in the
process: `[ApiController]` turned a missing field into an HTTP 400 ProblemDetails
while every other failure comes back as a 200 envelope (D1). Now there is one
error channel — ProductControls — and one set of messages.

**A regression this phase caught, worth remembering:** the entity-rename script
also renamed the *property* `UserDto.Basket` to `BasketEntity`, which compiles
fine and silently changes the wire contract (`user.basket` → `user.basketEntity`).
The compiler cannot see it; only reading the DTO did. **DTO property names are
part of the API contract and must never follow an entity rename.**

### 8.3 Phase 4 — executed: the Basket slice

| Layer | Added |
| --- | --- |
| Application | `IBuyerIdentity`, `IBasketRepository`, `BasketQuery`, `AddBasketItemCommand`, `RemoveBasketItemCommand` + handlers, `BasketDto`/`BasketItemDto` (moved from Api) with one `FromEntity` factory |
| Infrastructure | `BasketRepository` |
| Api | `HttpBuyerIdentity` (claims or the `buyerId` cookie, the only place that knows either), `BasketController` reduced to bind → `Send` → envelope |

**`IBuyerIdentity` replaces the controller's private `GetBuyerId()`/`RetrieveBasket()`.**
Authenticated users are identified by their name, everyone else by the cookie; the
implementation writes the cookie, so `Application` never sees HttpContext. That
also makes the handlers testable with a two-line fake.

**The basket is an aggregate, so it does not use the base CRUD handlers.** Its
rules ("the same product again increments one line", "removing more than present
clears the line") already live on `BasketEntity`, and applying them needs *tracked*
instances. Two flaws found and fixed while wiring it:

1. **An untracked product in a tracked basket.** The first version loaded the
   product through `IProductQueryRepository` (AsNoTracking) and passed it to
   `basket.AddItem(...)`, which would have made EF treat the product as new and
   try to INSERT it. The repository now loads the product tracked (`FindAsync`)
   and does the load-mutate-save itself, so a handler cannot get the tracking
   wrong.
2. **Removing from a nonexistent basket created one.** The remove path shared the
   get-or-create helper, leaving an empty basket row behind on every stale
   request. Remove now loads without creating and answers with an empty basket.

**"Empty basket" is a success, not an error.** `GET /api/basket` for a visitor
with no basket returns `succeeded: true` with `data: null` — a first-time visitor
is the normal case. On the client, `Basket.get` turns that into a rejection
*without* a toast, which is exactly what the old 404 did, so the basket slice did
not need changing.

**`BasketExtensions.MapBasketToDto` survives for now** (account, orders and
payments still read `StoreContext` directly) but delegates to
`BasketDto.FromEntity`, so there is one mapping rather than two.

**Tests: 8 new, 75 total** — empty basket, first add creating the basket and an
anonymous id, repeat add merging lines, unknown product failing without creating
a basket, zero quantity rejected, remove decrementing then clearing, an
authenticated buyer using their user name and never getting a cookie, and
remove-without-basket being a no-op.

**Smoke (no database available):** `GET /api/basket` → 200
`{"data":null,"result":true,"succeeded":true}`; `POST /api/basket?productId=1&quantity=2`
→ 200 with `succeeded:false` and the connection error; `DELETE` → 200. DI resolves
`IBuyerIdentity` and `IBasketRepository` with no startup errors.

---

### 8.4 Review follow-up — Basket read failures and deferred client work

Basket reads now distinguish absence from failure: the repository returns a
successful response with null data when no basket exists, while database errors
remain failed responses. The query logs the failure and returns a failed envelope
instead of converting it into an empty success. A regression test exercises a real
EF read against an SQLite database without a schema; the existing missing-basket
test also checks the repository's successful-null contract.

Further React changes are explicitly deferred to Phase 8a. Earlier Products/Basket
client adaptations remain historical work, not proof that the client migration is
complete. Search parameter alignment and pagination are tracked in that phase.
Feature validation stays in specific controls derived from `BaseControls<T>`;
this follow-up does not expand product update validation or the generic controls.

### 8.5 Phase 5 — executed: the Orders slice

The preceding Basket correction and client-phase plan were committed as
`4b8cbfa` (`Preserve basket read failures and schedule the React contract migration`).

| Layer | Added / changed |
| --- | --- |
| Application | `CreateOrderCommand`, `OrderListQuery`, `OrderDetailQuery`, handlers, `OrderControls : BaseControls<OrderEntity>`, `IOrderRepository`, `OrderDto`/`OrderItemDto` |
| Infrastructure | `OrderRepository` and scoped registrations for the repository and controls |
| Api | `OrdersController` binds the command/query directly and sends it through MediatR; old `CreateOrderDto`, order DTOs and `OrderExtensions` removed |

**Transaction ownership is explicit.** Checkout is not a generic single-row add:
the dedicated repository loads the buyer's basket and products, runs
`OrderControls`, builds item/price/address snapshots, optionally creates or updates
the user's address, reduces stock and removes the basket. It saves once inside a
serializable transaction. No generic write repository is called, so its
`ChangeTracker.Clear()` cannot detach pending checkout changes. The checkout
repository clears its own tracking only when the operation finishes. Disposal
rolls the transaction back on failure, including an exception after the database
writes but before commit.

Serializable isolation is chosen to prevent concurrent checkouts from committing
against the same basket or stale product stock. Conflicts return a failed envelope;
there is no automatic checkout retry. This concurrency behaviour still requires
verification against a real PostgreSQL server.

**Controls own the rules.** Missing/empty baskets, nonpositive line quantities,
unavailable products, insufficient stock and missing shipping addresses fail
without consuming the basket. The delivery rule is preserved: subtotal greater
than 10000 gets free delivery, otherwise 500. Existing saved addresses are updated
in place. Products without an image use an empty image URL in the order snapshot,
which fits the existing non-null snapshot column without changing the schema.

**Reads use the kernel.** The list and detail handlers inherit the base query
handlers, including filtering, sorting and paging. Both enforce an authenticated
buyer and add ownership criteria which client filters cannot remove. Nested item
snapshots and shipping addresses are projected and mapped to the Application DTOs.
Detail returns the same not-found failure for missing and another buyer's orders.

**Wire contract:** list returns `PaginatedResponseListDto<OrderDto>`, detail returns
`DtoReturnOne<OrderDto>`, create returns `InsertReturn` with the new order key.
Routes and `[Authorize]` remain; successful dispatches return HTTP 200 with the
outcome in the body. The list now uses kernel paging (`PagingInfo.PageNumber`,
`PagingInfo.PageRecordCount`, default 25) and newest-first ordering. DTO fields
retain `id`, `buyerId`, `orderItems`, totals and the status string. React is
deliberately unchanged; adapting Orders' old raw list/id responses is Phase 8a work.

**Verification:** API build succeeded with zero errors (existing nullable warnings
remain). All **87 tests passed**, including 11 new Orders cases covering checkout,
delivery threshold, new/existing saved address, missing/empty basket, insufficient
stock, missing address, rollback after actual writes, ownership, nested snapshots,
filtering/paging and anonymous access. SQLite verifies relational persistence and
rollback; no real PostgreSQL write or simultaneous checkout test was run.

**HTTP smoke:** with a deliberately unreachable local database and a temporary test
JWT key, anonymous `GET /api/orders` returned 401; authenticated list, detail and
create returned HTTP 200 with `succeeded:false`. This exercises routing, DI,
MediatR dispatch and database failure envelopes. Windows EventLog logging was
disabled only in the smoke process because the sandbox cannot write EventLog;
no application configuration was changed for that workaround.

### 8.6 Phase 6 — executed: the Account slice

Orders was committed as `352cbd0`
(`Move orders to CQRS with atomic checkout and buyer-scoped reads`).

| Layer | Added / changed |
| --- | --- |
| Application | `LoginCommand`, `RegisterCommand`, `CurrentUserQuery`, `SavedAddressQuery`, handlers, `UserDto`/`UserAddressDto`, `IIdentityService`, `ITokenGenerator`, `IAccountBasketRepository` |
| Infrastructure | `IdentityService`, `JwtTokenGenerator`, `AccountBasketRepository`; Identity registration moved from Program into the composition root |
| Api | `AccountController` binds commands and dispatches through MediatR; old login/register/user DTOs and `TokenService` removed |

**Identity keeps its own validation pipeline.** `User` is an ASP.NET Identity
entity, not a `BaseEntity`, so registration uses UserManager's existing username,
email and password validators rather than forcing Identity through generic CRUD
controls. Unique-email policy, Member role assignment, HS512 signing, name/email/
role claims and seven-day UTC token expiry are preserved. The JWT package reference
in Infrastructure uses 8.19.2, the version already resolved by the API.

Registration wraps user creation and Member assignment in one transaction, checks
both Identity results and rolls everything back if either fails. Validation
messages and Identity error codes go into the failed InsertReturn.

**Guest-basket transfer preserves legacy behaviour.** If a guest basket exists,
it replaces the previous account basket; quantities from the two baskets are not
combined. The transfer and old-basket deletion run in one serializable transaction.
Token creation happens before transfer, and the cookie is cleared only after a
successful response. The stored username is used, not the login's casing. Without
a guest basket, the existing account basket is returned unchanged.

`IBuyerIdentity.AnonymousBuyerId` exposes the guest cookie independently of the
authenticated identity. The transfer repository only accepts generated GUID-shaped
guest IDs, so a cookie containing another account's username cannot transfer that
account's basket. No HTTP types enter Application.

Current-user and saved-address handlers enforce an authenticated buyer; missing
accounts and database failures become failed envelopes. No saved address is a
successful response with null data. Current-user does not hide basket read errors.

**Wire contract:** existing routes are retained. Login/currentUser return
`DtoReturnOne<UserDto>` containing `email`, `token`, `basket`; registration returns
`InsertReturn` with the user key; savedAddress returns `DtoReturnOne<UserAddressDto>`
with the existing address fields. Handled failures return HTTP 200 with
`succeeded:false`; protected routes still reject missing/invalid authentication.
React is unchanged: adapting its account thunks, form errors and registration's
old 201 response belongs to Phase 8a. No schema migration is needed.

**Verification:** all **99 tests passed** (12 new Account cases), including real
UserManager password hashing/role membership, signed-token validation, invalid
credentials, case-insensitive login, guest transfer with/without an existing account
basket, forged username cookies, rollback after actual transfer writes, token
failure, registration rollback on missing role, current-user/address access and
basket read failure propagation. API build succeeded with zero errors; the two
remaining warnings in PaymentsController predate this phase.

**HTTP smoke:** with the database deliberately unreachable, currentUser and
savedAddress returned 401 anonymously and HTTP 200 failed envelopes with a test
JWT. Login and registration returned HTTP 200 failed envelopes for empty input and
for database failures. This verifies routing, DI and dispatch; successful database
flows were tested with SQLite, not PostgreSQL. The smoke process alone disabled
Windows EventLog logging as in Phase 5.

### 8.7 Phase 7 — executed: the Payments slice

Account was committed as `2552ff8`
(`Move account flows to CQRS with transactional registration and basket transfer`).

| Layer | Added / changed |
| --- | --- |
| Application | `CreateOrUpdatePaymentIntentCommand`, `ProcessPaymentWebhookCommand`, handlers, `PaymentControls : BaseControls<BasketEntity>`, `IPaymentGateway`, `IPaymentRepository`, provider-neutral payment records |
| Infrastructure | `StripePaymentGateway`, `PaymentRepository`, scoped registrations; Stripe.net 41.15.0 moved from Api to Infrastructure without a version change |
| Api | PaymentsController delegates through MediatR; webhook retains a raw-body adapter for signature verification; old PaymentService and now-unused BasketExtensions removed |

**Payment intent requests** require an authenticated buyer, load that buyer's
basket, reject missing/empty or invalid lines, and calculate the amount from
database prices with checked arithmetic. The USD/card flow and delivery threshold
are preserved. A deterministic idempotency key includes the basket, existing
intent and amount so the same gateway operation can be retried consistently.
The Stripe client is configured per adapter call instead of mutating the SDK's
global API key. No connection is made during startup.

Only payment columns are saved, using one guarded SQL UPDATE keyed by basket ID,
buyer and expected/current intent. This avoids attaching the product graph and
rejects a basket transferred, deleted or assigned a competing intent while Stripe
was being called. Repeated requests may succeed even when the intent/secret values
are unchanged; they no longer depend on SaveChanges returning a positive count.

Stripe and the database do not share a transaction. If Stripe succeeds and the
database save fails, the request returns a failed envelope; idempotency supports
retrying the same gateway operation. No distributed transaction, payment ledger or
reconciliation subsystem is introduced in this refactor.

**Webhook protocol is an explicit exception to D1's browser envelope contract.**
The adapter uses Stripe's signature verification on the exact raw JSON before
interpreting any data. Valid unrelated events, pending charges and charges without
a matching order are acknowledged with HTTP 200 and an empty body, preserving the
planned behaviour. Invalid signatures/payloads return 400; configuration or
database processing failures return 500 so the provider may retry. These failure
responses do not expose gateway/database error details. The command itself still
uses UpdateReturn internally; the controller translates it to provider acknowledgements.

Terminal charge states update only matching orders. Repeated delivery is a no-op;
success may supersede failure, and a delayed failure cannot downgrade an already
paid order. Updates are conditional SQL statements, so this guard is atomic.
As before, an event arriving before its order exists is acknowledged; reconciling
that payment/order timing is not added by this slice.

**Wire contract:** POST `/api/payments` now returns
`DtoReturnOne<BasketDto>` at HTTP 200 for handled outcomes. Its input and protected
route are unchanged. POST `/api/payments/webhook` stays anonymous with verified
provider input and the empty acknowledgements described above. React is unchanged
and its payment response adaptation remains in Phase 8a. No schema migration is needed.

**Verification:** all **119 tests passed**, including 20 new Payments cases:
amount/delivery calculation, create/repeated-update persistence, empty/missing
baskets, authenticated ownership, gateway/read/save failures, competing intent
guards, locally signed terminal/pending/unrelated events, unmatched orders,
duplicate/out-of-order delivery, invalid signatures and database failure handling.
Adapter request tests use a mocked Stripe SDK client to verify create/update
payloads and idempotency keys without sending a network request. The complete
suite and API build succeeded with zero errors.

**HTTP smoke:** using an unreachable local database and temporary JWT/webhook keys,
anonymous POST payments returned 401, authenticated POST returned a failed HTTP 200
envelope, signed unrelated/pending webhooks returned empty HTTP 200, a signed
terminal event returned 500 on the database error, and an invalid signature returned
400. Windows EventLog was disabled only for the temporary smoke process. Real
Stripe sandbox/production calls and PostgreSQL writes were not exercised.

### 8.8 Phase 8 — executed: the API shell

Payments was already committed as `7518baf`
(`Move payments to CQRS with isolated Stripe integration and verified webhooks`).
The working tree was clean when Phase 8 started, so no duplicate/empty commit was
created. The developer explicitly asked to retain the intentional frontend error
demonstrations (D13).

**Composition root:** `Program.cs` now calls `AddApplication`, `AddInfrastructure`,
`AddApiServices`, `InitializeDatabaseAsync` and `UseApi`. MediatR assembly scanning
belongs to Application's registration; HTTP concerns (MVC, Swagger, MCP, JWT, CORS,
buyer identity and health endpoints) live in API extensions. Database migration
and seeding retain their existing startup/degraded-mode behaviour.

**Error policy is opt-in.** `EnvelopeResponseAttribute` marks Products, Basket,
Orders and Account controllers, plus the payment-intent action. It is deliberately
absent from BaseApiController, BuggyController, the Stripe webhook, MCP and SPA/
health endpoints. There is no URL-prefix heuristic or global rewriting of status
codes.

On marked endpoints, MVC model-binding/validation failures now return HTTP 200
with the common `ApiErrorResponse` envelope: `succeeded:false`,
`errorCode:"ValidationFailed"`, a summary error and a field-to-messages `errors`
dictionary. Invalid query numbers and malformed JSON are covered. Unhandled
exceptions on marked endpoints return HTTP 200 with `errorCode:"UnhandledError"`;
production receives a generic message rather than exception details. Handler-level
business outcomes keep their existing typed envelopes. Clients should use
`succeeded` for both kinds of failures.

**Intentional HTTP errors are preserved.** BuggyController's source and all five
demonstration endpoints remain unchanged, including the thrown server exception
and manual ValidationProblem response. Authentication challenges/role denials
remain 401/403. Stripe webhook acknowledgements/rejections/retry failures remain
200/400/500. Native routing, method and media-type protocol statuses are not
converted to business outcomes. The middleware retains its existing ProblemDetails
500 path for unmarked endpoints and still avoids rewriting a started response.

Swagger, static hosting, MCP, the credentialed localhost React CORS policy and
`/health/app` / `/health/db` remain available. An HTTP test exposed the SPA fallback's
dependence on the process working directory; it now resolves index.html using the
host's WebRootPath, so a correctly configured content root works independently of
the launch directory. No business validation, schema, or React source changed.

**Verification:** all **136 tests passed** (119 existing and 17 new HTTP-shell
cases). The API build succeeded with zero errors. The new tests use ASP.NET
TestServer with real API registration, middleware, MVC binding, authentication,
authorization, CORS and routing; a controlled mediator isolates transport failures
from the already-tested feature handlers. They cover invalid query/JSON envelopes,
production exception sanitization, all five Buggy responses, 401/403, webhook
protocol responses, Swagger/health/SPA availability and CORS. TestHost is a test-only
dependency; the test project now references the API.

An additional standalone-process smoke run with the real DI graph and an
intentionally unreachable database verified `/`, Swagger and `/health/app` at 200,
`/health/db` at 503, all Buggy status codes unchanged, and an invalid product query
at HTTP 200 with a validation envelope. The process was launched outside the API
directory with an explicit content root to check the SPA fallback fix. EventLog
was disabled only in this temporary smoke process, as in earlier phases.

Phase 8a remains pending: React must adapt account/order/payment envelopes,
validation dictionaries, search/filter/sort inputs, paging and empty-basket handling.
Its intentional-error demo must continue handling the classic Buggy HTTP errors.

## 9. Deferred infrastructure — the switch-on runbook

Redis and RabbitMQ are **packaged, adapted and inert** after Phase 1 (D9). This
is what "inert" is guaranteed to mean, and what to do when the server is ready.

### 9.1 The invariant

> No request may fail because Redis or RabbitMQ is unavailable, and neither may
> delay startup.

Concretely, three separate gates have to open before a single byte is sent:

1. the `ICache`/`IMessageBus` registration is uncommented (§5.22);
2. `Redis:Enabled` / `RabbitMq:Enabled` is `true`, otherwise the adapter itself
   short-circuits to the no-op path;
3. the adapter's lazy connect succeeds. If it does not: one warning log, and the
   caller sees a cache-miss (`null`) or `PublishAsync == false` — never an
   exception, never a retry storm.

The adapters therefore deliberately avoid the two upstream patterns that break
this invariant: `DataCachSetting`'s `static IDatabase[]` filled at startup, and
MassTransit's hosted bus (`AddBus` starts during `IHost.StartAsync` and fails
host startup when the broker is down).

### 9.2 Switching on

1. Add the two services to the server stack (`deploy/compose.yml`, following the
   existing `db` service pattern), and to the local `docker-compose.yml` if you
   want to exercise them locally.
2. Set the four variables in `deploy/.env`:
   `Redis__Configuration`, `Redis__Enabled=true`,
   `RabbitMq__Host`, `RabbitMq__Enabled=true` (plus credentials if they are not
   the defaults).
3. Uncomment the two `AddSingleton` lines in `ServiceRegistration` (§5.22).
4. Redeploy. Nothing else changes — no handler, command, query or DTO.

### 9.3 Verification (do this with the servers *stopped* first)

- With `Enabled=false` and the registration uncommented: app starts, endpoints
  normal, no connection attempt in the logs.
- With `Enabled=true` and Redis/RabbitMQ stopped: app still starts, endpoints
  still return 200, logs contain exactly one warning per adapter. Nothing 500s.
- With `Enabled=true` and the servers running: a `Set`/`Get` round-trip works and
  a published message is visible in the RabbitMQ management UI.
- Health checks: if you add `AspNetCore.HealthChecks.Redis` /
  `...Rabbitmq`, register them under a **new tag** (e.g. `deps`) and do *not*
  add those tags to `/health/db`'s predicate — otherwise the existing readiness
  probe starts reporting unhealthy because a cache is down, which is exactly the
  coupling this design avoids.

### 9.4 First real consumer

Best first use once switched on: `ProductFiltersQuery` (brands/types distinct
lists — cheap to cache, invalidated on product write) and an
`OrderCreatedMessage` published from `CreateOrderCommand`. Both are Phase 2/4
work, so the seams will have a real consumer by then rather than staying
speculative.

---

## 10. What I will not touch

- `client/` — no frontend changes in this pass.
- `tools/ReStore.Deploy*`, `deploy/compose.yml` (image-based, path-free) —
  only the `*.md` prose paths in §4.2.
- Any git action (no commit/push/branch) unless explicitly asked.
- `D:\Projects\AccountsApi` — read-only, as instructed.
