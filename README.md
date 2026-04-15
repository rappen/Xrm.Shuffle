# Xrm.Shuffle for [XrmToolBox](https://www.xrmtoolbox.com/)

### Shuffle Builder 🏗️, Shuffle Runner 🏃 and Shuffle Deployer 🚚
*Empower yourself to achieve more.*

Created by [@rappen](https://github.com/rappen)
<br/>
Improving by [@imranakram](https://github.com/imranakram) & [@rappen](https://github.com/rappen)

[XrmToolBox](http://www.xrmtoolbox.com) tools to help compose and run/test **Shuffle Schema Definitions** — XML files that define exactly what data and solutions to export or import between Dataverse environments.


---
### *Shuffle tools are now available in the XrmToolBox Tool Library!* 🥳
---

## The Three Tools

### 🏗️ Shuffle Builder
The Builder helps you **create and edit Shuffle Definition XML files** through a visual UI — no hand-coding required.

- Connects to a Dataverse environment to browse entities, attributes, and relationships
- Build `<DataBlock>` and `<SolutionBlock>` nodes by pointing and clicking
- Copy, paste, and reorder blocks
- Save `.xml` definition files that are then consumed by the Runner or Deployer
- Available in the XrmToolBox Tool Library as **`Rappen.XrmToolBox.Shuffle.Builder`**

---

### 🏃 Shuffle Runner
The Runner **executes a Shuffle Definition** — exporting data from or importing data into a connected Dataverse environment.

- Load a definition file and a data file, then hit Run
- Supports both **Export** (Dataverse → XML/CSV file) and **Import** (file → Dataverse) modes
- Multiple serialization styles: Simple, SimpleWithValue, SimpleNoId, Explicit, Text, Full
- Filter records by attribute value or supply your own FetchXML
- High-performance bulk imports using **CreateMultiple/UpdateMultiple** on Dataverse (online) or **ExecuteMultipleRequest** on-premises
- Automatic runtime detection and fallback for maximum compatibility across Dynamics CRM 9.1 and all Dataverse versions
- Generates detailed, timestamped operation logs
- Available in the XrmToolBox Tool Library as **`Rappen.XrmToolBox.Shuffle.Runner`**

---

### 🚚 Shuffle Deployer
The Deployer orchestrates **controlled deployments** of packaged Shuffle definitions across environments.

- Works with `.cdpkg` / `.cdzip` package files that bundle definition and data files together
- Select which modules within a package to deploy and run them in sequence
- Progress tracking and detailed logs at every step
- Supports **double-click launch**: associate `.cdpkg` files with XrmToolBox and the Deployer will auto-load the package on startup
- Available in the XrmToolBox Tool Library as **`Rappen.XrmToolBox.Shuffle.Deployer`**

---

## Schema Reference

All three tools are driven by a **Shuffle Definition XML file** that follows the `ShuffleDefinition.xsd` schema. You can author these files in the Builder UI or by hand. Below is a full reference of every element and attribute.

---

### `<ShuffleDefinition>` — Root element

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Timeout` | int | — | Operation timeout in minutes |
| `StopOnError` | boolean | `false` | Halt all remaining blocks if any block fails |

Contains a `<Blocks>` child holding any combination of `<SolutionBlock>` and `<DataBlock>` elements, processed in order.

---

### `<SolutionBlock>` — Import or export a solution

| Attribute | Type | Description |
|-----------|------|-------------|
| `Name` | string (required) | Unique name for this block |
| `Path` | string | Folder path to the solution file |
| `File` | string | Explicit solution filename override |

#### `<Export>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Type` | `Managed` / `Unmanaged` / `Both` / `None` | required | Solution package type to export |
| `SetVersion` | string | — | Override the solution version on export |
| `PublishBeforeExport` | boolean | `false` | Publish all customizations before exporting |
| `TargetVersion` | string | — | Target platform version for the export |

`<Settings>` (optional child of `<Export>`) — include additional settings components in the export. All boolean, default `false`:

`AutoNumbering` · `Calendar` · `Customization` · `EmailTracking` · `General` · `Marketing` · `OutlookSync` · `RelationshipRoles` · `IsvConfig`

#### `<Import>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Type` | `Managed` / `Unmanaged` / `Both` / `None` | required | Expected package type to import |
| `OverwriteSameVersion` | boolean | `true` | Import even when the same version already exists in the target |
| `OverwriteNewerVersion` | boolean | `false` | Import even when the target already has a newer version |
| `ActivateServersideCode` | boolean | required | Activate plug-ins and workflows after import |
| `OverwriteCustomizations` | boolean | required | Overwrite unmanaged customizations |
| `PublishAll` | boolean | required | Publish all after import completes |

`<PreRequisites>` — one or more `<Solution>` elements that must be present in the target before import begins:

| Attribute | Description |
|-----------|-------------|
| `Name` | Solution unique name |
| `Comparer` | Version comparison rule: `any`, `eq-this`, `ge-this`, `eq`, `ge` |
| `Version` | Required version string (used with `eq` / `ge`) |

`<PostSuccessfulImportBlocks>` — a nested `<Blocks>` element whose blocks are run only after a successful import.

---

### `<DataBlock>` — Export or import entity records

| Attribute | Type | Description |
|-----------|------|-------------|
| `Name` | string (required) | Unique name for this block |
| `Entity` | string (required) | Dataverse entity logical name |
| `Type` | `Entity` / `Intersect` | `Entity` (default) for regular tables; `Intersect` for N:N relationship tables |
| `IntersectName` | string | The intersect entity logical name — required when `Type=Intersect` |

#### `<Export>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `ActiveOnly` | boolean | `false` | Skip inactive/disabled records |

Choose one of two query modes:

**Filter-based mode** — combine filters, sorting, and an explicit attribute list:
- `<Filter Attribute="..." Operator="..." Type="string|guid|int|bool|datetime|null|not-null" Value="...">` — add as many as needed
- `<Sort Attribute="..." Type="Asc|Desc">` — add as many as needed
- `<Attributes>` containing `<Attribute Name="..." IncludeNull="false">` — **required**; defines which fields to include in the export

**FetchXML mode** — supply your own query (defines both filters and returned attributes):
- `<FetchXML>` — paste your raw FetchXML string here; mutually exclusive with the filter-based mode

#### `<Import>` (optional child)

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `Save` | `CreateUpdate` / `CreateOnly` / `UpdateOnly` / `Never` | `CreateUpdate` | Whether to create new records, update existing ones, both, or skip saving |
| `Delete` | `None` / `Existing` / `All` | `None` | `None` = no deletes; `Existing` = delete records in target not present in import; `All` = delete all target records first |
| `CreateWithId` | boolean | `false` | Preserve the source record GUID when creating records in the target |
| `UpdateInactive` | boolean | `false` | Allow updating inactive/disabled records |
| `UpdateIdentical` | boolean | `false` | Send an update call even when no field values have changed |
| `BatchSize` | int | `100` | Records per bulk operation batch. Set to `1` to disable batching. Maximum `1000`. Microsoft recommends ~100 for standard tables. |
| `Overwrite` | boolean | — | ⚠️ **Deprecated** — use `Save` instead |

> **Performance tip:** Shuffle automatically uses **CreateMultiple/UpdateMultiple** bulk operations on Dataverse (online) for maximum throughput, falling back to **ExecuteMultipleRequest** for on-premises CRM 9.1 compatibility. `BatchSize` controls how many records are grouped per API call. The default of 100 aligns with Microsoft's recommendation for standard tables. Larger values (up to 1000) may improve throughput for simple operations. For records with complex plug-ins, reduce the value or set to `1` to disable batching entirely.

`<Match>` — controls how the importer finds existing target records to decide whether to create or update:

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `PreRetrieveAll` | boolean | `false` | Fetch all existing target records up-front before import starts; improves performance for large imports on small-to-medium target datasets |

Add one or more `<Attribute Name="..." Display="...">` children — these are the fields used to match incoming records against existing target records. `Display` is an optional alternate attribute used for the matched value in log output.

#### `<Relation>` (optional, repeatable)

Associates records from another `<DataBlock>` — used for N:N relationships or populating lookups:

| Attribute | Type | Description |
|-----------|------|-------------|
| `Block` | string (required) | Name of the `DataBlock` that provides the related records |
| `Attribute` | string (required) | Lookup attribute on this entity |
| `PK-Attribute` | string | Primary key attribute on the related block (optional; defaults to the block entity's primary key) |
| `IncludeNull` | boolean | Include the relation even when the lookup value is null |

---

## Recent Changes

### CreateMultiple/UpdateMultiple bulk operation support
Import operations now use **CreateMultiple** and **UpdateMultiple** bulk messages on Dataverse (online) for significantly improved performance — up to 2-4× faster than ExecuteMultipleRequest for large datasets. The implementation includes:

- **Runtime capability detection** — queries `sdkmessagefilter` to detect if bulk operations are supported for each entity type
- **Per-entity caching** — capability checks are cached for the lifetime of the import run
- **Graceful fallback** — automatically falls back to ExecuteMultipleRequest for on-premises CRM 9.1 or entities that don't support bulk operations
- **Full backwards compatibility** — works seamlessly with Dynamics CRM 9.1 on-premises and all Dataverse versions
- **Optimized default batch size** — reduced from 200 to 100 records per batch to align with Microsoft's recommendation for CreateMultiple/UpdateMultiple

No configuration changes required — the system automatically detects the target environment's capabilities and selects the best available API.

### Multi-Select OptionSet support
Export and import of Multi-Select OptionSet (OptionSetValueCollection) fields now works correctly. Previously, exported data.xml contained the literal string "OptionSetValueCollection" instead of actual values.

### ExecuteMultipleRequest batching (legacy)
Import operations on on-premises Dynamics CRM 9.1 use `ExecuteMultipleRequest` for batching (Create, Update, Delete operations). Dataverse (online) environments automatically use the newer and faster CreateMultiple/UpdateMultiple APIs instead. Configurable via the `BatchSize` attribute on the Import element (default: 100, max: 1000). Set to 1 to disable batching. The Shuffle Builder UI includes a "Batch size" field.

### Deterministic XML export ordering
Entity attributes are now sorted alphabetically during export, eliminating spurious diffs in version control when re-exporting unchanged data.

### Bug fixes and performance improvements
- Fixed off-by-one error in CSV/text export that could cause an IndexOutOfRangeException
- Metadata lookups (PrimaryIdAttribute) hoisted out of inner loops to reduce overhead
- Replaced O(n) list searches with HashSet for attribute deduplication during import
- Replaced O(n²) attribute filtering in SelectAttributes with single-pass LINQ approach
- Update failures now log the exception message for easier diagnostics

---

## Home page
https://jonasr.app/shuffle/


## Articles
https://jonasr.app/2017/04/devops-i/ <br/>
These describe the outline of the tools in this repository.

https://saralagerquist.com/2019/12/02/mvp-advent-calendar-transport-data-between-environments-with-saras-favorite-tool/<br/>
Sara Lagerquist explains an example how to use it.
