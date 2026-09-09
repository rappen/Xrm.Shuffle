# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

Xrm.Shuffle is an **XrmToolBox plugin suite** for Dataverse/Dynamics CRM. It ships as three separate NuGet packages — Shuffle Builder, Shuffle Runner, Shuffle Deployer — all compiled from a **single assembly** (`Rappen.XTB.Shuffle.dll`). The tools read/write XML-based "ShuffleDefinition" files that describe what Dataverse data or solutions to export or import.

## Build

```bash
nuget restore Rappen.XTB.Shuffle.sln
msbuild Rappen.XTB.Shuffle.sln /p:Configuration=Release /p:Platform="Any CPU" /m
```

Output goes to `XTB\bin\Release\`. There are no automated tests — validation is manual/integration only.

To produce NuGet packages:
```bash
nuget pack "XTB\ShuffleRunner.nuspec" -OutputDirectory nupkg
nuget pack "XTB\ShuffleBuilder.nuspec" -OutputDirectory nupkg
nuget pack "XTB\ShuffleDeployer.nuspec" -OutputDirectory nupkg
```

CI runs `.github/workflows/build.yml` / `release.yml` with versioning scheme `1.{year}.{month}.{run_number}`.

## Architecture

### Project layout

```
XTB/                        ← Single .csproj for all three tools
  Builder/                  ← Visual XML editor for creating definitions
  Runner/                   ← Executes definitions (export or import)
  Deployer/                 ← Orchestrates packaged deployments (.cdpkg/.cdzip)
shared/Xrm.Shuffle.Core/   ← Shared Project (.shproj) — compiled directly into XTB, no separate DLL
Xrm.Utils.Core/            ← Git submodule: extensions, fluent API, logging, CSV helpers
```

### The Shared Project pattern

`Xrm.Shuffle.Core` is a **Shared Project** (`.shproj`), not a library. Its files are compiled directly into the main project. All core business logic lives here:

- **`Shuffler.cs`** — top-level orchestrator; parses the ShuffleDefinition XML, drives block execution, dispatches events
- **`ShuffleDataImport.cs`** — high-performance importer with a capability-detection fallback chain (see below)
- **`ShuffleDataExport.cs`** — query-based exporter; supports filter and FetchXML modes
- **`ShuffleSolutionImport/Export.cs`** — solution package handling
- **`Types.cs`** — core enums (`SerializationType`, `ItemImportResult`, `SolutionImportConditions`)
- **`ShuffleHelper.cs`** — schema validation, DataFileRequired checks, node documentation lookup
- **`Const.cs`** — auto-generated latebound constants for CRM API entities (ImportJob, AsyncOperation)

The XML schema is `Resources/ShuffleDefinition.xsd`; the corresponding C# class `Resources/ShuffleDefinition.cs` is auto-generated from it.

### Bulk import strategy — runtime capability detection

`ShuffleDataImport.cs` detects what the connected environment supports **at runtime per entity** (cached) and walks down this chain:

1. **UpsertMultiple** (Dataverse online, fastest — bypasses PreRetrieveAll entirely when `UpdateIdentical=true`)
2. **CreateMultiple / UpdateMultiple** (Dataverse online)
3. **ExecuteMultipleRequest** (CRM 9.1 on-premises fallback)
4. **Individual operations** (CRM 8.x fallback)

Detection uses `sdkmessagefilter` queries. Results are cached per entity name to avoid repeated round-trips.

### Batching

Creates, updates and upserts are accumulated into a pending list and flushed in
batches of `BatchSize` (default 100). The batch must be flushed early whenever the
next step needs the server to already know about the pending records — before a live
`Match` query, and before `ReplaceGuids` rewrites a record whose lookups point at a
record still in the batch. `IsBatchable` decides what may be batched at all; among
other things it excludes any record carrying `statecode`, `statuscode` or `ownerid`.

### DeferStateAndOwner

When `DeferStateAndOwner` is enabled, `statecode`/`statuscode`/`ownerid` are stripped
from the record before it is saved and applied in a second pass once the records
exist. The point is the `IsBatchable` exclusion above: without deferring, every record
that carries one of those attributes falls off the batched path and is imported one at
a time, so a block of inactive or non-default-owner records gets no batching at all.
Deferring keeps those records batchable and moves the state/owner work into a separate
pass that can itself be batched. The end state of each record is unchanged.

No benchmark numbers are published for this — the gain depends entirely on how many
records in the block carry those attributes.

### XrmToolBox plugin model

All three UI classes inherit `PluginControlBase` and implement standard XrmToolBox interfaces (`IMessageBusHost`, `IGitHubPlugin`, `IHelpPlugin`, `IAboutPlugin`). Long-running operations raise events via `ShuffleEventHandler` / `ShuffleEventArgs` rather than blocking the UI thread.

### Builder controls bind through `Tag`

Each editor under `XTB/Builder/Controls/` derives from `ControlBase`, which reads and
writes the definition XML purely by convention: every control whose `Tag` is set to
`"AttributeName|required|defaultvalue"` becomes that XML attribute, ordered by
`TabIndex`, and a value equal to the default is omitted from the output. Adding a new
attribute to the schema therefore means adding a control with the matching `Tag` —
there is no separate mapping table.

`ControlBase` also fills one "Information" box per node from the `<xs:documentation>`
on the *element* in `ShuffleDefinition.xsd`, via `ShuffleHelper.GetNodeDocumentation`.
It does not resolve documentation on *attributes*, so per-attribute help has to be a
tooltip on the control.

### Serialization types

Six types control export format: `Full`, `Simple`, `SimpleWithValue`, `SimpleNoId`, `Explicit`, `Text`. `SimpleWithValue` is the default for Runner. Attributes are sorted alphabetically on export for deterministic diffs.

### Namespaces

| Area | Namespace |
|---|---|
| Core logic | `Cinteros.Crm.Utils.Shuffle` |
| Builder UI | `Rappen.XTB.Shuffle.Builder` |
| Runner UI | `Rappen.XTB.Shuffle.Runner` |
| Deployer UI | `Rappen.XTB.ShuffleDeployer` |

## Key dependencies

- **XrmToolBoxPackage** — plugin framework (PluginControlBase, connection management)
- **Xrm.Utils.Core** (submodule) — IExecutionContainer, ILogger, entity/service extensions
- **Microsoft.CrmSdk.Workflow** — CRM SDK types
- **System.IO.Compression** — used by Deployer for `.cdpkg` (ZIP) handling
