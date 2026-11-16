# ReStore

A learning e-commerce application with an ASP.NET Core API and a React/Redux client.
The backend now follows the AccountsApi-inspired Domain / Application /
Infrastructure / Api layout, with MediatR CQRS and Mapster.

[Demo](https://restore.navidasadipur.ir) · [Architecture](docs/architecture.md) ·
[API contracts](docs/api-contracts.md) · [Migration plan and results](docs/cqrs-port-plan.md)

The demo link is an existing deployment; this refactor has not been verified or
deployed there. The initializer seeds `admin` and `alice` with password `Pa$$w0rd`
when creating the demo accounts. These are demo credentials, not a production account policy.

## Project layout

| Path | Responsibility |
| --- | --- |
| `SourceApi/ReStore.Domain` | Entities, domain responses and repository contracts |
| `SourceApi/ReStore.Application` | Commands, queries, handlers, Controls, DTOs and integration interfaces |
| `SourceApi/ReStore.Infrastructure` | EF Core/PostgreSQL, Identity, Mapster, Stripe, Cloudinary and optional cache/bus adapters |
| `SourceApi/ReStore.Api` | HTTP/MCP adapters, authentication, error policy and static hosting |
| `Test/ReStore.Test` | Backend slice, persistence, migration and HTTP-shell tests |
| `client` | React 18, TypeScript, Redux Toolkit, MUI, Axios, React Router and React Hook Form/Yup |
| `tools`, `deploy` | Separate deployment tools and server runbooks |

The API targets .NET 9; deployment tools target .NET 8. AutoMapper has been replaced
by Mapster. PostgreSQL is the runtime database; SQLite is used for relational tests.
Redis and RabbitMQ are not required: their no-op adapters are registered by default.

## Run locally

Use the .NET 9 SDK, PostgreSQL and Node.js/npm for the API/client workflow. The
Dockerfile currently pins Node 18 for its existing Create React App build and .NET 9
for the API; this phase does not upgrade runtimes or packages.

From the repository root in PowerShell, configure a local database and signing key:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=restore;Username=restoreuser;Password=<local-password>'
$env:JWTSettings__TokenKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
dotnet restore SourceApi/ReStore.Api/ReStore.Api.csproj
dotnet run --project SourceApi/ReStore.Api --launch-profile http
```

Replace the database placeholder with your local settings. Keep the signing key
stable if you want issued tokens to survive API restarts. Configuration may also
come from development user secrets; do not put real credentials in documentation.

The HTTP profile listens on `http://localhost:5000`. Startup attempts migrations and
initialization, logs database failures and continues; check `/health/db` before
expecting database-backed operations to work.
Readiness checks connectivity, not complete schema or payment-flow correctness.

In a second terminal:

```powershell
cd client
npm ci
npm start
```

The client runs at `http://localhost:3000`; its development API URL is
`http://localhost:5000/api/`. That origin is allowed by the API's credentialed CORS
policy. Requests carry the guest cookie and, when signed in, the Bearer token.

## Payment migration in progress

The demo catalog uses rial storage and toman display. Checkout uses the ZarinPal Sandbox. **API startup applies a catalog repricing
migration**; review [the migration decisions and upgrade notes](docs/zarinpal-migration-plan.md)
before running against an existing database. Existing demo orders are repriced to rials using the same sample pricing rule as the catalog.

## Configuration

| Environment variable | Used for |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection |
| `JWTSettings__TokenKey` | HS512 token signing and validation; use at least 64 bytes of signing material |
| `ZarinpalSettings__Mode` | `Sandbox` (default) or `Production`; validated at startup |
| `ZarinpalSettings__MerchantId` | Test UUID in Sandbox; merchant ID from the ZarinPal panel in Production |
| `ZarinpalSettings__CallbackUrl` | Absolute backend callback URL; Production requires HTTPS and a non-loopback host |
| `StripeSettings__WhSecret` | Verifying raw webhook payload signatures |
| `Cloudinary__CloudName`, `Cloudinary__ApiKey`, `Cloudinary__ApiSecret` | Product image storage |
| `Redis__Enabled`, `Redis__Configuration` | Optional Redis adapter, after explicitly switching its registration |
| `RabbitMq__Enabled`, `RabbitMq__Host` | Optional RabbitMQ adapter, after explicitly switching its registration |

ZarinPal settings are validated before database initialization. The checked-in configuration
uses Sandbox and a test UUID; startup logs only the selected mode. Invalid or missing
settings fail startup without including their values in the error. Configure real merchant
IDs through environment variables or development user secrets, not frontend code.
See [step 2 configuration examples](docs/zarinpal-migration-plan.md#step-2-configuration).
Checkout returns to the storefront at /checkout/result, which calls the API to verify
the payment. Set ZarinpalSettings__CallbackUrl to the storefront’s /checkout/result URL.
See [the Sandbox test instructions](docs/zarinpal-migration-plan.md#step-7-storefront-checkout).

## API and errors

[API contracts](docs/api-contracts.md) lists routes, inputs, response shapes and
pagination. Swagger is available at `/swagger` (`/swagger/v1/swagger.json`).
Business outcomes, including handled failures, use HTTP 200 envelopes. Branch on
`succeeded`, not HTTP status alone. `result` is a boolean for query envelopes and a
numeric enum for command envelopes.

`EnvelopeResponseAttribute` also opts business routes into binding/validation and
unhandled-exception envelopes. Authentication still uses 401/403, and Stripe
webhooks keep their acknowledgement/retry protocol. The intentional
`/api/buggy/*` error demonstrations are retained for the frontend's error UI.

| Health endpoint | Meaning |
| --- | --- |
| `GET /health/app` | Process liveness, independent of database connectivity |
| `GET /health/db` | PostgreSQL connection readiness; returns 503 when unhealthy |

Both are anonymous JSON endpoints. The current implementation does not register
`/health/live` or `/health/ready`.

The anonymous `/mcp` endpoint exposes `mcp_test` and six product tools:
`list_products`, `get_product`, `get_product_filters`, `create_product`,
`update_product`, `delete_product`. Business tools return their MediatR envelopes;
file upload remains an HTTP multipart operation. **The Admin policy on HTTP product
writes is not applied automatically to these anonymous MCP tools.** See the
[authorization boundaries](docs/architecture.md#authorization-boundaries) before
exposing this endpoint outside a trusted demo environment.

## Build and verify

From the repository root:

```powershell
dotnet build SourceApi/ReStore.Api/ReStore.Api.csproj --configuration Release
dotnet test Test/ReStore.Test/ReStore.Test.csproj --configuration Release
```

The full solution also contains deployment tools requiring the .NET 8 SDK. Their
independent suite is `dotnet test tools/ReStore.Deploy.Tests/ReStore.Deploy.Tests.csproj`.

From `client`, run client checks and an isolated production build:

```powershell
npm ci
npx tsc --noEmit
npm test -- --watchAll=false --runInBand
$env:BUILD_PATH = 'build'
$env:GENERATE_SOURCEMAP = 'false'
$env:CI = 'false'
npm run build
Remove-Item Env:BUILD_PATH
```

`CI=false` matches the Docker build's handling of existing lint warnings. The
isolated output is ignored `client/build`. Without the BUILD_PATH override,
`client/.env.production` writes to `SourceApi/ReStore.Api/wwwroot`. A plain
`dotnet build` does not rebuild the checked-in SPA snapshot: use the React dev
server or rebuild the client to see new frontend code. Docker rebuilds the client
from source and serves it in the API container.

Recorded implementation checks: 136 backend tests, 23 client tests, TypeScript and
production build passed. Client tests use controlled HTTP/Stripe responses; these
checks do not prove a live PostgreSQL/Stripe checkout or a deployment upgrade.
See the [release verification list](docs/architecture.md#remaining-verification).

## Deployment and project history

The [Dockerfile](Dockerfile) packages React and the API as one deployable unit.
Existing deployment instructions are in [deploy/DEPLOY.md](deploy/DEPLOY.md) and
[deploy/AGENT.md](deploy/AGENT.md). Their historical deployment records do not
verify this refactor on the server. Redis/RabbitMQ activation remains Phase 10 in
[the migration plan](docs/cqrs-port-plan.md).

Based on Neil Cummings' course,
[Learn to build an e-commerce store with .NET and React Redux](https://www.udemy.com/course/learn-to-build-an-e-commerce-store-with-dotnet-react-redux/).
