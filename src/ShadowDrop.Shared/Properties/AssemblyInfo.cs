using System.Runtime.CompilerServices;

// The CLI assembly is named `shadowdrop` (see <AssemblyName> in ShadowDrop.Cli.csproj); the grant
// must match the assembly name, not the project/namespace name.
[assembly: InternalsVisibleTo("shadowdrop")]
