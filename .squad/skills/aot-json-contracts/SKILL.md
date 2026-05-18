---
name: aot-json-contracts
description: Keep shared transport DTO serialization Native AOT-safe across API and CLI
domain: dotnet, aot, serialization
confidence: high
source: earned in ShadowDrop issue #15
---

## Pattern

When a shared contract is serialized in the API and deserialized in the CLI, define a shared `JsonSerializerContext` beside the DTOs and use it from both sides immediately.

## Why

This avoids trim/AOT warnings from `JsonSerializer.Serialize`/`Deserialize` reflection paths and keeps the wire shape centralized. It is especially useful in ShadowDrop because the CLI is intentionally Native AOT-friendly while the API also emits the same DTOs.

## Example

- Put `ContractsJsonSerializerContext` in `src/ShadowDrop.Shared/Contracts/`
- Annotate it with every transport DTO the CLI/API exchange
- Use `ContractsJsonSerializerContext.Default.<TypeName>` from both producer and consumer code
