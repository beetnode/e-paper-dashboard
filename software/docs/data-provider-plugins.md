# Data provider plugins

Data providers are built-in modules. Home Assistant and Google use the same registry, source routing, and normalized data contracts. Adding a module requires rebuilding the application; there is no runtime package installer.

## Connections

Open **Connections** to create, rename, reconfigure, or remove named connections. Connections and credentials belong to the signed-in user and can be reused across that user's dashboards. The Home Assistant add-on retains its existing shared ingress identity, so its connections are shared by the people using that add-on.

A widget's source is resolved using the persisted dashboard owner, including device rendering and scheduled jobs. A source from another user's connection is rejected. Connection IDs and source IDs are separate: two servers can both expose `weather.home` without colliding. Deleting a connection leaves widget configuration intact and makes its sources unavailable until reconfigured.

Credentials and Google refresh tokens are encrypted using ASP.NET Core Data Protection. Back up `/data/DataProtection-Keys` along with the database. API responses contain only public settings and connection status; saved secret fields stay blank when editing. Blank secret fields retain their saved values. Updating Google OAuth settings requires connecting the Google account again. Connection changes invalidate the source and rendered-image caches.

LiteDB initializes the new connection collection automatically. PostgreSQL deployments must stop the app and run the existing `--maintenance` command to apply migration 3 before restarting.

### Home Assistant

Create a connection with a server URL, then either paste a long-lived access token from your Home Assistant profile or choose **Save and sign in** (or **Sign in** on a saved connection). Sign-in uses Home Assistant's login page, creates a long-lived token and stores it encrypted in the connection; the token never passes through the browser. Sign-in needs `CLIENT_URL` (or add-on ingress), and the server URL must be reachable from both your browser and izBoard. Changing the server URL discards a signed-in token unless you paste a new one. Each connection can point to a different Home Assistant installation.

Dashboards use connections only. Older versions stored a Home Assistant host and token on each dashboard; on startup izBoard moves them into named connections, named after the server (for example **Home Assistant (homeassistant.local:8123)**). Dashboards of one owner that share a server and token share one connection. The dashboard's widgets, conversation agent and, for Home Assistant dashboards, its rendering switch to that connection, and the stored settings are removed. Settings without a usable server or token are removed without a connection; select one for those dashboards. Use **Test** on a connection to check that it can list its sources.

In the Home Assistant add-on, Connections always lists a built-in **Home Assistant add-on** connection that reaches the add-on's own Home Assistant through the Supervisor. It provides widget data and Home Assistant conversation agents, and cannot be edited or removed. It cannot render **Home Assistant dashboard** mode, because the browser needs a long-lived token that the Supervisor cannot issue: add a Home Assistant connection with a long-lived access token (for example `http://homeassistant:8123`) and select it for those dashboards. When the add-on moves an older dashboard's token into a connection, it uses that server.

When creating a dashboard, choose its type, which cannot be changed later: **Custom layout** (widgets fed by any provider, edited in the designer) or **Home Assistant dashboard** (an existing Home Assistant dashboard captured by the rendering component). A Home Assistant dashboard references a named Home Assistant connection plus a dashboard path; the connection list refreshes when you return from a Connections tab. Removing a referenced connection makes the dashboard report an unavailable connection until another one is selected.

### Google Calendar and Tasks

Every user supplies their own OAuth client credentials; there is no administrator-wide Google client or shared Google account.

1. In your Google Cloud project, enable **Google Calendar API** and **Google Tasks API**.
2. Configure the OAuth consent screen and, while the app is in testing, add the Google account you will connect as a test user.
3. Create an OAuth client of type **Web application**.
4. Add the exact redirect URI shown on the Connections page to the client's authorized redirect URIs. It ends in `/api/providers/google/callback`. Use the same browser-facing installation origin from which you start authorization. The URL must use HTTPS; Google and izBoard allow HTTP localhost URLs for development. Keep any installation/ingress prefix shown in the URL. The device-only port cannot serve this callback.
5. Add a Google connection in izBoard with your client ID, client secret, and redirect URI. Save it, then select **Connect Google**. Allow the sign-in tab/pop-up and grant Calendar and Tasks access.
6. In the dashboard designer, select the connection's calendar or task list. Common Calendar and Todo widgets accept Google and Home Assistant sources. The Google category also provides Google-scoped Calendar and Tasks widgets.

Permissions are read-only: `calendar.calendarlist.readonly`, `calendar.events.readonly`, and `tasks.readonly`. Repeated calendar events are expanded by Google's API; all-day dates are preserved. Task lists and calendar lists are paginated. Authentication failures are reported as unavailable sources; reconnect from Connections to restore access. Google Keep is not included.

Authorization uses a short-lived state value encrypted with ASP.NET Core Data Protection, a browser correlation cookie, and PKCE. Nothing is kept in memory, so sign-in survives restarts and works across replicas that share the Data Protection key ring. Completing sign-in changes the connection's credentials, so a state value cannot be reused. Credentials and refresh tokens are persisted per user. OAuth test-mode refresh tokens may expire under Google's testing policy; reconnect or move your own OAuth application to production as appropriate.

