---
name: "parse-result-help-detection"
description: "Detect CLI help from parser-recognized options, not raw argv scanning"
domain: "cli, parsing"
confidence: "high"
source: "extracted"
---

## Context

Use this pattern when a CLI accepts positional operands that may legally look like option tokens once the user inserts
`--` to end option parsing. Raw string scans of `args` cannot distinguish a real help option from a literal operand.

## Pattern

- Parse the command line first.
- Detect help from parser-recognized option results (`ParseResult`, `CommandResult`, or equivalent parser metadata), not
  by scanning raw `string[] args`.
- Preserve the `--` end-of-options contract so operands like `--help` or `-h` can reach the command handler unchanged.
- Keep help-routing logic aligned with the parser's own command/option resolution.

## Anti-Pattern

- ❌ Treating any raw argv token equal to `--help` or `-h` as a help request.
- ❌ Bypassing parser state in a way that makes literal file names or other operands unreachable.
