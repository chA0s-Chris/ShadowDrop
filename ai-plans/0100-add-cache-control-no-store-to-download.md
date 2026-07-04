# Add Cache-Control: no-store to download and manifest responses

> Issue: [#100](https://github.com/chA0s-Chris/ShadowDrop/issues/100) (part of umbrella [#97](https://github.com/chA0s-Chris/ShadowDrop/issues/97))

## Rationale

`DownloadStreamResult` and the share manifest endpoint set content headers for decrypted
direct-HTTP payloads and manifests, but they do not currently send cache directives. That leaves
decrypted file bytes and share metadata eligible for storage by browser disk caches or intermediary
caches. Add `Cache-Control: no-store` to these responses so downloaded plaintext and manifests are
handled as non-cacheable sensitive material.

## Acceptance Criteria

- [x] Direct-HTTP download responses include `Cache-Control: no-store` for both full-file and range
  responses while preserving the existing content, range, disposition, and ShadowDrop metadata
  headers.
- [x] Share manifest JSON responses include `Cache-Control: no-store` while preserving the existing
  JSON contract and status-code behavior.
- [x] CLI streamed-download responses do not gain new cache directives; `Cache-Control: no-store`
  is limited to direct-HTTP plaintext downloads and share manifest JSON responses.
- [x] Automated tests need to be written.

## Technical Details

Keep the change close to `ShadowDrop.Api.Downloads.DownloadEndpoints`. The successful manifest path
currently returns `Results.Json(result.Manifest)` from `GetShareManifestAsync`; replace or wrap that
result with a small `IResult` helper if needed so the response can set `Cache-Control: no-store`
without changing the serialized manifest contract. The successful file path already uses the custom
`DownloadStreamResult`, so set the cache directive in `ExecuteAsync` alongside the existing response
headers only when `resolution.Mode == DownloadMode.DirectHttp`.

Error responses (`StatusDownloadResult`, including their JSON error bodies) are intentionally out of
scope and keep their current cache behavior; they carry no decrypted content or share metadata.

Use ASP.NET Core's typed header APIs where practical (`Response.GetTypedHeaders().CacheControl` with
`NoStore = true`) or the equivalent response header assignment if that fits the surrounding style
better. Avoid adding `public`, `private`, `max-age`, or validation directives unless the tests or
framework behavior require them; the issue specifically asks for `no-store`.

Add focused API tests around the successful response paths. Cover a direct-HTTP full download, a
direct-HTTP range download, a successful manifest response, and a CLI streamed-download response.
The direct-HTTP and manifest assertions should verify the presence of `no-store`; the CLI assertion
should verify the cache directive is absent. Also spot-check that the existing important headers and
payload behavior remain intact. Do not broaden the scope into an application-level cache middleware
change unless that is necessary to keep the response behavior consistent.
