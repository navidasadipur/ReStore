# Deploying ReStore on the VPS (Docker + Caddy + GHCR)

The manual runbook. Everything here has been executed on the server once; the
commands are what actually ran, not a guess.

---

## 0. Status right now (2026-10-13)

**Live:** <https://restore.navidasadipur.ir> and <https://www.restore.navidasadipur.ir>,
serving image `ghcr.io/navidasadipur/restore:sha-51ce406`.

Verified end to end: Let's Encrypt certificate issued for both names, SPA served,
`/api/products` returns the 18 seeded products, `POST /api/account/login` with
`admin` / `Pa$$w0rd` returns a JWT, and `/api/orders` returns 401 anonymously.

| Piece | State |
|---|---|
| `navidasadipur.ir`, `nimdoor.ir` | untouched, still HTTP 200 |
| `/opt/restore` (app + Postgres) | running; DB healthy; EF migrations applied |
| `/opt/personal-site` | only `caddy` was recreated; originals kept as `*.bak-before-restore` |
| Deploy files | branch `deploy/docker-cicd`, not yet merged to `main` |
| Auto-deploy on push | **not done yet** - see section 7 |

---

## 1. The shape of the deployment

ReStore is **one deployable unit**, not two apps. The React client is compiled to
static files, those files go into the API's `wwwroot`, and the ASP.NET Core API
serves them (`UseStaticFiles` + `MapFallbackToController` in `API/Program.cs`).
So there is one image, one `app` service.

```
Internet
   |
   v
Caddy (ports 80/443, container "caddy", project /opt/personal-site)
   |
   |  network: restore-net
   v
restore-app:8080          <- one container: React static files + .NET 7 API
   |
   |  network: restore-net
   v
db (postgres:16-alpine, volume restore-pgdata)
```

| Piece | Where |
|---|---|
| Compose project for the app + DB | `/opt/restore/compose.yml` |
| Secrets / config for it | `/opt/restore/.env` (mode 600, owner `runner`) |
| Shared Docker network | `restore-net` (created by that project, explicitly named) |
| Database volume | `restore-pgdata` |
| Reverse proxy | `/opt/personal-site/compose.yml` + `Caddyfile` |
| Image registry | `ghcr.io/navidasadipur/restore` |

Neither the app nor the DB publishes a host port. Caddy reaches the app by
container name over `restore-net`; the app reaches Postgres by container name
over the same network. Nothing about the database is reachable from the
internet - publishing 5432 would get it brute-forced within hours.

---

## 2. Why the image is NOT built on the server

The NavidAsadipurWebsite pattern builds on the self-hosted runner on this server.
That works there because it has no npm build. **It does not work for ReStore.**

A React 18 + MUI production build needs more than 1 GB of heap. The VPS has
967 MB of RAM and 1 vCPU, so webpack spends all its time in swap: the attempt
made here burned 17 minutes at ~20% CPU and never reached minification, and it
had already died once with `FATAL ERROR: Reached heap limit - JavaScript heap
out of memory`.

So the image is built by `.github/workflows/build.yml` on a **GitHub-hosted
runner** (free, because this repository is public) and pushed to GHCR. The
server only pulls the finished image. That is also the architecture the CI/CD
phase wants, so nothing is thrown away later.

A 2 GB swapfile was added on the server anyway (`/swapfile`, in `/etc/fstab`,
`vm.swappiness=10`). Not for builds - a 1 GB box running Postgres with zero swap
is a bad idea on its own.

---

## 3. Deploy it

### 3.1 Build and push (this is the "CI" half)

