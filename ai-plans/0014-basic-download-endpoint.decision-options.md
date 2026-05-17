# Decision Options: Basic Download Endpoint Crypto Scope

## Problem Statement

Direct-HTTP download decryption originally derived cryptographic state from share-scoped values even though normal
uploads happen before any share exists. That meant a normal upload could not later satisfy direct-HTTP decryption once a
share was created.

Current state before the fix:

- `FileEncryptionContext` required `(shareId, fileId, kdfSalt)`
- chunk authentication also bound `shareId`
- uploads assign `fileId` during persistence and store `kdfSalt` in metadata
- shares are created independently, assigning `shareId` after upload

---

## Option 1: File-Scoped Context (Selected)

**What changes:** Remove `shareId` from the direct-HTTP cryptographic binding. Use only `(fileId, kdfSalt)` for HKDF
derivation, and bind chunk authentication to file-scoped metadata only. In practice, the server must reserve the
opaque `fileId` before the client encrypts so the file-scoped context exists at upload time.

**Pros:**

- Simplest crypto shape: file context is stable and available before upload once the server reserves the file id
- File context is inherently known at upload time
- Any share can decrypt the file without re-encryption
- Direct-HTTP becomes compatible with normal uploads

**Cons:**

- All shares of the same file use identical file-scoped crypto context
- Reduces isolation between multiple shares of the same uploaded file

**Migration:** No data migration is planned for the MVP. This decision applies to the implementation moving forward.

---

## Option 2: Stored Encryption Context

**What changes:** At upload time, generate a random encryption context ID. Store it with file metadata. At download,
retrieve and use the stored context instead of any share-derived value.

**Pros:**

- File-level isolation preserved
- Context available at encryption time
- Supports future context versioning and key rotation

**Cons:**

- Requires schema changes
- Requires migration work for already-stored files
- Adds implementation complexity to the MVP

---

## Option 3: Client-Provided Context

**What changes:** Allow the client to provide an explicit encryption context during upload and persist it for later
direct-HTTP decryption.

**Pros:**

- Flexible for automation and advanced workflows
- Can support pre-planned sharing models

**Cons:**

- Pushes crypto-shape knowledge into client code
- Requires extra validation and API complexity

---

## Option 4: Disable Direct-HTTP for Normal Uploads

**What changes:** Keep direct-HTTP only for special seeded flows and do not support it for normal uploads.

**Pros:**

- No additional implementation work
- Avoids changing current crypto contracts

**Cons:**

- Direct-HTTP remains incomplete for the normal product flow
- Conflicts with the intended MVP direction

---

## Recommendation

**Option 1 (File-Scoped Context)** is the best MVP trade-off:

- removes the upload-time/share-time mismatch
- avoids new persistence requirements
- keeps the direct-HTTP path shippable now

If stronger per-share isolation becomes necessary later, revisit Option 2 after the MVP ships.

---

## Compatibility Impact

| Option | Backward Compat                        | Migration Effort | Timeline         |
|--------|----------------------------------------|------------------|------------------|
| 1      | Compatible with forward implementation | Zero             | Use immediately  |
| 2      | Requires schema/data work              | Moderate         | After MVP        |
| 3      | Optional; client-driven                | Moderate         | Later            |
| 4      | Already compatible                     | Zero             | Current fallback |

---

## Decision Record

- **Issue:** `#14`
- **Decision:** Use **file-scoped context** for the MVP, with a server-reserved upload file id.
- **Recorded in:** `docs/ARCHITECTURE_DECISIONS.md`
- **Why:** Direct-HTTP must work for normal uploads, and the cryptographic binding must only depend on values available
  when the file is encrypted. Reserving the file id before upload gives the client that file-scoped context without
  reintroducing share-derived binding.
- **Deviation from earlier plan/concept:** The original direct-HTTP shape effectively assumed share-scoped crypto
  binding. The implemented MVP instead binds direct-HTTP decryption to file-scoped values so uploads do not depend on
  future share creation.
