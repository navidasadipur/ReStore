# Test GitHub-hosted SSH access

This manual test runs on GitHub's `ubuntu-latest`, not on the VPS. It does not
install a runner, build an image, pull an image, or restart containers.

## 1. Test the network first (no secrets)

1. Publish `.github/workflows/test-ssh.yml` to the repository's default branch.
   GitHub requires a `workflow_dispatch` workflow to exist on the default branch
   before it can be run manually from Actions.
2. Open **Actions → Test hosted runner SSH → Run workflow**.
3. Confirm the server IP and SSH port. The default IP, `94.184.36.73`, comes from
   the existing deployment runbook; change it if needed.
4. Run and open **Test network and SSH banner** and the run summary.

The test makes three connections, each with bounded timeouts:

- **3/3 SSH responses:** this GitHub runner can reach the server's SSH service.
  Proceed to the authentication test.
- **Timeout:** possible network filtering, routing trouble, or an unavailable
  server. This alone does not identify which network is blocking traffic.
- **Connection refused:** the port was actively rejected; check the SSH port,
  service, and firewall.
- **TCP connected but no SSH response:** the endpoint did not complete the
  expected SSH banner exchange.
- **Partial success:** connectivity is intermittent; do not automate deploys yet.

A successful run proves connectivity from that runner at that time. Repeat on
separate runs before relying on it because GitHub-hosted runner addresses vary.

## 2. Optionally test login and Docker access

In **Settings → Secrets and variables → Actions**, add all three repository secrets:

| Secret | Value |
|---|---|
| `SSH_USER` | The deployment account, normally `runner` |
| `SSH_PRIVATE_KEY` | Dedicated unencrypted deployment private key, including its BEGIN/END lines |
| `SSH_KNOWN_HOSTS` | Verified OpenSSH known_hosts entry for this server and port |

Install the matching public key in the deployment account's `authorized_keys`
through your existing server access. Obtain and verify the host key through a
trusted server connection or console; do not trust a key solely because this
new network path returned it. For a nonstandard port, the known_hosts host field
must use `[hostname]:port` (matching the host input).

Run the workflow again. It will authenticate with strict host key checking,
check Docker daemon access, validate `/opt/restore`'s Compose configuration
without printing its secrets, and show the app service status.

Both stages passing makes hosted-runner SSH deployment a viable next step.
It does **not** yet prove GHCR image-pull credentials, a successful app restart,
or application health. Test a chosen immutable image and its health gate when
implementing actual deployment. Leave existing runners in place until that
end-to-end deployment succeeds.