Push to `main` (or to a branch listed in the workflow's `on.push.branches`), or
run **Actions -> Build and Push -> Run workflow**. The job prints the exact
deploy command in its summary.

> Nothing has been merged to `main` yet - the deploy files live on the branch
> `deploy/docker-cicd`. The workflow lists that branch too, so pushing to it
> builds and pushes an image without touching `main`.

### 3.2 Pull and run on the server

```bash
cd /opt/restore

export RESTORE_TAG=sha-<7-char-sha>      # from the workflow summary
docker compose pull app
docker compose up -d app
docker compose ps
```

`docker compose up -d app` recreates the app and nothing else. The database
container and its volume are untouched by an app deploy.

### 3.3 First-time only: make Caddy able to see the app

`restore.navidasadipur.ir` and `www.restore.navidasadipur.ir` must already
resolve to `94.184.36.73` (they do) before Caddy can request a certificate.

**a.** Attach Caddy to `restore-net`. In `/opt/personal-site/compose.yml`, the
`caddy` service gets two networks and the file gets a top-level `networks:`
block:

```yaml
  caddy:
    # ... existing keys ...
    networks:
      - default
      - restore-net

networks:
  restore-net:
    external: true
```

**b.** Add the site block to `/opt/personal-site/Caddyfile` (see
`deploy/Caddyfile.snippet`):

```
restore.navidasadipur.ir, www.restore.navidasadipur.ir {
    encode zstd gzip
    reverse_proxy restore-app:8080
}
```

**c.** Recreate Caddy and reload:

```bash
cd /opt/personal-site
docker compose up -d caddy
docker compose exec caddy caddy reload --config /etc/caddy/Caddyfile
```

`docker compose up -d caddy` names the service explicitly, so `navid-website`
and the other domains on this stack are not recreated.

### 3.4 Verify

```bash
cd /opt/restore
docker compose ps                 # restore-db healthy, restore-app healthy
docker compose logs -f app        # watch it start and run EF migrations
curl -I https://restore.navidasadipur.ir
```

---

## 4. Operating it

```bash
cd /opt/restore

docker compose ps                      # what is running
docker compose logs -f app             # follow the API
docker compose logs db                 # Postgres

# Back up the database. The Docker volume is NOT a backup.
docker compose exec -T db pg_dump -U restoreuser restore | gzip > ~/restore-$(date +%F).sql.gz

# Restore
gunzip -c ~/restore-2026-10-13.sql.gz | docker compose exec -T db psql -U restoreuser -d restore
```

Read or change a secret:

```bash
sudo -u runner cat /opt/restore/.env      # or: sudo -u runner nano /opt/restore/.env
```

### Rolling back

Every push produces a `sha-<7>` tag. Point at an older one:

```bash
cd /opt/restore
export RESTORE_TAG=sha-<older-sha>
docker compose pull app
docker compose up -d app
```

Migrations run forward automatically on startup (`MigrateAsync` in `Program.cs`)
and are **not** undone by an image rollback. If a deploy shipped a destructive
migration, restore the database dump too.

---

## 5. Things that bit us (so they don't bite you again)

| Symptom | Cause | Fix |
|---|---|---|
| `JavaScript heap out of memory` while building | React build on a 1 GB box | Build on a hosted runner (what we do now). If you ever build locally, `NODE_OPTIONS=--max-old-space-size=1024` is already set in the Dockerfile |
| `apt-get install curl` fails with `404 Not Found` from `deb.debian.org/debian-security` | The .NET 7 image is Debian 11; that mirror's index and pool were out of sync | The Dockerfile no longer installs anything. The healthcheck uses bash's `/dev/tcp`, and `adduser` is already in the base image |
| `COPY --from=client-build /client/build` fails | `client/.env.production` sets `BUILD_PATH=../API/wwwroot`, so create-react-app wrote outside `/client` | The Dockerfile sets `BUILD_PATH=build` as a real environment variable, which wins over the `.env` file |
| App starts but never creates tables | `MigrateAsync` failures are caught and logged, not fatal, so the app runs happily against an empty database | `depends_on: db: condition: service_healthy` - the DB is up before the app starts |
| Deploying ReStore restarts `navid-website` | Running `docker compose up -d` with no service name in a shared project | Always name the service: `docker compose up -d app` / `up -d caddy` |
| `docker compose` in `/opt/restore` complains a variable is missing | `.env` was not read, or is not readable by the user running compose | `.env` must be `600` and owned by `runner`; run compose as `runner` or `root` |

---

## 6. Security follow-ups (not yet done)

These are real and should be handled before the site is advertised:

1. **`API/appsettings.Development.json` is committed to git** with live Stripe
   test keys and a Cloudinary API secret. It is excluded from the image via
   `.dockerignore`, so it never reaches production - but it is in the repository
   and in the git history. Rotate those keys and remove the file from tracking.
2. **Seeded users.** `API/Data/DbInitializer.cs` creates `admin` and `bob` with
   the password `Pa$$w0rd`, which is printed in the README. On a public URL that
   is an open admin account. Change it before going live.
3. **.NET 7 reached end of life in May 2024.** The images are pinned so nothing
   breaks today, but there are no security patches. Plan an upgrade to .NET 8.
4. The Stripe webhook secret in `.env` is the *test* one. Register
   `https://restore.navidasadipur.ir/api/payments/webhook` in the Stripe
   dashboard and put the real signing secret into `/opt/restore/.env`.
5. The Stripe **publishable** key is hardcoded in
   `client/src/features/checkout/CheckoutWrapper.tsx`. It is a build-time value,
   so changing it means rebuilding the image.

---

## 7. Simple pull-based deployment: progress and next steps

The .NET Deployment Agent in `tools/ReStore.Deploy` runs Compose every
**30 minutes**, through a systemd service and timer. It pulls the `production`
image, updates only `app`, waits for container health, and checks the public
products API. No inbound SSH from GitHub or additional self-hosted runner is
needed. There is no custom state file, automatic rollback, failed-digest
tracking, crash recovery, or generated Compose override. Rollback is manual.

### Verified so far

- The operator successfully pulled the baseline as the deployment user:
  `sudo -H -u runner docker pull ghcr.io/navidasadipur/restore:sha-51ce406`.
- The .NET agent, tests, systemd files and instructions are prepared locally.
- The agent and timer have not been installed or live-tested on the VPS.

The original working baseline was `sha-51ce406`, with repository digest:

```text
sha256:4aa156c35df5b757104ed772a834dc3131d410f92edd5957ea8b7a0c4bc75286
```

### CI publication contract

The workflow builds on pushes to `main`, `develop`, and `deploy/docker-cicd`,
or manual runs. It tests the .NET deployment tools, publishes the self-contained
Linux agent artifact, then builds and pushes the application image with full
and legacy short commit SHA tags. Only a successful current-main run updates
`production` and `latest`. Main runs are serialized and stale commits skip
promotion. The summary reports the exact build digest and promotion result.
There are no application test suites; these checks do not prove runtime health.

SHA tags identify source commits but can change on rebuild; digest references
identify exact image content. This simple agent uses the mutable production tag
and Compose's local image handling instead of custom digest resolution/pinning.
A failed pull does not proceed to `up`; a failed health check reports failure
without automatic rollback. The next scheduled run can retry the same release.
A green CI run means publication succeeded, not deployment to the VPS.

### Remaining steps

1. Push to `develop` and confirm the Linux tests, artifact and image build pass
   while production is unchanged.
2. Merge to `main` and verify production publication.
3. Follow [AGENT.md](AGENT.md): check server prerequisites, record the current
   image and back up the database, install the agent and set `.env` to use
   `RESTORE_TAG=production`.
4. Run one controlled deployment through the service and inspect the logs/API.
5. Verify manual rollback and unchanged-image behavior, then enable the
   30-minute timer. Use systemd to pause/resume scheduling and avoid concurrent
   manual deployment commands. Existing runners for other repositories stay.

Image rollback does not undo database migrations. Keep schema changes backward
compatible and maintain database backups. The unit tests do not replace these
live server checks.
