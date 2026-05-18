---
name: "Request Consolidation & Centralized Validation"
description: "Consolidate scattered input parameters into a single validated object at the API boundary layer, preventing parameter sprawl and inferred behavior across handlers."
domain: "api-design, contract-clarity, endpoint-validation"
confidence: "high"
source: "observed in issue #27 CLI download contract clarification (plan 0027); Eliot/Sophie codebase impact analysis surfaced parameter threading fragmentation risk"
tools:
  - name: "C# record/struct definitions"
    description: "Consolidation pattern uses immutable value objects"
    when: "At API endpoint boundary before routing/service calls"
---

## Context

When API endpoints accept multiple independent input parameters (query parameters, headers, request body fields) that interact via validation rules, scattering the validation logic across endpoint → service → helper methods creates three failure modes:

1. **Inferred Behavior:** Handlers don't know what previous layers already validated, risking duplicate or missed checks
2. **Parameter Sprawl:** New parameters get threaded separately through the call stack instead of grouped together
3. **Contradictory Inputs:** Mixing parameter combinations (e.g., `?mode=cli` + legacy `plaintextStart`) is discovered late instead of at construction time

This is especially risky for APIs with multiple "modes" or branching logic where different parameter combinations trigger different behavior paths.

## Patterns

### 1. Single Consolidated Request Object

At the endpoint entry point, validate and consolidate all inputs into a single immutable value object before making any business logic calls:

```csharp
// BAD: Parameters threaded separately
await DownloadFileService.ResolveAsync(
    shareId, 
    mode, 
    range, 
    bearerToken, 
    plaintextStart,           // Old param
    plaintextEndExclusive,    // Old param
    directHttpKey             // Different boundary
);

// GOOD: Single consolidated object
var request = new CliDownloadRequest(
    mode: mode,
    plaintextRange: ResolvePlaintextRange(range, plaintextStart, plaintextEndExclusive),
    shareId: shareId,
    bearerToken: bearerToken,
    directHttpKey: directHttpKey
);
await DownloadFileService.ResolveAsync(request);
```

### 2. Validation at Construction Time

Place all cross-parameter validation in the consolidation object's constructor or factory method. Fail fast if inputs are contradictory:

```csharp
public record CliDownloadRequest
{
    public string Mode { get; }
    public PlaintextRange? RequestedRange { get; }
    public ShareId ShareId { get; }
    public BearerToken? Token { get; }
    public DirectHttpKeyMaterial? KeyMaterial { get; }
    
    public CliDownloadRequest(
        string mode, 
        PlaintextRange? range, 
        ShareId shareId, 
        BearerToken? token, 
        DirectHttpKeyMaterial? keyMaterial)
    {
        // All contradictions caught here, not discovered in service layer
        if (mode == "cli" && keyMaterial != null)
            throw new ArgumentException("CLI mode incompatible with direct-HTTP key material");
        
        if (mode == "cli" && token != null && /* share forbids direct-HTTP */)
            throw new ArgumentException("This share does not support CLI mode");
        
        Mode = mode;
        RequestedRange = range;
        ShareId = shareId;
        Token = token;
        KeyMaterial = keyMaterial;
    }
}
```

### 3. Centralized Validation Sequencing

Place the consolidation step in the endpoint **before** any routing or service calls. Document the sequence:

1. **Parse & Normalize:** Extract inputs from request (query, headers, body)
2. **Construct Object:** Validate and consolidate into single object (contradictions fail here with generic 400)
3. **Route:** Based on validated object properties, call appropriate service or handler
4. **Service Call:** Receive only the consolidated object, not individual parameters
5. **Response:** Return unified result; endpoint streams/serializes deterministically

### 4. One Request Model per Mode or Branch

If your API has multiple "modes" (e.g., direct-HTTP, CLI binary, legacy JSON), consider:
- **Single flexible model:** One consolidation class handles all modes via optional fields
- **Multiple models per mode:** Separate `DirectHttpDownloadRequest`, `CliDownloadRequest`, etc.; use sealed base class for common shape

Choose based on shared vs. distinct validation rules.

## Examples

### Plan 0027: CLI Download Contract

**Scenario:** Endpoint receives `?mode=cli`, `Range: bytes=0-999`, legacy `?plaintextStart=0`, bearer token, and direct-HTTP key material. Should consolidate before routing:

```csharp
// Endpoint layer
var consolidatedRequest = CliDownloadRequest.FromHttpRequest(
    context.Request.Query["mode"],
    context.Request.Headers["Range"],
    context.Request.Query["plaintextStart"],
    context.Request.Query["plaintextEndExclusive"],
    context.Request.Headers["Authorization"],
    context.Request.Query["directHttpKey"]
);

// If construction succeeds, inputs are valid and non-contradictory
if (consolidatedRequest.Mode == "cli")
    return await _cliService.ResolveAsync(consolidatedRequest);
else
    return await _legacyService.ResolveAsync(consolidatedRequest);
```

### Header Sanitization (Related)

Similar pattern for response headers: consolidate metadata into a single object, validate internal consistency, then emit deterministically:

```csharp
var metadata = new CliDownloadMetadata(
    firstChunkIndex: 5,
    lastChunkIndex: 12,
    plaintextRangeStart: 4096,
    plaintextRangeEnd: 8191,
    totalSize: 1_000_000,
    chunkSize: 1024,
    finalChunkLength: 512
);

// Validation happens here
metadata.ValidateConsistency(); // Throws if, e.g., lastChunkIndex < firstChunkIndex

// All headers set deterministically from one source
response.Headers["X-ShadowDrop-First-Chunk-Index"] = metadata.FirstChunkIndex.ToString();
response.Headers["X-ShadowDrop-Last-Chunk-Index"] = metadata.LastChunkIndex.ToString();
// ... etc.
```

## Anti-Patterns

### 1. Partial Consolidation

Don't consolidate some parameters and thread others separately:

```csharp
// BAD: Mixed approach — some in object, some threaded
var range = new PlaintextRange(start, end);
var mode = context.Request.Query["mode"];
var keyMaterial = ParseDirectHttpKey(context.Request);

await service.Resolve(range, mode, keyMaterial, /* ... more individual params */);
```

### 2. Late Validation

Don't validate contradictions in service layer; validate at construction time:

```csharp
// BAD: Service discovers contradiction, too late to fail with 400
public async Task<DownloadResult> ResolveAsync(
    string mode, 
    PlaintextRange? range, 
    DirectHttpKeyMaterial? key)
{
    if (mode == "cli" && key != null)
        throw new InvalidOperationException("..."); // Wrong exception type
}

// GOOD: Endpoint rejects before service call
var request = new CliDownloadRequest(mode, range, key); // Throws ArgumentException
```

### 3. Inference from Omitted Parameters

Don't use absence of a parameter to infer behavior; make it explicit:

```csharp
// BAD: Inferring mode from absence of query parameters
if (string.IsNullOrEmpty(query["mode"]))
    useLegacyPath(); // Implicit inference

// GOOD: Explicit default with clear routing
var mode = query["mode"] ?? "default"; // Explicit, testable
var request = new DownloadRequest(mode, ...);
```

## Related Patterns

- **Single Responsibility:** Each handler receives one clear responsibility (endpoint handles validation, service handles business logic)
- **Fail Fast:** Contradictions detected at boundary, not discovered mid-pipeline
- **Explicit Routing:** Mode selection is a data-driven property of the consolidated object, not implicit logic
