# Harden admin endpoint exposure against unauthenticated PBKDF2 amplification

> Issue: [#99](https://github.com/chA0s-Chris/ShadowDrop/issues/99) (part of umbrella [#97](https://github.com/chA0s-Chris/ShadowDrop/issues/97))

## Rationale

Every request to `/api/admin/*` that carries a syntactically valid bearer token reaches
`AdminTokenService.IsValidToken`, which runs the configured PBKDF2 token hash before rejecting
invalid credentials. Without an upstream rate limit or network exposure boundary, unauthenticated
callers can turn invalid admin requests into cheap CPU amplification.

Issue #36 deliberately removed ShadowDrop's in-process rate limiting in favor of reverse-proxy
enforcement. This plan preserves that direction: admin operations remain protected by the existing
bearer-token check, but deployments get explicit hardening guidance and configuration support for
keeping admin routes off the public interface or behind upstream request controls.

## Acceptance Criteria

- [ ] Deployment documentation explains that `/api/admin/*` must not be exposed directly to the
  public Internet without upstream rate limiting or equivalent access controls, because invalid
  bearer tokens still trigger PBKDF2 verification work.
- [ ] The documented hardening path covers both recommended options: disable admin operations with
  `ShadowDrop__ApiExposure__EnableAdminOperations=false` when the server is download-only, or expose
  admin operations only on a trusted management boundary such as loopback, VPN, private network, or
  reverse-proxy-protected route.
- [ ] The documentation includes concrete reverse-proxy guidance for request throttling on
  `/api/admin/*` and makes clear that the ShadowDrop application currently relies on this layer
  rather than an in-process limiter.
- [ ] Configuration samples and appsettings comments or equivalent docs make the default
  `EnableAdminOperations=true` behavior explicit so operators must consciously decide how admin
  routes are exposed.
- [ ] Automated tests verify that disabling admin operations leaves `/api/admin/*` unmapped and
  avoids resolving `AdminTokenService` for those routes, so the documented download-only hardening
  path does not execute PBKDF2 on admin requests.
- [ ] No application-level rate limiter is reintroduced unless the project explicitly revisits the
  issue #36 decision.

## Technical Details

Add first-pass deployment hardening documentation rather than changing request-processing semantics
by default. Add a focused deployment document under `docs/` (alongside `docs/ARCHITECTURE_DECISIONS.md`)
covering "Admin endpoint exposure" and link it from the README, which is currently a placeholder.
The text should call out the exact risk path:
`AdminBearerTokenEndpointFilterExtensions.RequireAdminBearerToken` parses bearer credentials and
`AdminTokenService.IsValidToken` performs PBKDF2 verification for invalid non-empty tokens.

Document two supported mitigations. For download-only deployments, set
`ShadowDrop__ApiExposure__EnableAdminOperations=false`, which prevents `AdminEndpoints.MapAdminEndpoints`
from mapping `/api/admin/*`. For deployments that need upload/share administration, keep admin
operations enabled but bind them to a management-only boundary or place `/api/admin/*` behind a
reverse proxy that enforces source restrictions and rate limits before requests reach Kestrel.
Include concise examples or pseudo-config for common proxy behavior without committing the project
to one specific proxy product.

Keep runtime changes narrowly scoped. Do not re-add ASP.NET Core rate-limiting middleware as part of
this issue. If a small configuration or sample-file update is needed to make the documentation
discoverable, keep it declarative and avoid changing the default local-development experience. The
existing `ApiWalkingSkeletonTests.ManagementEndpoint_ShouldNotBeMapped_WhenAdminOperationsExposureIsDisabled`
already covers the disabled mapping behavior; extend or add focused coverage proving that a disabled
admin route returns not found without constructing `AdminTokenService` or doing token verification.
Because the composition root only registers `AdminTokenService` when `EnableAdminOperations` is true
(`DependencyInjection` gates `AddSingleton<AdminTokenService>()` and `Startup` skips its warm-up),
the test can assert `app.Services.GetService<AdminTokenService>()` is null when admin operations are
disabled, alongside the not-found response, to prove the PBKDF2 path is unreachable.
