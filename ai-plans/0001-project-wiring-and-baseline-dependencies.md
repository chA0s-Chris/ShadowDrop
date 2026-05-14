## Rationale

Establish the baseline solution wiring so the API, CLI, shared library, and test projects build together with the dependencies needed for the first implementation slices. This plan should leave the repository ready for feature work without introducing product behavior yet.

## Acceptance Criteria

- [ ] Existing project references are verified and preserved.
- [ ] Production and test projects are included in `ShadowDrop.slnx`.
- [ ] Central package versions are added for LiteDB, System.CommandLine, and Spectre.Console.
- [ ] Required package references are added to the API and CLI projects.
- [ ] The CLI project configuration supports Native AOT publishing.
- [ ] A Native AOT smoke publish succeeds for the `linux-x64` CLI runtime identifier.
- [ ] The solution builds successfully with `dotnet build`.
- [ ] The test suite runs successfully with `dotnet test`.

## Technical Details

Keep the solution structure minimal: `ShadowDrop.Api`, `ShadowDrop.Cli`, and `ShadowDrop.Shared` are the only production projects needed for the MVP. Do not introduce application, domain, or infrastructure projects.

Project references should follow deployment boundaries. The API and CLI can depend on `ShadowDrop.Shared`; `ShadowDrop.Shared` should not depend on the API or CLI. Test projects should reference the project they test and any shared test dependencies already established by the repository. The existing project references and solution membership should be verified rather than recreated.

Use central package management in `Directory.Packages.props`. Add only the package versions required to support the near-term plans: LiteDB for metadata persistence, System.CommandLine for CLI parsing, and Spectre.Console for terminal UI. Add package references to the projects that actually need them: LiteDB belongs in `ShadowDrop.Api`, while System.CommandLine and Spectre.Console belong in `ShadowDrop.Cli`.

Configure the CLI project so Native AOT compatibility is visible early. The plan does not need to produce all runtime-specific artifacts yet, but it should include a smoke publish for the `linux-x64` runtime identifier. CLI dependencies and project settings should avoid patterns that make Native AOT an afterthought.
