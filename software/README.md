# izBoard Software

The server component of the izBoard system. An ASP.NET Core web application with an Angular frontend that renders dashboards into images optimized for E-Paper displays. It provides a visual dashboard designer, data connections to Home Assistant and Google Calendar & Tasks, device management, and scheduled updates.

## Features

- Two dashboard types: **Custom layout** (widgets edited in the designer) and **Home Assistant dashboard** (an existing dashboard view captured by the rendering component)
- Visual designer with a layers panel, inspector, searchable widget picker, grid-snapped drag and resize, position fields, and per-widget font sizes
- Provider-neutral widgets: every widget accepts compatible sources from any connection
- Built-in data provider plugins: Home Assistant and Google Calendar & Tasks, with health shown wherever sources are chosen
- Multiple named connections per user, shared across their dashboards, with Home Assistant sign-in or long-lived tokens
- AI generation from a plain-language prompt, for whole dashboards or individual widgets, through an OpenAI-compatible provider or a Home Assistant conversation agent; content can be regenerated ahead of scheduled updates
- E-Paper optimized image processing
- Device management, claim-code pairing, and OTA firmware delivery
- Configurable update schedules
- Multi-user accounts with one-time administrator setup (standalone mode)
- LiteDB storage by default, or PostgreSQL for hosted deployments
- Deployable as standalone Docker or Home Assistant Add-on
- Optional Home Assistant dashboard screenshots through an on-demand, sandboxed rendering component

## Data providers

Use **Connections** to configure your own Home Assistant and Google accounts. Common widgets can select compatible sources across providers. Existing Home Assistant dashboards remain compatible. See [provider setup and the plugin extension API](docs/data-provider-plugins.md) for Google OAuth setup, migration requirements, and adding plugins.

## Deployment

### Docker Compose

For a standalone deployment, copy `.env.example` to `.env` in the `software` directory and fill in
`CLIENT_URL` before starting. Set `CLIENT_URL` to an address reachable by both the display and
your Home Assistant OAuth browser; `localhost` is suitable only for local testing. The `.env` file
and any personal `docker-compose.yml` are ignored by Git. The tracked `docker-compose.base.yml`
contains no credentials and is used by the renderer deployment and manual test commands below.

```yaml
services:
  app:
    image: izdevdigital/e-paper-dashboard:<tag>
    ports:
      - "<port>:8128"   # Web UI and API
      - "<port>:8129"   # Device communication
    volumes:
      - <host-path>/data:/data:rw
    environment:
      - CLIENT_URL=http://dashboard.local:<port>
      - TZ=<time-zone>
```

### First-start accounts

Open the web UI and create the **service administrator** through the one-time setup page.
Complete setup before exposing a fresh installation to other people: the first successful setup request claims administration.
The administrator manages accounts and shared server components. Ordinary users register separately and never receive administrator permissions.
Existing accounts, administrator permissions, dashboards, and devices are preserved. Existing installations do not reopen setup,
even if their accounts are later deleted. The old superuser environment variables are no longer read or required.
Home Assistant add-on deployments continue using ingress authentication and disable local account setup and registration.
The add-on uses one shared identity: its rendering setting and queue apply to everyone accessing that add-on.

PostgreSQL upgrades require the maintenance migration described below; it records durable setup state and rendering preferences.

### Home Assistant Add-on