Google documentation: [Web-server OAuth](https://developers.google.com/identity/protocols/oauth2/web-server), [Calendar scopes](https://developers.google.com/workspace/calendar/api/auth), [Tasks API](https://developers.google.com/workspace/tasks/reference/rest/v1/tasks/list).

## Extension API

Backend contracts live under `Services/Plugins`:

- `IDataProviderPlugin` supplies a manifest, credential validation, source discovery, and data fetching. `FetchManyAsync` can be overridden for efficient state/history batches.
- `ProviderManifest` describes credential fields and plugin widget registrations. Mark secrets with `CredentialField.Secret` so they never appear in API responses.
- `DataSource` exposes an opaque provider-local ID, a display name, and explicit capabilities. Compatibility is based on capabilities, not on a Home Assistant domain or source ID prefix.
- `SourceData` contains normalized `EntityState`, `CalendarEvent`, `TodoItem`, `WeatherForecast`, `HistoryState`, and `RssFeedEntry` values. Translate remote API formats inside the plugin. Weather state uses the shared weather attribute keys consumed by the common weather renderer.
- Widget compatibility in the designer comes only from capabilities; core widgets contain no provider-specific rules. A plugin may define its own capability for its own widgets.
- `ProviderContext` carries the dashboard identity and resolved connection. The router owns authorization; plugins receive only an owner-validated connection.
- `IOAuthDataProviderPlugin` is implemented by plugins whose connections support browser sign-in. Set `ProviderManifest.RequiresOAuth` when sign-in is mandatory (the registry rejects it without the interface); Home Assistant offers optional sign-in next to a pasted token. The core serves `POST /api/providers/connections/{id}/authorize` and the anonymous `GET /api/providers/{plugin-id}/callback`, binds the plugin's one-time state to the browser in an `izboard-{plugin-id}-oauth` cookie, and drops `AuthorizationCredentialKeys` whenever other credentials change, unless the same update supplies them. `IsAuthorized` drives the connection's status. Use `ProviderManifest.SetupInstructions` for provider-specific setup text shown on the Connections page.
- `IDefaultConnectionSource` supplies a deployment-provided default connection per user. It is listed with the user's connections but never stored, has no stored credentials, and cannot be updated, authorized or removed. Unqualified source IDs are routed to it, and its sources are discovered with unqualified IDs. The add-on registers one for its Supervisor-backed Home Assistant.

To add a plugin:

1. Implement `IDataProviderPlugin` in its own directory and register it with `AddSingleton<IDataProviderPlugin, YourPlugin>()` in a module extension. Register that module in `AddDataProviderPlugins`.
2. Declare credential fields and supported source capabilities. Fetch using the provided cancellation token. Keep secrets and raw upstream error bodies out of returned errors.
3. Add frontend widget definitions under `frontend/src/app/plugins/<plugin>/widgets.ts`, then include them in `plugins/provider-widgets.ts`. Each plugin widget has a namespaced type, `pluginId`, and `renderAs` pointing to a common widget. The backend manifest must register the matching `ProviderWidget(type, renderAs)`. Template variants reuse common configuration, preview, data planning, and native rendering. Bespoke widgets can instead supply their own components and native renderer as described below.
4. Add tests for credential validation, source discovery, normalization, capability compatibility, and failure behavior. The common routing tests cover ownership and connection identity.

Shipped plugins contribute data only: every widget is common and accepts any plugin's sources with the matching capability, so prefer adding capabilities over plugin-specific widgets. Add a plugin widget only for presentation that no common widget can provide, and keep in mind that dashboards are rendered at their update times, not in real time. Common widgets remain available independently of plugin-specific variants. For a bespoke widget, register an `IWidgetRenderer` from your plugin module and declare `ProviderWidget(type)` without `renderAs`. Its frontend definition supplies full sizing/defaults plus `configComponent` and optionally `previewComponent`. Configuration components receive `widget`, `dashboard`, `sources`, and an `onChange(updatedWidget)` callback. Preview components receive `widget`, `colorScheme`, `designerSettings`, and `data`. The current designer uses the native renderer for its canonical preview.

If bespoke data is required, override `GetWidgetDataRequest` to return a source reference, custom capability, and optional JSON parameters. Implement that capability in `FetchAsync`, returning `SourceData.Custom`. The shared pipeline performs owner and plugin checks, caches the response, and puts the JSON in `SsrData.WidgetData[widget.Id]` (also exposed as `widgetData` by the preview-data API). Errors appear under `widget:<widget-id>` in source statuses. Native renderers consume this snapshot without doing network access. This allows a new plugin to own data, configuration, and rendering without adding widget switches to the core.

Runtime JavaScript or assembly loading is outside this version's extension API.

The persisted `entityId` field is retained for layout compatibility. New values use the opaque `source:<connection-id>:<base64url-local-source-id>` format produced by `SourceReference`; clients should store and return these values unchanged. Bare entity IDs identify the default connection (the add-on's Home Assistant); without one they are unavailable.

## Validation

Run the .NET unit suite with a .NET 10 SDK and `-p:SkipAngularBuild=true`. Set `TEST_POSTGRES_CONNECTION_STRING` to a disposable PostgreSQL database to enable persistence integration tests. In the frontend run `npm test -- --watch=false` and `npm run build`. If the local LMDB Angular cache crashes on macOS, `CI=true` disables that local persistent cache without changing project configuration.
