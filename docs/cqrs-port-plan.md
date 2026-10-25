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
| D4 | **Mirror AccountsApi's layout**: `SourceApi/ReStore.Api`, `SourceApi/ReStore.Domain`, `Source/ReStore.Application`, `Source/ReStore.Infrastructure`, `Test/ReStore.Test`. | `Dockerfile`, `build.yml`, `.dockerignore`, `client/.env.production`, `.vscode/*.json`, solution and deploy docs all need path edits. See §7. |
| D8 | **First pass stops at the shared kernel.** Three libraries + test project + base handlers + generic repositories + Controls + Mapster wiring. No controller is converted to CQRS yet. | The kernel is only proven by tests in this phase; Phase 2 (Products) must follow promptly or it rots. |

Still my recommendation, not yet decided (cheap to change):
- **D2** Mapster replaces AutoMapper (recommended). During the kernel phase `TypeAdapterConfig` is registered *alongside* AutoMapper, so nothing breaks.
- **D5** Messages in **English** (recommended) rather than Persian — ReStore's UI is English.
- **D6** Base handlers use **constructor injection** instead of `IIocContainers` service locator (recommended). Pipeline and hooks stay identical.
- **D7** Keep the `ChangeFields` partial-update plumbing (recommended; costs nothing).

---

## 1. What AccountsApi's architecture actually is

Seven mechanisms, in the order a request meets them.

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
  ReStore.sln                       5 projects: 4 new + ReStore.Deploy pair
  Source/
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
`../Source/ReStore.Api/wwwroot`.

---

## 4. Phase 0 — the move (no behaviour change)

This has to happen first, because `ReStore.Infrastructure` cannot reference the
DbContext while it still lives in the web project.

### 4.1 File moves
| From | To |
| --- | --- |
| `API/Entities/**` (10 files) | `Source/ReStore.Domain/Entities/**`, namespace `API.Entities*` → `ReStore.Domain.Entities*` |
| `API/Data/StoreContext.cs` | `Source/ReStore.Infrastructure/Persistence/` |
| `API/Data/DbInitializer.cs` | `Source/ReStore.Infrastructure/Persistence/` |
| `API/Data/Migrations/**` (7 files) | `Source/ReStore.Infrastructure/Persistence/Migrations/`, namespace `API.Data.Migrations` → `ReStore.Infrastructure.Persistence.Migrations` |
| everything else under `API/` | `Source/ReStore.Api/`, `API.csproj` → `ReStore.Api.csproj` |
| — | new: `ReStore.Domain.csproj`, `ReStore.Application.csproj`, `ReStore.Infrastructure.csproj`, `ReStore.Test.csproj` |

Use `git mv` for these when the work is executed, so history follows.

### 4.2 Editing the solution and configs
- `ReStore.sln`: rewrite the `API` project entry to the new path, add the three
  libraries + the test project.
- `Dockerfile`: `COPY API/API.csproj API/` → copy *all* `Source/*/*.csproj` and
  `dotnet restore Source/ReStore.Api/ReStore.Api.csproj`; `COPY API/ API/` →
  `COPY Source/ Source/` (the publish stage needs the referenced projects too);
  `dotnet publish Source/ReStore.Api/ReStore.Api.csproj`;
  `ENTRYPOINT ["dotnet","ReStore.Api.dll"]`.
- `build.yml`: `dotnet build API/API.csproj` → `Source/ReStore.Api/ReStore.Api.csproj`;
  add a step running `Test/ReStore.Test` once it exists (CI has no application
  test gate today — this is the moment to add one).
- `.dockerignore`: `API/wwwroot` → `Source/ReStore.Api/wwwroot`.
- `client/.env.production`: `BUILD_PATH=../API/wwwroot` → `../Source/ReStore.Api/wwwroot`.
- `.vscode/tasks.json` (3 entries) and `.vscode/launch.json`: repoint to
  `Source/ReStore.Api/ReStore.Api.csproj`; the `program` path is also stale
  (`API/bin/Debug/net7.0/API.dll`) — fix to net9.0 while there.
- `docker-compose.yml` (env vars only — no path change), `deploy/DEPLOY.md`,
  `README.md`, `deploy/AGENT.md`: prose references to `API/…` need updating.
- `artifacts/` is **untracked local scratch** (`git ls-files artifacts` is
  empty; it holds `dotnet-ef` 9.0.11 under `artifacts/ef-tools` and a published
  `api-net9/` snapshot). No git action needed; local `dotnet ef` invocations
  become `--project Source/ReStore.Infrastructure --startup-project Source/ReStore.Api`.
- `API/appsettings.json` + `API/appsettings.Development.json` are **tracked**;
  they move with the web project unchanged.

### 4.3 Controller touch in Phase 0
Controllers keep their EF code verbatim; only the `using` lines change
(`API.Entities` → `ReStore.Domain.Entities`, `API.Data` →
`ReStore.Infrastructure.Persistence`). Anything else in Phase 0 is a bug.

### 4.4 Gate for Phase 0
- `dotnet build ReStore.sln -c Release` succeeds.
- `dotnet ef migrations list --project Source/ReStore.Infrastructure --startup-project Source/ReStore.Api`
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

### Definition of done for Phase 1
- Solution builds in Release with no new warnings.
- The app still runs from `Source/ReStore.Api` and every endpoint behaves
  exactly as in Phase 0 (kernel is additive).
- `Test/ReStore.Test` green, CI runs it.
- `docs/cqrs-port-plan.md` §5 checked off; Phase 2 (Products) queued.

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
| 7 | Vault, Serilog/Seq, Prometheus, Redis, Elastic, MassTransit, Refit, S3 | not ported | not needed here |
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
the referenced projects any more — it must copy `Source/`.

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

---

## 9. What I will not touch

- `client/` — no frontend changes in this pass.
- `tools/ReStore.Deploy*`, `deploy/compose.yml` (image-based, path-free) —
  only the `*.md` prose paths in §4.2.
- Any git action (no commit/push/branch) unless explicitly asked.
- `D:\Projects\AccountsApi` — read-only, as instructed.
