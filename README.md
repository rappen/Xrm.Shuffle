# Xrm.Shuffle for [XrmToolBox](https://www.xrmtoolbox.com/)

### Shuffle Builder 🏗️, Shuffle Runner 🏃 and Shuffle Deployer 🚚
*Empower yourself to achieve more.*

Created by [@rappen](https://github.com/rappen)
<br/>
Improving by [@imranakram](https://github.com/imranakram) & [@rappen](https://github.com/rappen)

[XrmToolBox](http://www.xrmtoolbox.com) tools to compose and run **Shuffle definitions** - XML
files that describe exactly which data and solutions to export from, or import into, a Dataverse
environment. The same engine runs in Azure DevOps pipelines through the
[Innofactor CRM CI build tasks](https://marketplace.visualstudio.com/items?itemName=InnofactorSE.cinteros-devutils-ci-build-tasks).

---
### *Shuffle tools are available in the XrmToolBox Tool Library!* 🥳
---

## The Three Tools

### 🏗️ Shuffle Builder
The Builder **creates and edits Shuffle definition files** through a visual UI - no hand-coding
required.

- Connects to a Dataverse environment to browse tables, columns and relationships
- Build `<DataBlock>` and `<SolutionBlock>` nodes by pointing and clicking
- Copy, paste and reorder blocks
- Save `.xml` definition files for the Runner, the Deployer or a pipeline
- Available in the Tool Library as **`Rappen.XrmToolBox.Shuffle.Builder`**

### 🏃 Shuffle Runner
The Runner **executes a Shuffle definition** - exporting from or importing into the connected
environment.

- Load a definition file and, for imports, a data file, then run it
- **Export** (Dataverse → XML or text file) and **Import** (file → Dataverse)
- Serialization styles: Simple, SimpleWithValue, SimpleNoId, Explicit, Text, Full
- Filter records by attribute value, or supply your own FetchXML
- Optional bulk import - see [Batching and performance](#batching-and-performance)
- Writes a log file next to the definition, named
  `<definition>_<Import|Export>_<connection>_<yyyyMMdd>_<HHmmss>.log`
- Available in the Tool Library as **`Rappen.XrmToolBox.Shuffle.Runner`**

### 🚚 Shuffle Deployer
The Deployer runs **controlled deployments** of packaged Shuffle definitions across environments.

- Works with `.cdpkg` / `.cdzip` packages that bundle definitions and data files
- Select which modules of a package to deploy, and run them in sequence
- Progress tracking and a log at every step
- **Double-click launch**: associate `.cdpkg` files with XrmToolBox and the Deployer loads the
  package on startup
- Available in the Tool Library as **`Rappen.XrmToolBox.Shuffle.Deployer`**

---

## Schema Reference

All three tools are driven by a **Shuffle definition** that follows `ShuffleDefinition.xsd`
(`shared/Xrm.Shuffle.Core/Resources`). Author it in the Builder or by hand.

### `<ShuffleDefinition>` - Root element

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Timeout` | int | 2 | Minutes to wait for an **asynchronous solution import** to finish once the import job has started. It does not limit anything else. |
| `StopOnError` | boolean | `false` | Stop the run at the first record or solution that fails, instead of logging it and carrying on |
| `BypassSyncLogic` | boolean | `false` | Import without running synchronous plugins and workflows. See [Bypassing custom logic](#bypassing-custom-logic) |
| `BypassAsyncLogic` | boolean | `false` | Import without running asynchronous plugins and workflows (Dataverse only) |
| `BypassFlows` | boolean | `false` | Import without triggering Power Automate flows (Dataverse only) |

Contains a `<Blocks>` element holding any combination of `<SolutionBlock>` and `<DataBlock>`,
processed in order.

---

### `<SolutionBlock>` - Import or export a solution

| Attribute | Type | Description |
|-----------|------|-------------|
| `Name` | string (required) | Unique name for this block |
| `Path` | string | Folder path to the solution file |
| `File` | string | Explicit solution filename override |

#### `<Export>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Type` | `Managed` / `Unmanaged` / `Both` / `None` | required | Solution package type to export |
| `SetVersion` | string | - | Set the solution version before exporting |
| `PublishBeforeExport` | boolean | `false` | Publish all customizations before exporting |
| `TargetVersion` | string | - | Target platform version for the export |

`<Settings>` (optional child of `<Export>`) - include settings in the export. All boolean,
default `false`:

`AutoNumbering` · `Calendar` · `Customization` · `EmailTracking` · `General` · `Marketing` ·
`OutlookSync` · `RelationshipRoles` · `IsvConfig`

#### `<Import>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Type` | `Managed` / `Unmanaged` / `Both` / `None` | required | Package type to import |
| `OverwriteSameVersion` | boolean | `true` | Import even when the target already has the same version |
| `OverwriteNewerVersion` | boolean | `false` | Import even when the target already has a newer version |
| `ActivateServersideCode` | boolean | required | Activate plug-ins and workflows after import |
| `OverwriteCustomizations` | boolean | required | Overwrite unmanaged customizations |
| `PublishAll` | boolean | required | Publish all customizations after the import |

`<PreRequisites>` - one or more `<Solution>` elements that must be present in the target before
the import starts:

| Attribute | Description |
|-----------|-------------|
| `Name` | Solution unique name |
| `Comparer` | Version rule: `any`, `eq-this`, `ge-this` (compared with the version of the solution being imported), `eq`, `ge` (compared with `Version`) |
| `Version` | Required version, used with `eq` / `ge`. A `*` is read as `0`, so `ge` with `1.2.*` means at least 1.2.0 |

`<PostSuccessfulImportBlocks>` - a nested `<Blocks>` element whose blocks run only after a
successful import.

---

### `<DataBlock>` - Export or import records

| Attribute | Type | Description |
|-----------|------|-------------|
| `Name` | string (required) | Unique name for this block; the data file refers to it |
| `Entity` | string (required) | Table logical name |
| `Type` | `Entity` / `Intersect` | `Entity` (default) for regular tables; `Intersect` for N:N relationship tables |
| `IntersectName` | string | Intersect table logical name, when it differs from `Entity` |

#### `<Export>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `ActiveOnly` | boolean | `false` | Skip inactive records |

Choose one of two query modes:

**Filter mode** - combine filters, sorting and an explicit column list:
- `<Filter Attribute="..." Operator="..." Type="string|guid|int|bool|datetime|null|not-null" Value="...">` - as many as needed
- `<Sort Attribute="..." Type="Asc|Desc">` - as many as needed
- `<Attributes>` containing `<Attribute Name="..." IncludeNull="false">` - **required**; the columns to export. `Name` may start and/or end with a `*` (or `%`) wildcard, such as `cint_*`; matching is case-insensitive.

**FetchXML mode** - supply your own query, which defines both filters and columns:
- `<FetchXML>` - the raw FetchXML; mutually exclusive with filter mode

#### `<Import>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Save` | `CreateUpdate` / `CreateOnly` / `UpdateOnly` / `Never` | `CreateUpdate` | Whether to create missing records, update matched ones, both, or neither |
| `Delete` | `None` / `Existing` / `All` | `None` | `Existing`: a record that matches exactly one target record replaces it - the target record is deleted and the source record created. `All`: without `<Match>`, delete **every** record of the table in the target before importing; with `<Match>`, delete all matching target records before creating. |
| `CreateWithId` | boolean | `false` | Create records with their source id instead of a new one |
| `UpdateInactive` | boolean | `false` | Update matched records that are inactive in the target; the record is activated first. Shuffle only knows a target record's state when the source record carries `statecode`. |
| `UpdateIdentical` | boolean | `false` | Write matched records even when nothing has changed, instead of skipping them as `(Identical)` |
| `BatchSize` | int | `1` | Records per bulk request, up to `1000`. **Batching is opt-in**: at `1` every record is written individually, exactly as before batching existed. See [Batching and performance](#batching-and-performance). |
| `DeferStateAndOwner` | boolean | `false` | Save records without `statecode`, `statuscode` and `ownerid`, and apply those in a second pass, so the records themselves can be batched |
| `Overwrite` | boolean | - | ⚠️ **Deprecated** - use `Save` |

`<Match>` - how the import finds the existing target record for each source record:

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `PreRetrieveAll` | boolean | `false` | Read the whole target table once at the start of the block and match against it in memory, instead of one query per record. Required for batching a matched block. |

Add one or more `<Attribute Name="..." Display="...">` children: the columns compared to find the
match. `Display` names a different column to show for the record in the log.

`<Relation>` (optional, repeatable) - restricts the export to records related to another block's
records:

| Attribute | Type | Description |
|-----------|------|-------------|
| `Block` | string (required) | Name of the `DataBlock` that provides the related records |
| `Attribute` | string (required) | Lookup column on this table |
| `PK-Attribute` | string | Column on the related block's records to compare with; defaults to its primary key |
| `IncludeNull` | boolean | Also include records where the lookup is empty |

---

## Batching and performance

Every import option below is **opt-in**. A definition that sets none of them imports one record
at a time, as before.

### Turning batching on

Set `BatchSize` (about `100` is Microsoft's recommendation for standard tables). Shuffle then
detects per table what the environment supports and uses, in order:

1. **`CreateMultiple` / `UpdateMultiple`** - Dataverse online
2. **`ExecuteMultipleRequest`** - on-premises 9.1, and tables without the bulk messages
3. **individual requests** - when neither is available

Detection is cached per table for the run, and the log says what it found, such as
`CreateMultiple support for account: True`.

A block that uses `<Match>` only batches with **`PreRetrieveAll="true"`**. Without it, every
record runs its own match query, which has to see the records created so far, so the pending
batch is sent before each query and nothing is grouped.

Records carrying `statecode`, `statuscode` or `ownerid` are never batched, because those need
separate requests. `DeferStateAndOwner="true"` saves them without those columns, in batches, and
then applies them in a second pass: state with `UpdateMultiple` where the table supports it,
owners with `Assign`. The end result is the same. A block that carries nothing but state and
owner is left as it is, since deferring would leave nothing to save.

### Things to know

- **A batch is one transaction.** `CreateMultiple` and `UpdateMultiple` succeed or fail as a
  whole, so one bad record fails its batch, where an unbatched import fails only that record. For
  tables with heavy plug-ins, keep `BatchSize` low or leave batching off. Server-side plug-ins and
  workflows run for every record either way; batching saves round trips, not their cost.
- **`StopOnError="true"`** sends batches with `ContinueOnError = false`. The platform stops at the
  first faulting record, the rest of that batch is not executed, and Shuffle logs
  `StopOnError: aborting, N record(s) in this batch were not executed`. With
  `StopOnError="false"` every record in the batch is attempted and each failure is logged and
  counted, so `Created + Updated + Skipped + Failed` always adds up to the block's records.
- **`PreRetrieveAll` holds the target table in memory.** Every page is read (past Dataverse's
  5000-record page) with only the primary key, the match columns and the imported columns, and
  indexed by match values; the log reports
  `Pre-retrieved N records for matching (K distinct match keys, T ms)`. That is fine into the
  hundreds of thousands of rows. Avoid it on very large tables, and on tables whose imported
  columns are heavy - an `annotation` block importing `documentbody` would read every attachment
  in the environment.
- **`PreRetrieveAll` does not see records created earlier in the same block.** If the data file
  holds two records with the same match values, both are created. Without `PreRetrieveAll`, the
  second would match the first.

### The upsert path

A block skips matching entirely and sends **`UpsertMultiple`**, letting Dataverse decide between
create and update, when **all** of these hold:

- `BatchSize` above `1`
- `Save="CreateUpdate"` and `CreateWithId="true"`
- `<Match>` has exactly one attribute: the table's primary key (such as `contactid`)
- `Delete="None"`
- `UpdateIdentical="true"` - upsert never reads the existing record, so it cannot skip identical ones

Upsert finds records by primary key and nothing else, which is why any other `<Match>` keeps the
match path: a record that exists in the target under another id would otherwise be created a
second time. `PreRetrieveAll` is not needed and is skipped. Where `UpsertMultiple` is not
available, Shuffle sends `UpsertRequest`s in an `ExecuteMultipleRequest`, and failing that,
creates each record and updates it if it already exists.

```xml
<DataBlock Name="Contacts" Entity="contact">
  <Import Save="CreateUpdate" CreateWithId="true" UpdateIdentical="true" BatchSize="100">
    <Match>
      <Attribute Name="contactid" />
    </Match>
  </Import>
</DataBlock>
```

---

## Bypassing custom logic

A data load into a configured environment fires every plugin, workflow and flow registered on
the tables it writes to - once per record. That makes loads slow, fills the async queue, and can
change the data on its way in: auto-numbering, validation, flows that send mail. Three attributes
on `<ShuffleDefinition>` switch that off for the whole import:

```xml
<ShuffleDefinition BypassSyncLogic="true" BypassAsyncLogic="true" BypassFlows="true">
```

| Attribute | Skips | Sent as |
|-----------|-------|---------|
| `BypassSyncLogic` | Synchronous plugins and real-time workflows | `BypassBusinessLogicExecution = CustomSync` |
| `BypassAsyncLogic` | Asynchronous plugins and background workflows | `BypassBusinessLogicExecution = CustomAsync` |
| `BypassFlows` | Power Automate flows triggered by Dataverse | `SuppressCallbackRegistrationExpanderJob = true` |

- **Every write in every data block** carries the parameters: batched and single creates and
  updates, upserts, deletes, state and owner changes (also the deferred pass) and N:N
  associations. Solution blocks are not affected.
- **The user needs the `prvBypassCustomBusinessLogic` privilege** for sync and async logic
  (System Administrators have it). Without it, the first write fails with Dataverse's error.
- Microsoft's own logic still runs; only custom logic is skipped.
- **On-premises 9.0 and 9.1** only support bypassing sync logic, sent as the older
  `BypassCustomPluginExecution`. A definition that also asks for async logic or flows stops
  before writing anything there, rather than import with logic it said to skip. Older versions
  cannot bypass at all.

See Microsoft's [Bypass custom business logic](https://learn.microsoft.com/power-apps/developer/data-platform/bypass-custom-business-logic)
and [Bypass Power Automate flows](https://learn.microsoft.com/power-apps/developer/data-platform/bypass-power-automate-flows).

---

## What's new since 1.2023.5

### Import
- **Opt-in batching** with `BatchSize`, using `CreateMultiple`/`UpdateMultiple`, `ExecuteMultipleRequest` or individual requests depending on what the environment supports
- **`DeferStateAndOwner`** for batching records that carry state or owner
- **Upsert path** for blocks matching on the primary key, with `UpsertMultiple`
- **Bypass custom logic**: `BypassSyncLogic`, `BypassAsyncLogic` and `BypassFlows` on the definition skip plugins, workflows and flows during the import, with three matching checkboxes on the Builder's root node
- The Builder has a **Batch size** field and a **Defer state and owner** checkbox on the Import node
- **`PreRetrieveAll` reads the whole target table.** It stopped at 5000 records, so every source record whose match lay beyond that was created a second time
- **`PreRetrieveAll` matching is a lookup** instead of a scan of the whole table for every record
- A record that matches several target records is reported with its row number
- A deferred state or owner change gets the real id of a record created in a batch, and changes for records that were never written are dropped instead of reported as failures
- A record whose lookups point at a record still waiting in the batch is written after that batch is sent, instead of with the source-system id
- Failed records in a batch are logged with their row and the server's message, and never counted as saved
- Multi-select choice columns (`OptionSetValueCollection`) export and import correctly
- Floating point (`Double`) columns can be imported from every format, not only Full
- Records with date columns are skipped as `(Identical)` when nothing changed. A date read from a file and the same date read from the target used to compare as different, so these records were always updated, and a `Match` on a date column created a duplicate

### Solutions
- A zip without `solution.xml` fails with a clear `FileNotFoundException`
- Prerequisite versions are logged after they are resolved

### Logs and the Runner
- Log lines show values instead of SDK type names, and the record id for blocks matched on the primary key
- No more `[START of ToString]` noise from the import status poll and record labels
- Log sections are nested correctly, also after a failed record or solution
- Log files are named after the definition, operation, connection and time
- The Runner no longer makes the import wait for its window to repaint, which cost about 15 ms per record
- Every import and export ends with its total run time and counts, for example `Import finished in 1 min 58 s: 14970 created, 0 updated, 0 skipped, 0 deleted, 0 failed` - in the log, the Runner and the pipeline output

### Export
- **Exports are complete.** Every export query stopped at 5000 records, so larger tables were cut off without a warning; all three - filter, FetchXML and intersect - now read every page
- **Data files are culture-independent.** Numbers are written as `1234.5` and dates as `2026-10-05T12:30:00.0000000Z` on every machine, so a file exported on one machine imports correctly on another
- Columns are written in alphabetical order, so re-exporting unchanged data gives no diff
- Fixed an off-by-one error in text export that could throw `IndexOutOfRangeException`

### Platform
- .NET Framework 4.8; `ILMerge` removed; `DotNetZip` replaced with `System.IO.Compression`

### Behaviour changes to be aware of
- **A failed update now counts as Failed**, and stops the run when `StopOnError` is set. It used to be swallowed and counted as Skipped.
- **`PreRetrieveAll` really reads the whole table** - see above.
- A match column that is present but empty compares as `<null>` instead of failing the record.
- **Numbers and dates in data files no longer follow the machine's culture.** New exports write `1234.5`. An older file written with commas (`1234,5`) is still imported on a machine that uses a decimal comma; on any other machine that value is now rejected with an error, where it used to be read as `12345`.
- Dates are written as `2026-10-05T12:30:00.0000000Z` instead of the machine's date format. They are read exactly as before, so existing data files import their dates unchanged.

---

## Building and testing

```
nuget restore Rappen.XTB.Shuffle.sln
msbuild Rappen.XTB.Shuffle.sln /p:Configuration=Release /p:Platform="Any CPU"
vstest.console.exe tests\Xrm.Shuffle.Core.Tests\bin\Release\Xrm.Shuffle.Core.Tests.dll /Framework:.NETFramework,Version=v4.8
```

`tests/Xrm.Shuffle.Core.Tests` compiles the Shuffle core and
[Xrm.Utils.Core](https://github.com/rappen/Xrm.Utils.Core) (a submodule) and tests them against a
fake organization service that pages results at 5000 like Dataverse: the import engine, data
export, solution import decisions, export-and-import round trips in every serialization style
and across cultures, and the Xrm.Utils.Core value helpers. The XrmToolBox user interfaces are
not covered. Run it in both
Debug and Release - Debug sends extra FetchXml conversion requests. CI
(`.github/workflows/build.yml`) builds and tests both on every push and on pull requests to master; `release.yml` publishes a
CI run's packages to NuGet, which the XrmToolBox Tool Library reads. See `CLAUDE.md` for the
code layout and the import strategy in more detail.

---

## Home page
https://jonasr.app/shuffle/

## Articles
https://jonasr.app/2017/04/devops-i/ <br/>
These describe the outline of the tools in this repository.

https://saralagerquist.com/2019/12/02/mvp-advent-calendar-transport-data-between-environments-with-saras-favorite-tool/<br/>
Sara Lagerquist explains an example of how to use it.
