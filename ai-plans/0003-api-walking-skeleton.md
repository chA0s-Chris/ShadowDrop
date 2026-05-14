## Rationale

Create the first runnable API skeleton so later vertical slices have a real host, configuration model, metadata store, storage root, and admin-token validation foundation.

## Acceptance Criteria

- [ ] `ShadowDrop.Api` starts as an ASP.NET Core application.
- [ ] API endpoints are implemented with Minimal APIs, not controllers.
- [ ] Serilog is configured as the API logging provider.
- [ ] A health endpoint is available without authentication.
- [ ] A protected management skeleton endpoint exists for verifying admin-token authentication.
- [ ] LiteDB is initialized from configuration.
- [ ] Blob storage configuration is bound, including a local filesystem storage root path.
- [ ] API configuration includes sections for LiteDB metadata, local storage root, and API exposure settings.
- [ ] `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` is read during startup or first-run initialization.
- [ ] Bootstrap admin token material is stored only in protected form, such as a cryptographic hash.
- [ ] Admin API requests can be validated with `Authorization: Bearer <admin-token>`.
- [ ] Download endpoints and upload/management endpoints are grouped so exposure can be configured separately later.
- [ ] API test dependencies include `Microsoft.AspNetCore.Mvc.Testing` for `WebApplicationFactory<T>`.
- [ ] Automated tests use `WebApplicationFactory<T>` to launch the application in-process.
- [ ] Automated tests cover startup/configuration behavior and admin-token validation through real HTTP requests.

## Technical Details

Keep the API organized by vertical slices instead of broad layers. It is acceptable to have small shared infrastructure areas for persistence, storage configuration, authentication, logging, and configuration, but endpoint behavior should remain grouped around workflows.

The walking skeleton should establish the host, configuration binding, Serilog setup, health endpoint, LiteDB connection setup, blob storage configuration shape, and admin-token validation. It should not implement full upload, share creation, download behavior, or the blob storage abstraction yet.

Add the Serilog packages needed by the API project as part of this plan, since this is the first plan that uses Serilog.

Configuration should include explicit sections for metadata persistence, local storage configuration, and API exposure settings. Suggested sections are `ShadowDrop:Metadata:LiteDbPath`, `ShadowDrop:Storage:LocalRoot`, and `ShadowDrop:ApiExposure`.

Use ASP.NET Core Minimal APIs for endpoint mapping. Do not introduce MVC controllers. Route groups should make the intended separation between public download routes and protected upload/management routes visible from the start.

The bootstrap token should come from `SHADOWDROP_BOOTSTRAP_ADMIN_TOKEN` because the default deployment target is a one-container Docker setup that should work well in tools such as Portainer. The plaintext bootstrap token must not be persisted. Store a hash or equivalent protected verifier in LiteDB. First-run bootstrap behavior is enough for this plan; token rotation can wait for a later management slice.

LiteDB usage should be limited to bootstrapping and validating admin token material. Do not introduce full metadata repositories yet.

Separate download routes from upload/management routes in endpoint mapping. The first version may still run in one process, but route groups and configuration should make it possible to expose public download endpoints while keeping management endpoints private or disabled on public listeners. Route groups may contain placeholder endpoints only; this plan should prove routing and authorization boundaries without implementing upload or download behavior.

API tests should use `WebApplicationFactory<T>` so the application is started through the ASP.NET Core hosting pipeline. Add `Microsoft.AspNetCore.Mvc.Testing` to the API test project through central package management. Structure `Program` so the test project can reference it as the factory entry point. Tests should override configuration to use temporary filesystem paths and isolated LiteDB database files. Admin-token validation should be tested through HTTP calls against a protected skeleton endpoint rather than by directly invoking helper classes.

Do not implement the blob storage abstraction in this plan. The walking skeleton should only establish the configuration shape for the default local filesystem backend so later upload and download slices can plug in the actual storage implementation.
