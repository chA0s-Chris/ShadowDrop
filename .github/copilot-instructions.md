# Copilot Instructions

## Code Reviews

When performing a code review, always ignore files in these directories:

- ./.claude
- ./.idea
- ./.nuke
- ./.windsurf

### Nullable reference types and FluentAssertions

This repository pins FluentAssertions 7.2.2, whose assertions carry nullability
flow annotations (`[NotNull]`): after `value.Should().NotBeNull()`, the C#
compiler treats `value` as non-null. Dereferencing the asserted value on the
following lines without a null-forgiving operator (`!`) is therefore correct
and produces no CS8602 warning — the solution builds in Release with
`TreatWarningsAsErrors=true`.

Do not flag dereferences that follow a `Should().NotBeNull()` assertion as
nullable-dereference bugs or build breakers, and do not suggest reintroducing
`!` after such assertions.
