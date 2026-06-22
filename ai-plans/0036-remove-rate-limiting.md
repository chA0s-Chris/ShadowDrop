## Rationale

Remove the basic in-process rate limiting introduced for the admin upload endpoint because it is not configurable,
covers only one endpoint, and is not needed for the MVP. Deployments can enforce rate limits at the reverse proxy
until a broader, configurable design is revisited. This plan addresses
[#36](https://github.com/chA0s-Chris/ShadowDrop/issues/36).

## Acceptance Criteria

- [x] The rate-limiting services, middleware, upload policy, and the `RateLimiting.cs` implementation are all removed
  from the API.
- [x] The admin upload endpoint no longer has a rate-limiting policy and repeated valid upload requests are processed
  normally instead of returning `429 Too Many Requests` from ShadowDrop.
- [x] Automated tests are updated to remove the throttling expectation and verify the resulting upload behavior.

## Technical Details

Remove the rate-limiter configuration call from `DependencyInjection.ConfigureServices`, the `UseRateLimiter` call
from `Middleware.ConfigureMiddleware`, and the `RequireRateLimiting` metadata from the upload route in
`AdminEndpoints`. Delete the now-unused `CompositionRoot/RateLimiting.cs` implementation rather than leaving dormant
MVP configuration behind.

Replace the rate-limit-specific walking-skeleton test (`UploadRoute_ShouldReturn429_WhenRateLimitIsExceeded` in
`ApiWalkingSkeletonTests.cs`) with coverage demonstrating that more than three valid uploads from the same client
remain successful. Keep authentication, upload validation, persistence, and error-response
behavior unchanged; this issue only removes application-level throttling.
