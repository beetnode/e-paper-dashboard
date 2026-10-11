# Account setup and rendering isolation

## Intended behavior

- A new standalone/host installation opens a one-time service-administrator setup page.
- Ordinary registration never grants administrative permissions. Existing accounts and their dashboards remain intact.
- Setup completion is durable and checked atomically in LiteDB and PostgreSQL, including across PostgreSQL replicas.
- Home Assistant ingress keeps its existing identity and does not expose local account setup.
- The add-on has one shared rendering preference and queue; it does not isolate individual Home Assistant users.
- Each user can enable, test, retry, and disable browser rendering. Disabling affects only that account.
- Component files and lifecycle are shared per replica. Account preferences are stored in the database; each replica prepares its own matching component on demand.
- Owner validation covers previews and device-triggered rendering. Credentials and outputs remain scoped to a render job.
- Per-user FIFO queues are dispatched round-robin, with one active job per user and bounded per-user and replica queues.
- Each job retains the existing fresh worker/browser/context, private temporary directory, stripped environment, deadline, and cleanup.
- Ordinary users receive only rendering availability and their preference; detailed component status is administrator-only. Rendering failures do not return raw exceptions or credential-bearing URLs.

## Implementation sequence

1. Durable setup state and anonymous setup API; remove environment-based user creation.
2. Setup UI, ordinary registration validation, administrator landing page, deployment documentation.
3. User rendering preference/API/UI and trusted rendering owner propagation.
4. Fair scheduling, owner-specific cancellation, replica-local runtime preparation.
5. Regression tests for setup races, upgrades, ownership, disablement, scheduling, and cancellation; backend and frontend builds.

## Isolation boundary

Application-level isolation protects account state, credentials, result access, scheduling, and cancellation. The existing restricted renderer sidecar bounds the replica's aggregate resources. Processes sharing its OS identity are not separate hostile-tenant security boundaries. Per-job OS sandboxing and enforced per-job network egress require an additional deployment design; these must not be represented as guarantees of browser contexts or temporary directories.

## Compatibility

Existing accounts retain rendering access until explicitly disabled; new accounts opt in. Existing administrator accounts and owned resources are preserved. PostgreSQL deployments must run the new maintenance migration before starting updated replicas. Each replica needs a separate component directory and its own renderer sidecar/socket.

## Implementation and verification

The implementation sequence is complete. Verification covers 495 backend tests (including a disposable PostgreSQL instance), 23 frontend tests, the production frontend build, and the renderer worker build. Live API and browser checks cover first-run setup, regular registration, administrator-only operations, and independent rendering preferences. A real Home Assistant screenshot was not exercised; the disposable web instance used an unavailable component source to check preparation failure handling.
