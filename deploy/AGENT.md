# Install and operate the simple .NET Deployment Agent

The agent runs once and exits. It validates the Compose app image, pulls
`ghcr.io/navidasadipur/restore:production`, runs Compose for `app` only, waits
for its existing healthcheck, then checks that the public products API returns
HTTP 200 and a JSON array. Compose normally reuses an unchanged container.

There is no deployment state file, automatic rollback, failed-digest tracking,
or crash recovery. Failure returns a nonzero exit code; the systemd journal
contains the result. The next timer run tries again, including the same failed
production image. Inspect failures and perform rollback manually when needed.

The timer runs about 30 minutes after boot and 30 minutes after each service
run finishes. Use the systemd service for deployments: systemd does not start
a second instance of the same running service. There is no separate application
lock; do not launch the executable directly during a service run or run manual
Compose updates concurrently.

## Prerequisites and first installation

The VPS needs Docker Compose v2 with `up --wait`, systemd, and a glibc-based
Linux x64 host supported by .NET 8. The self-contained artifact includes its
.NET runtime; no separate SDK or runtime installation is required on the VPS.
The `runner` account needs Docker access and access to `/opt/restore/.env`.
The database and proxy must already be running. Buildx is not used by the agent.

Publish the CI changes first and verify the successful main build published
the production tag. Download that run's **restore-deploy-linux-x64** artifact,
extract its outer ZIP locally, and transfer its tarball plus the matching
service and timer files to a staging folder on the server. Do not install a
different commit's agent by accident.

From a staging folder containing the tarball and `deploy/` directory, as root:

```bash
uname -m
getconf GNU_LIBC_VERSION
sudo -H -u runner docker compose version
sudo -H -u runner docker manifest inspect ghcr.io/navidasadipur/restore:production

install -d -o root -g root -m 0755 /usr/local/lib/restore-deploy
tar -xzf restore-deploy-linux-x64.tar.gz -C /usr/local/lib/restore-deploy --no-same-owner
chown -R root:root /usr/local/lib/restore-deploy
chmod 0755 /usr/local/lib/restore-deploy/restore-deploy
install -o root -g root -m 0644 deploy/restore-deploy.service /etc/systemd/system/restore-deploy.service
install -o root -g root -m 0644 deploy/restore-deploy.timer /etc/systemd/system/restore-deploy.timer
systemd-analyze verify /etc/systemd/system/restore-deploy.service /etc/systemd/system/restore-deploy.timer
systemctl daemon-reload
```

Before the first deployment, back up the database and record the current
container's image reference for manual rollback:

```bash
sudo -H -u runner docker inspect restore-app --format '{{.Config.Image}}'
```

Record a known-working commit SHA tag and its repository digest, rather than
only the moving production tag. Rebuilding a commit can update its SHA tag;
the digest identifies exact content. Keep its local image until verification.

Set these values in `/opt/restore/.env` without replacing the other secrets:

```dotenv
RESTORE_IMAGE=ghcr.io/navidasadipur/restore
RESTORE_TAG=production
```

The agent requires the rendered app image to match that production reference.
It rejects existing `compose.override.*` or `docker-compose.override.*` files
so manual Compose and agent runs do not silently select different images.
If an earlier agent generated an override, review and archive it before using
this version. This repository's earlier agent was not installed on the VPS.

## Controlled first deployment, then timer

The following start command performs a real deployment, not a dry run:

```bash
systemctl start restore-deploy.service
systemctl status restore-deploy.service --no-pager
journalctl -u restore-deploy.service -n 60 --no-pager
```

A successful oneshot normally becomes `inactive (dead)` with a successful exit.
Confirm the container and API are healthy. Then enable the periodic check:

```bash
systemctl enable --now restore-deploy.timer
systemctl list-timers restore-deploy.timer --no-pager
```

To pause scheduling, use `systemctl disable --now restore-deploy.timer`. This
does not interrupt a running service; wait for it to finish before manual work.
To resume scheduling, use `systemctl enable --now restore-deploy.timer`.

## Manual rollback

Disable the timer, ensure the service has finished, then use the previously
recorded SHA tag. For the operator's original baseline that was `sha-51ce406`;
use the actual last verified tag if deployments have changed since then.

```bash
systemctl disable --now restore-deploy.timer
systemctl status restore-deploy.service --no-pager

cd /opt/restore
export RESTORE_TAG=sha-51ce406
docker compose pull app
docker compose up -d --no-deps --no-build --pull never --wait --wait-timeout 180 app
curl --fail --max-time 10 https://restore.navidasadipur.ir/api/products
unset RESTORE_TAG
```

Stop if the pull or up command fails; do not continue the sequence blindly.
For a digest baseline, set `RESTORE_IMAGE` to the repository and use a reviewed
Compose override manually, or select its corresponding verified SHA tag.
Do not re-enable the timer until `production` points to the intended healthy
release; otherwise the next run will deploy it again. Image rollback does not
undo database migrations. Container recreation can briefly interrupt requests.

## Build and test

```bash
dotnet test tools/ReStore.Deploy.Tests/ReStore.Deploy.Tests.csproj -c Release
dotnet publish tools/ReStore.Deploy/ReStore.Deploy.csproj -c Release -r linux-x64 --self-contained true -o artifacts/restore-deploy
```

Tests simulate Docker and API failures; live checks on the VPS are still needed.
The agent has command timeouts, a bounded public API request and a nonzero exit
code on failure. Docker/Compose stdout and stderr are not included in its error
messages because configuration diagnostics may contain secrets. Use manual
Docker diagnostics when investigating. Republish the self-contained artifact
after .NET runtime updates; never replace it during an active service run.
