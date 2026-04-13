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
- Batches import operations using `ExecuteMultipleRequest` for high-performance large-dataset imports
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
| `BatchSize` | int | `200` | Records per `ExecuteMultipleRequest` batch. Set to `1` to disable batching. Maximum `1000`. |
| `Overwrite` | boolean | — | ⚠️ **Deprecated** — use `Save` instead |

> **Performance tip:** `BatchSize` controls how many Create/Update/Delete operations are grouped into a single API call. Larger values significantly improve throughput for large imports. If records trigger complex plug-ins that need to run individually, reduce the value or set it to `1` to disable batching entirely.

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

### Multi-Select OptionSet support
Export and import of Multi-Select OptionSet (OptionSetValueCollection) fields now works correctly. Previously, exported data.xml contained the literal string "OptionSetValueCollection" instead of actual values.

### ExecuteMultipleRequest batching
Import operations (Create, Update, Delete) are now batched using `ExecuteMultipleRequest` for significantly improved performance on large datasets. Configurable via the `BatchSize` attribute on the Import element (default: 200, max: 1000). Set to 1 to disable batching. The Shuffle Builder UI includes a new "Batch size" field.

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
