# Install and operate the ReStore pull deployer

The deployer checks `ghcr.io/navidasadipur/restore:production`, resolves its
registry digest, pulls that exact image, and updates only `app`. A systemd timer
checks 30 minutes after boot and 30 minutes after the previous service run ends.
The .NET console application exits after each run. Its Linux x64 artifact is
self-contained: the VPS needs neither a .NET SDK nor a separately installed
.NET runtime. It requires Docker Compose v2, Buildx, systemd, a glibc-based Linux
host supported by .NET 8, and the existing `runner` account with Docker access.
The existing application's target framework is unchanged.

## Before installation

Publish the CI changes and verify that a successful **current main** build has
published `production`. Installing files does not publish that channel. Keep
the current database backup and baseline image; do not prune rollback images.
Image rollback cannot undo database migrations. The app is briefly unavailable
while its single container is recreated; this is not a zero-downtime deployment.

Run on the VPS as root:

```bash
uname -m
getconf GNU_LIBC_VERSION
sudo -H -u runner docker compose version
sudo -H -u runner docker buildx version
sudo -H -u runner docker buildx imagetools inspect ghcr.io/navidasadipur/restore:production
```

Confirm `/opt/restore/compose.yml` and `.env` exist, are usable by `runner`, and
the app and DB are already running. The deployer refuses an existing unmanaged
Compose override. Review any `compose.override.yml`, `compose.override.yaml`,
or `docker-compose.override.*` before proceeding; do not overwrite it.

## Install, initially paused

Download the **restore-deploy-linux-x64** artifact from the successful, reviewed
GitHub Actions run for your commit. Extract its outer artifact ZIP on your own
computer; it contains `restore-deploy-linux-x64.tar.gz`. Transfer that tarball
and the matching `deploy/restore-deploy.service` and `.timer` files to a staging
folder on the VPS using your existing access. The artifact must match the
reviewed commit; the app's GHCR image is separate from this agent artifact.

The CI builds and tests C# using the .NET 8 SDK and publishes a self-contained
Linux x64 folder inside the tarball. Preserve the full folder, including its
runtime libraries. From a staging folder containing the tarball and `deploy/`
directory, run as root for this first installation:

```bash
install -d -o root -g root -m 0755 /usr/local/lib/restore-deploy
tar -xzf restore-deploy-linux-x64.tar.gz -C /usr/local/lib/restore-deploy --no-same-owner
chown -R root:root /usr/local/lib/restore-deploy
chmod 0755 /usr/local/lib/restore-deploy/restore-deploy
install -o root -g root -m 0644 deploy/restore-deploy.service /etc/systemd/system/restore-deploy.service
install -o root -g root -m 0644 deploy/restore-deploy.timer /etc/systemd/system/restore-deploy.timer
systemd-analyze verify /etc/systemd/system/restore-deploy.service /etc/systemd/system/restore-deploy.timer
systemctl daemon-reload
```

Initialize from the **currently running, verified** image. The following digest
is the baseline confirmed by the operator; if the app has changed since then,
use its actual verified GHCR repository digest instead:

```bash
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy init \
  --image ghcr.io/navidasadipur/restore@sha256:4aa156c35df5b757104ed772a834dc3131d410f92edd5957ea8b7a0c4bc75286
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy status
```

Initialization validates the actual container image ID, Docker health, the
container's `/api/products` response (which queries the database), and the public
HTTPS API's JSON response. It creates a persistent `compose.override.yml` pinned
to that digest, plus `.deploy/state.json`. **It does not recreate any container
and leaves automatic deployment paused.** Do not rerun init after success.

The override is automatically picked up by ordinary `docker compose` commands
in `/opt/restore`. Once initialized, changing `RESTORE_TAG` alone no longer
changes the app image: operate through this tool. Preserve the override and
state together. If initialization is interrupted between writing the override
and state, inspect both files and the actual running image before repairing;
do not delete state from an established installation.

## First controlled deployment

After publication, baseline validation, and a database backup:

```bash
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy resume
systemctl start restore-deploy.service
systemctl status restore-deploy.service --no-pager
journalctl -u restore-deploy.service -n 60 --no-pager
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy status
```

Confirm the new exact digest is current, `pending` is null, and the prior image
is recorded as `previous`. Then enable polling:

```bash
systemctl enable --now restore-deploy.timer
systemctl list-timers restore-deploy.timer --no-pager
```

The service's HOME is `/home/runner`; existing Docker credentials there remain
in use. Buildx metadata is kept under `/opt/restore/.deploy/buildx` so the service
does not need to write to the user's home. No additional registry token was
needed in the operator's baseline pull test. Keep the journal available for
deployment failures; CI success only reports image publication.

## Operation and recovery

```bash
# Pause automatic deployments (waits are not used; a busy lock returns an error).
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy pause

# Inspect recorded current/previous versions and last deployment error.
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy status

# Roll back to the previous verified local image and leave automation paused.
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy rollback

# Explicitly resume after investigating; failed digests stay blocked.
sudo -H -u runner /usr/local/lib/restore-deploy/restore-deploy resume

# Check immediately (respects pause and failed-digest suppression).
systemctl start restore-deploy.service

# Disable future scheduling; does not interrupt an active deployment.
systemctl disable --now restore-deploy.timer
```

All control and deployment commands share a nonblocking file lock. If busy,
inspect the service and retry after it finishes. Use the service to run checks
so output goes to the journal. A healthy oneshot service normally becomes
`inactive (dead)` with a successful exit after finishing.

Registry failure or failed download preserves the running app. A failed deploy
restores and health-checks the previous image; its failed digest is blocked on
future checks. Publish a corrected image with a new digest to proceed. If both
deploy and rollback fail, the tool pauses and retains a pending journal.

After interruption/reboot, the next `check` recovers a pending deployment back
to its prior image and leaves automation paused. Recovery runs even while
paused; `status` and `pause` themselves do not initiate recovery. Investigate
logs and use `resume` explicitly after recovery. Persistent state and atomic
file replacement protect restart recovery, but do not replace database backups.

Validate successful deployment, unchanged-image no-op, registry outage, failed
release rollback, and reboot recovery on the VPS before relying on automation.
The repository unit tests mock Docker/network calls and do not replace these
live checks or `systemd-analyze verify` on the actual server.

## Build and test the tools locally

With a .NET 8 SDK or a compatible newer SDK:

```bash
dotnet test tools/ReStore.Deploy.Tests/ReStore.Deploy.Tests.csproj -c Release
dotnet publish tools/ReStore.Deploy/ReStore.Deploy.csproj -c Release -r linux-x64 --self-contained true -o artifacts/restore-deploy
```

The agent uses only .NET libraries at runtime; xUnit dependencies are confined
to the test project. Tests use fake Docker/HTTP operations and temporary state
files. SSH probe tests use a local loopback TCP listener. CI runs the same tests
on Linux, including the real `flock` exclusion test. After runtime security
updates, republish and replace the self-contained agent while its timer is
disabled and no deployment is running. The VPS still needs the usual native
Linux runtime dependencies and CA certificates.
