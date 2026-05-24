---
name: "async-assertion-awaiting"
description: "Await FluentAssertions async exception checks in NUnit tests"
domain: "testing"
confidence: "high"
source: "extracted"
---

## Context

In NUnit, FluentAssertions async exception APIs such as `ThrowAsync` and `NotThrowAsync` return tasks. If the test
method is not `async Task` and the assertion task is not awaited, the assertion may never be observed by NUnit, creating
a false-positive test.

## Pattern

- Declare the test as `public async Task ...()`.
- Keep the act delegate async (`Func<Task>` or `async () => ...`).
- `await act.Should().ThrowAsync<TException>();`
- Apply the same rule to other FluentAssertions async assertion helpers.

## Anti-Pattern

- ❌ Calling `act.Should().ThrowAsync<TException>();` inside a `void` test method.
- ❌ Assuming constructing the assertion task is enough for NUnit to fail the test.
