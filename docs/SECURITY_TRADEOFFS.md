# Security Trade-offs

ShadowDrop encrypts content on the sender's machine with AES-256-GCM and
stores only ciphertext on the server. What varies between the modes below is
**how the decryption key travels** and what a leaked artifact is worth to an
attacker. This page explains those trade-offs; the operational mitigations
live in [Deployment Hardening](DEPLOYMENT_HARDENING.md).

## Separate-key mode (default)

A default `upload` produces two artifacts with deliberately split value:

- the **share URL** — public reference to the ciphertext; useless without the
  key, and
- the **share key** — printed as `share-key:` (or written to a file with
  `--secrets-out`).

Deliver them over **different channels** (e.g. URL by e-mail, key by
messenger). An attacker must compromise both channels to read the content;
the server can never decrypt it because the key never reaches the server.
This is the recommended mode whenever the recipient can run the CLI.

`--secrets-out` keeps credentials off the terminal (and out of shell history
and scrollback). `--embed-secrets` does the opposite for queues: it produces a
single self-contained queue file that is as sensitive as the shared files
themselves — treat it like the plaintext.

## Direct-HTTP mode (`--direct-http`)

Direct-HTTP shares exist for recipients who cannot run the CLI. The emitted
`download-url` embeds the key material in the `sd-key` query parameter, so
**possession of the URL equals possession of the file**:

```text
https://…/d/<share-token>/files/<file-id>?sd-key=<base64-key>
```

Complete URLs are routinely recorded: browser history, HTTP referrer headers,
chat previews, proxy and access logs, and request tracing all retain the key
material — potentially long after the download. See
[Direct-HTTP download URL sensitivity](DEPLOYMENT_HARDENING.md#direct-http-download-url-sensitivity)
for the full guidance, including revoking a share after a suspected exposure.

### `curl-command`: header-based key delivery

For command-line recipients, direct-HTTP uploads also emit a `curl-command`
that sends the key in the `ShadowDrop-Key` **header** and keeps `sd-key` out
of the URL:

```bash
curl -H 'ShadowDrop-Key: 0+9Ol9…' 'https://…/d/<share-token>/files/<file-id>' -o 'report.pdf'
```

Headers do not land in URL-based logs or browser history, so prefer the
`curl-command` over the `download-url` whenever the recipient has a shell.

## Bearer tokens

### Download bearer tokens

`upload --download-token` (separate-key shares only) generates an additional
download bearer token. Downloads then require both the share key **and**
`--bearer-token <token>`, giving you a second, independently deliverable
credential. The download CLI accepts the token only as a command-line
argument.

### The upload token is the admin token

The CLI's upload token (`--upload-token`, `SHADOWDROP_UPLOAD_TOKEN`, or the
config file) is the server's **admin bearer token** — uploads are admin
operations against `/api/admin/uploads`. There is no lesser upload-only
credential in the MVP. Consequences:

- Anyone who can upload can also create, revoke, and clean up shares.
- The CLI must reach the admin exposure boundary; per
  [Deployment Hardening](DEPLOYMENT_HARDENING.md#admin-endpoint-exposure),
  that boundary should not be the public Internet.
- Guard the token accordingly: prefer the environment variable or the
  config file over the flag (process listings can expose flags), and never
  bake it into scripts you distribute.

## `--insecure` versus `--cacert`

When the server presents a certificate the CLI does not trust (self-signed,
private CA):

- `--cacert <pem>` (or `SHADOWDROP_CACERT`) adds the given certificate as an
  additional trust anchor. The presented chain is **still validated** — this
  is the safe option and should be your default for lab or internal setups.
- `-k`/`--insecure` (or `SHADOWDROP_INSECURE=1|true|yes`) disables certificate
  validation entirely. A man-in-the-middle can then read the upload token,
  bearer tokens, and any direct-HTTP key material in transit. Use it only for
  throwaway local testing, never with a real admin token.

Once `SHADOWDROP_INSECURE` is set to a truthy value there is no flag to force
validation back on for a single invocation — unset the variable instead.
