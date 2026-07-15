# Add API endpoint documentation (API.md)

> Issue: [#155](https://github.com/chA0s-Chris/ShadowDrop/issues/155)

## Rationale

ShadowDrop exposes roughly 15 HTTP endpoints across six endpoint groups, but while `docs/CLI.md`, `docs/DEPLOYMENT.md`, and `docs/SECURITY_TRADEOFFS.md` exist, the HTTP API surface itself is undocumented — anyone integrating directly against the API or reviewing the security surface has to read the endpoint classes. A curated `docs/API.md` orients readers with route, method, purpose, audience, and required authentication per endpoint, and doubles as a security-surface inventory: an explicit statement of which routes are anonymous by design, which require an upload credential, and which require the management key.

## Acceptance Criteria

- [x] `docs/API.md` exists and lists every HTTP endpoint of `ShadowDrop.Api` with route, HTTP method, a short purpose description, audience, and required authentication.
- [x] Endpoints are grouped by audience: health (ops/anyone), downloads (anonymous downloader, token-based), uploads and shares (uploader with upload credential), admin (management key).
- [x] Each group documents its authentication semantics: which header or credential is expected, and what a scoped upload credential can and cannot reach (owner-bound routes introduced with #154).
- [x] Request/response schemas are **not** duplicated in the document; it stays at the level of route, method, purpose, audience, and auth.
- [x] The documentation overview in `README.md` links to `docs/API.md`.
- [x] An automated drift-guard test asserts that every route pattern registered in the API appears in `docs/API.md`, so the document cannot silently go stale when endpoints are added. The guard checks route patterns only; HTTP methods are documented but verified by review.

## Technical Details

The document is derived from the six endpoint registration classes: `HealthEndpoints`, `DownloadEndpoints`, `UploadEndpoints`, `ShareEndpoints`, `AdminEndpoints`, and `UploadCredentialEndpoints` (all under `src/ShadowDrop.Api`). Group the document by audience as in the issue and keep each entry to a single table row or short paragraph; per-group prose covers the auth semantics.

Authentication facts to state per group, sourced from the existing security infrastructure rather than invented: upload and share routes accept an upload credential as `Authorization: Bearer` token via the endpoint filter applied by `UploadOrAdminBearerTokenEndpointFilterExtensions`; admin routes require the management key as `Authorization: Bearer` token via the endpoint filter applied by `AdminBearerTokenEndpointFilterExtensions` (both in `src/ShadowDrop.Api/Infrastructure/Security`); download routes are anonymous and authorize through the share token in the URL path, with client-supplied key material passed in the `ShadowDrop-Key` header (`DownloadKeyConstants.HeaderName`). The scoped-credential section should describe ownership enforcement on owner-bound routes (uploads/shares) at the level of "what can this credential reach", not implementation detail.

For the drift-guard test, add a test in `ShadowDrop.Api.Tests` that boots the API with a `WebApplicationFactory<Program>`, following the existing per-class factory pattern in that project (e.g. `TestApiFactory` in `ApiWalkingSkeletonTests`), resolves `EndpointDataSource` from the application's services, enumerates the `RouteEndpoint` route patterns, and asserts each raw route pattern text occurs in `docs/API.md`. Locating the file from the test can be done by walking up from `AppContext.BaseDirectory` to the repository root (or by linking the file into the test project as content) — the implementer is free to choose. The assertion should be direction-sensitive only from code to doc: every registered route must appear in the doc, while extra prose in the doc is fine. The guard matches route pattern text only — it does not pair routes with their HTTP methods, which stay a review concern. Normalize route templates (e.g. `{fileId:guid}`) so the doc can show them verbatim.

No production code changes are expected beyond the documentation and the test.
