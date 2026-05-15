---
last_updated: 2026-05-14T17:48:12.434Z
---

# Team Wisdom

Reusable patterns and heuristics learned through work. NOT transcripts — each entry is a distilled, actionable insight.

## Patterns

<!-- Append entries below. Format: **Pattern:** description. **Context:** when it applies. -->

**Pattern: Upfront validation gates for streaming pipelines.** Reject malformed input **before** consuming expensive resources (request body streams, I/O, network). Establishes a strict contract: parsing is synchronous, complete, and deterministic before any resource is locked. **Context:** API endpoints accepting file uploads, blob streaming, or large payloads where rejection after partial consumption wastes bandwidth or resources.

**Pattern: Cross-layer rollback on transactional failure.** When a multi-step write spans multiple persistence layers (database, filesystem, or remote storage), use explicit cleanup handlers or multi-phase commit to guarantee no orphaned state remains if any layer fails. Test by simulating failure at each boundary. **Context:** Distributed storage, blob + metadata workflows, where partial success in one layer but failure in another creates silent leaks or broken pointers.
