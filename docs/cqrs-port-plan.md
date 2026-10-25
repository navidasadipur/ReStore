# Porting AccountsApi's CQRS / clean-architecture layering into ReStore

Status: **plan only** — no code has been changed. Only this file is new.
Sources read: `D:\Projects\AccountsApi` (branch `develop`, HEAD `389ad5e`) and
`D:\Learn\ReStore` (branch `develop`, HEAD `543df08`).

Goal: ReStore's backend adopts the same shape as AccountsApi —
`Domain / Application / Infrastructure / Api`, MediatR CQRS through base
handlers, a generic repository pair over a `RequestConfig`, per-entity
`Controls`, Mapster instead of AutoMapper — and the same wire style
(envelopes, always HTTP 200).

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

1. `[CheckKeyExists]` and the non-generic repository bases are **not** in the
   kernel. The attribute is only meaningful against real feature repositories,
   so it lands with Phase 2 rather than as an untested seam.
2. `FilterInfo`/`QueryFilter`/`eFilterType` are **not** ported. Honouring them
   needs upstream's `IFilterOperate` expression builder, and ReStore's queries
   need only `FullSearchPhrase` + `GetAdditionalCriteria`. A silent
   pass-through would have been worse than absence.
3. `BaseControls.GetSelectForKey()` returns null by default, so a control loads
   the **whole** row rather than upstream's key-only projection. Cheaper reads
   are still available by overriding it; correctness first.
4. Redis/RabbitMQ settings are bound **by indexer** in `ServiceRegistration`,
   because `Microsoft.Extensions.Configuration.Binder` has no 9.0.x in the local
   package cache. No behavioural difference.
5. Ordering column names are validated against the entity's properties before
   they reach Dynamic LINQ (`ListAsync_ignores_an_ordering_column_that_is_not_a_property`).
   Upstream passed the client's column name straight through.
6. `BaseInActiveHandler` and `IHasInactive` exist but **no entity implements**
   the marker — soft delete is a product decision, not part of the port.

**Gates:**

| Gate | Result |
| --- | --- |
| `dotnet build ReStore.sln -c Release` | succeeded, 0 errors |
| `dotnet test` | **40/40 pass** |
| App starts with the new DI graph | yes — `/health/app` 200, `/api/orders` 401, Swagger 200, `/` 200, `/api/buggy/bad-request` 400, no DI resolution errors |
| Runs without Redis or RabbitMQ | yes — `NullCache`/`NullMessageBus` registered; `RedisCache_degrades_to_a_miss_when_the_server_is_unreachable` proves the real adapter cannot throw either |

The tests worth knowing about, because each pins a behaviour the port depends on:
partial update writes **only** `changeFields`; a failing `afterSave` rolls the
delete back; a `SelectExpr` projection maps back onto the entity and leaves
unselected columns at their defaults; Mapster's `IgnoreNullValues` keeps a column
the command did not mention; a throwing repository becomes `Result=Failed`
instead of an exception; and a failing control stops the add before the
repository is touched.

**Not in the kernel yet, on purpose:** `AddMediatR` is not wired in `Program.cs`
and no controller uses `IMediator`. That is the first step of Phase 2, together
with the first real slice.

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
| 2 | Products | reference slice: `ProductListQuery`/`ProductDetailQuery`/`ProductFiltersQuery`/`Create|Update|DeleteProductCommand`, `ProductControls`, `IImageStorage`; `ProductMcpTools` moves onto `IMediator` with identical tool names |
| 3 | Basket | `IBuyerIdentity` replaces `User.Identity.Name` + `buyerId` cookie handling |
| 4 | Orders | basket→order conversion is the `afterSave`/unit-of-work shape; stock rules go in `OrderControls` |
| 5 | Account | `ITokenGenerator` + `IIdentityService`; basket merge on login |
| 6 | Payments | `IPaymentGateway`; webhook must keep returning 200 `EmptyResult()` for unknown/pending events |
| 7 | API shell | `Program.cs` slims; the envelope→HTTP decision (D1) lands here together with the client update |
| 8 | Docs | `README.md` API section, `docs/architecture.md` |
| 9 | Switch on Redis + RabbitMQ | after the server has them — uncomment two lines, set four variables, verify degraded mode. See §9 |

---

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
