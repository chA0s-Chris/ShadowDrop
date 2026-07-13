# Deployment Hardening

ShadowDrop's admin endpoints live under `/api/admin/*`. Do not expose those
routes directly to the public Internet unless an upstream control limits who
can reach them and how quickly requests are forwarded to the application.

## Admin Endpoint Exposure

Admin operations are protected by the configured admin bearer token, but a
syntactically valid non-empty bearer token still reaches the token verifier
before it can be rejected. The request path is:

1. `AdminBearerTokenEndpointFilterExtensions.RequireAdminBearerToken` parses the
   `Authorization: Bearer ...` header.
2. `AdminTokenService.IsValidToken` loads the stored admin credential and runs
   PBKDF2 verification for any non-empty token (valid or invalid).

That PBKDF2 work is intentional for valid authentication, but it means a public
admin route can be used as unauthenticated CPU amplification if the deployment
does not enforce an exposure boundary or request throttling before requests
reach Kestrel.

## Recommended Mitigations

Use one of these supported deployment shapes.

### Download-only deployments

If a server only needs to serve public download routes, disable admin
operations:

```text
ShadowDrop__ApiExposure__EnableAdminOperations=false
```

With admin operations disabled, `AdminEndpoints.MapAdminEndpoints` does not map
`/api/admin/*`, and the application does not register or warm up
`AdminTokenService`.

### Deployments with administration enabled

If a server needs upload, share, cleanup, or revoke administration, keep admin
operations enabled only on a trusted management boundary. Acceptable boundaries
include loopback-only access, a VPN, a private network, or a reverse-proxy route
that enforces source restrictions and request limits before forwarding traffic
to ShadowDrop.

The default configuration is:

```text
ShadowDrop__ApiExposure__EnableAdminOperations=true
```

That default keeps local development and full API deployments convenient, but
operators must decide how `/api/admin/*` is exposed before publishing the
service.

## Reverse Proxy Controls

ShadowDrop currently relies on the deployment layer for admin request limiting.
Issue #36 removed the previous in-process ASP.NET Core rate limiter, so do not
expect the application to return `429 Too Many Requests` for repeated invalid
admin-token attempts by itself.

Apply controls equivalent to this at the proxy or ingress:

```text
route /api/admin/* {
  allow source 10.0.0.0/8
  allow source 192.168.0.0/16
  deny all other sources

  rate_limit key=client_ip sustained=10_requests_per_minute burst=20

  proxy_to http://shadowdrop:8080
}
```

Adjust the trusted source ranges and limits to match the deployment, but keep
both properties: untrusted clients should not reach `/api/admin/*`, and allowed
clients should still be throttled before requests are forwarded to Kestrel.

Do not forward `/health/live` or `/health/ready` through the reverse proxy.
They are unauthenticated, intended for host-local and orchestrator probes only,
and each readiness request triggers a MongoDB round-trip when a MongoDB
provider is active.

## Direct-HTTP download URL sensitivity

Direct-HTTP `download-url` values are as sensitive as the file contents because
the URL carries the share key material in the `sd-key` query parameter:

```text
/d/<share-token>/files/<file-id>?sd-key=<base64-key-material>
```

Possession of this URL is equivalent to possession of the plaintext file for
the lifetime of the share. Systems that record complete URLs can retain the key
material even after the user has stopped using the link.

Do not send, paste, store, or proxy direct-HTTP URLs through channels that log
full URLs, including browser history, HTTP referrer headers, chat systems,
reverse proxies, access logs, intermediary HTTP logs, or other request tracing
systems. Use direct-HTTP `download-url` values only in contexts where
complete-URL logging is acceptable.

For command-line transfers, prefer the emitted `curl-command`. It sends the key
material in the `ShadowDrop-Key` header and omits `sd-key` from the request URL,
which keeps the key out of URL-based browser history and HTTP access logs.

If a direct-HTTP URL may have been exposed through a logging channel, revoke the
share or let it expire before treating the file contents as protected again.