Install via the [izBoard Home Assistant Add-on repository](https://github.com/beetnode/hass-add-ons/tree/master/e-paper-dashboard). Configure `CLIENT_URL` as a device-reachable endpoint, normally `http://homeassistant.local:8129`. Remote URLs work through a direct port, VPN, or reverse proxy; ingress URLs do not.

The add-on image `izdevdigital/izboard-addon` is built from the `addon` target of `EPaperDashboard/Dockerfile` alongside the standalone image. It differs only in running as root (Supervisor mounts `/data` root-owned) and carrying Home Assistant labels.

### Environment Variables

| Variable | Required | Description |
|---|---|---|
| `CLIENT_URL` | Yes | Absolute HTTP or HTTPS URL reachable from the display network. In standalone/host mode it is also the Home Assistant OAuth client URL; in add-on mode it is the direct device endpoint. |
| `TZ` | Recommended | Timezone (e.g. `Europe/London`) |
| `APP_MODE` | No | Deployment mode: `standalone` (default) or `addon` |
| `DATABASE_PROVIDER` | No | `litedb` (default) or `postgresql`. Independent of `APP_MODE`. |
| `DATABASE_CONNECTION_STRING` | PostgreSQL only | Npgsql connection string for an existing PostgreSQL database. |
| `HOME_ASSISTANT_HOST` | No | Host mode only: Home Assistant URL used when moving older dashboard settings into connections (default `http://localhost:8123`) |
| `RENDERING_COMPONENT_REPOSITORY` | No | GitHub repository that publishes the optional rendering component (default: `beetnode/izBoard`) |
| `RENDERING_COMPONENT_RELEASE_TAG` | No | Override the component release tag; normally set by the official image |
| `RENDERING_COMPONENT_BASE_URL` | No | Direct component artifact source for manual deployment testing; bypasses GitHub release lookup |
| `RENDERING_COMPONENT_SOCKET` | No | Standalone app's private renderer Unix socket (default: `/run/izboard-rendering/renderer.sock`) |
| `RENDERING_LOG_WORKER_ERRORS` | No | Set to `1` on the renderer to log raw browser errors for troubleshooting. They can contain dashboard URLs and page text. Default: `0` |
| `RENDERING_MAX_CONCURRENT_RENDERS` | No | Concurrent worker limit, 1–16 (default: 2); increase only after measuring memory use |
| `RENDERING_MAX_QUEUED_RENDERS` | No | Waiting request limit, 0–1000 (default: 100) |
| `RENDERING_MAX_QUEUED_RENDERS_PER_USER` | No | Waiting request limit per user, 0–100 (default: 5) |

### Ports

| Port | Purpose |
|---|---|
| `8128` | Web UI and API |
| `8129` | Device communication (pairing, firmware image retrieval, and configuration) |

`CLIENT_URL` must describe the externally reachable URL after port mappings or a reverse proxy are applied. If displays are isolated on a VLAN, allow them to reach this URL. In standalone/host mode, expose the same URL to both the browser OAuth callback and the display API (normally through port `8128` or a reverse proxy). In add-on mode, use the exposed device port (`8129` by default), not the browser-only ingress URL.

For HTTPS, the display verifies the server certificate against its embedded trusted root bundle and must be able to obtain network time on its first connection. Deployments using a private certificate authority must add that root to the firmware bundle before flashing.

### Data

With the default LiteDB backend, the database is stored in `/data/epaperdashboard.db`. Uploaded images, firmware cache, configuration, and data-protection keys also live in `/data`. Mount this path as a persistent volume for either backend.

For a fresh PostgreSQL deployment, set `DATABASE_PROVIDER=postgresql` and `DATABASE_CONNECTION_STRING` in `software/.env` (or `/data/options.json`). For example:

```text
DATABASE_PROVIDER=postgresql
DATABASE_CONNECTION_STRING=Host=postgres.example;Database=izboard;Username=izboard;Password=replace-me
```

Create the database and user beforehand, then run maintenance before the first start and before each app upgrade. The maintenance database user needs permission to create and alter objects in its schema. Existing PostgreSQL databases created by earlier builds are recorded as schema version 1 without deleting their data. The app does not import LiteDB data; choose the backend before creating data. PostgreSQL stores users, dashboards, devices, and pairing sessions. `/data` remains required for files and data-protection keys. A shared PostgreSQL database alone does not make multiple app replicas safe: scheduled jobs run in each app process, and uploaded images and keys need shared storage.

#### PostgreSQL maintenance

Stop all app instances before running maintenance. For Docker Compose, select the new image version through `IZBOARD_APP_IMAGE`, then run these steps in order:

```shell
docker compose -f docker-compose.base.yml stop app
docker compose -f docker-compose.base.yml run --rm --no-deps app dotnet EPaperDashboard.dll --maintenance
# Run only after maintenance exits successfully:
docker compose -f docker-compose.base.yml up -d app
```

For a direct deployment, stop the service and run `dotnet EPaperDashboard.dll --maintenance` from the new version's directory with `DATABASE_PROVIDER` and `DATABASE_CONNECTION_STRING` configured. Restart the service only after exit code 0. Maintenance does not start HTTP listeners, background jobs, or initial account setup; it needs only database configuration. Exit code 1 means maintenance failed and the service must remain stopped until the issue is resolved.

Normal startup only checks the schema and refuses to serve requests if migrations are missing or the database belongs to a different app schema version. A database lock prevents maintenance while an app instance is running and prevents an app from starting during maintenance. Normal startup never applies migrations. LiteDB initialization is independent of maintenance.

PostgreSQL schema changes, including the first schema, use FluentMigrator's fluent API under `Data/PostgreSql`. Add a new migration with a unique, increasing `[Migration(...)]` version; do not edit migrations that have already shipped. Applied versions are stored in `izboard_schema_versions` in the connection's current schema. JSON document format changes may also require data updates or backwards-compatible deserialization.

### Optional rendering component

Standalone deployments run browser rendering in a restricted sidecar using the **same slim application image**. No separate
heavy image or additional image version needs to be selected: the browser, worker, and native libraries are still installed
from the UI. Start the standalone renderer with:

```shell
cd software
docker compose -f docker-compose.base.yml -f docker-compose.renderer.yml up -d --no-build app
```

This uses the published application image, not a local rebuild. For unpublished source builds use the local component test
stack below instead. The app and sidecar must use the same image build; set `IZBOARD_APP_IMAGE` to select a published image
(preferably an immutable digest) for both. Update/recreate
both containers together. A mismatched sidecar fails closed and **Settings → System** reports the component as not ready. Custom layouts do
not require the sidecar. Home Assistant add-ons retain local execution for their single trusted user.

The app writes component files into its existing persistent data volume. A one-shot storage initializer creates its `components`
directory, and the sidecar mounts **only that subdirectory, read-only**, preserving existing installations without exposing
the app database, tokens, configuration, or data-protection keys. This uses Docker volume subpath support; use a current Docker
Engine and Compose. The app communicates through a private Unix socket volume, never a public HTTP/browser-control port.
Each request carries its user's dashboard credentials; the sidecar validates the app build and manifest, starts a bounded,
sandboxed worker, and returns image bytes. It has no Docker socket, elevated capabilities, or app secrets, and has a read-only
root filesystem with limited temporary storage, CPU, memory, PIDs, and private shared memory.

The sandbox seccomp profile in `rendering-seccomp.json` is vendored from
[Microsoft's Playwright 1.62.0 profile](https://github.com/microsoft/playwright/blob/v1.62.0/utils/docker/seccomp_profile.json),
under its [Apache 2.0 license](https://github.com/microsoft/playwright/blob/v1.62.0/LICENSE), included as `rendering-seccomp.LICENSE`. It additionally permits the `chroot`
syscall required inside the browser's user namespace when all container capabilities are dropped; kernel permission checks still apply.
Sandbox creation must be permitted by the host kernel/security policy; unsupported hosts fail readiness rather than silently
disabling sandboxing. Do not use privileged mode or disable seccomp to bypass failures. The renderer needs network access to
users' Home Assistant servers: operators should restrict outbound access to approved destinations and block cloud metadata
and unrelated internal services. Container/browser isolation does not by itself provide an SSRF/network-access policy.

The default image does not include the browser-rendering runtime or browser binaries. Custom layouts work without them.
To render an existing Home Assistant dashboard, sign in with your own account, open **Settings → Rendering**, and select **Enable for my account**.
Apply the [e-paper theme](../home-assistant/README.md) to the captured view so it uses only the display colors.
In add-on mode, open the same page through Home Assistant ingress; the control applies to the whole add-on.
izBoard downloads the version-matched component for the current CPU architecture from the project's GitHub release, verifies its
SHA-256 checksum, performs a browser readiness test, and activates it immediately. The component is stored in the persistent `/data` volume, so it survives image
updates when `/data` is mounted as documented above. If izBoard is updated while the component is installed, izBoard automatically
updates the component to the exact application build as well. Custom layouts remain available while an offline update waits for
the matching component; Home Assistant screenshots are unavailable until it is ready. Removing the component waits for running
renders and deletes all of its files from `/data`. Activation atomically switches between version directories, and interrupted
installation/removal cleanup is completed on startup.

CI builds and publishes component archives, but does not run renderer deployment/browser tests. Each component has a retained,
version-addressed GitHub release (`rendering-v<major.minor.patch.build>`) published before its application image. Existing component
releases are never overwritten, so older installations do not depend on the mutable development release.
Developers manually verify deployments by setting `RENDERING_COMPONENT_BASE_URL` to a directory served over HTTP that contains the
architecture-specific `.tar.gz` archive and its `.sha256` file.

### Local component deployment test

Docker Compose can build the slim application and an unpublished rendering component for the host architecture, serve the
component inside the Compose network, and configure the application to install from that local source:

```shell
cd software
docker compose -f docker-compose.base.yml -f docker-compose.rendering.yml up --build app
```

Set `CLIENT_URL=http://localhost:8128` in your local `.env` for this test. Open `http://localhost:8128`,
complete administrator setup, then register a regular account. Open **Settings → Rendering** and select
**Enable for my account**, followed by **Test rendering**. Set `IZBOARD_VERSION` to override the default test version; the application
and component builds always receive the same value.

Before publishing, keep the pinned browser-runtime dependency current and manually verify installation, **Test component**,
a real Home Assistant dashboard (including authentication), and custom layouts. Old manifest/protocol revisions are replaced,
not treated as ready. Restart the app and confirm rendering still works. Rebuild with a different `IZBOARD_VERSION` and confirm the
installed component updates automatically. Repeat with the artifact server stopped: custom layouts must still work, and Home
Assistant rendering must recover after the matching artifacts become reachable. Test simultaneous requests, cancelled requests,
and removal during rendering; after removal inspect `/data/components` in the app container to confirm the files are gone.

Rendering uses one active browser job per user, FIFO within each user's queue, and round-robin dispatch between ready users.
The defaults are two active workers per replica, 100 pending jobs per replica, and five pending jobs per user.
Configure these using `RENDERING_MAX_CONCURRENT_RENDERS`, `RENDERING_MAX_QUEUED_RENDERS`, and
`RENDERING_MAX_QUEUED_RENDERS_PER_USER`. A zero queue limit permits immediately runnable jobs only.
An accepted job has a five-minute total deadline; runtime queue waiting and worker execution each have a 150-second deadline. Viewports are limited to 4096 pixels per dimension and 16 million pixels, HTML to one million characters, images to
64 MB, and worker responses to 64,000 characters. Workers receive only their request's credentials, not the app's environment secrets.

User rendering preferences are persisted in the database. New accounts start disabled; existing accounts retain access until they
explicitly disable it. Disabling cancels that account's queued and active work without removing shared files or draining other users' jobs.
Other replicas observe preference changes and account deletion through a one-second access check. A newly used replica prepares
its own matching runtime on the first render; the caller can retry once preparation completes. Replicas must have separate component
directories and private renderer sockets. User preferences are shared through PostgreSQL; queue limits are per replica, not cluster-wide.

The service administrator retains global component installation/removal and diagnostic controls under **Settings → System**. Removing a shared
component affects all users on that replica; an enabled user's next render can prepare it again. Use deployment configuration to take
a renderer offline for maintenance. Users only enable/disable their own capability and cannot remove shared files.

Fresh processes, browser contexts, temporary directories, stripped environments, and ownership checks provide application-level
separation. Worker processes still share the sidecar's OS identity and aggregate CPU/memory limits. This is not a per-job container,
hard per-user resource boundary, or enforced per-job network-egress policy. Hostile-tenant OS isolation requires additional deployment
sandboxing.

Only the single-user Home Assistant add-on retains an in-app-container compatibility worker with browser sandboxing disabled.
Standalone and host deployments require the isolated sidecar and browser sandboxing; they never fall back to compatibility mode.

Stop the stack and remove its isolated test data and generated component artifacts with:

```shell
docker compose -f docker-compose.base.yml -f docker-compose.rendering.yml down --volumes
```

## Building from Source

### Single Architecture Build

```shell
# <arch> can be: amd64 or arm64
docker build --platform linux/<arch> --build-arg VERSION=<version> -t izdevdigital/e-paper-dashboard:<version>-<arch> -f EPaperDashboard/Dockerfile .
```

### Multi-Architecture Build

Build and push for all supported platforms (requires docker buildx):

```shell
# Create a new builder instance (one-time setup)
docker buildx create --name multiarch --use

# Build and push multi-arch image (amd64 + arm64)
docker buildx build \
  --platform linux/amd64,linux/arm64 \
  --build-arg VERSION=<version> \
  -t izdevdigital/e-paper-dashboard:<version> \
  -t izdevdigital/e-paper-dashboard:latest \
  -f EPaperDashboard/Dockerfile \
  --push .
```
