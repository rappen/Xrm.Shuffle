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
| `DeferStateAndOwner` | boolean | `false` | Strip `statecode`, `statuscode`, and `ownerid` from records during import and apply them in a second pass using bulk operations. **Significantly improves performance** when importing data that includes state/owner attributes. |
| `Overwrite` | boolean | — | ⚠️ **Deprecated** — use `Save` instead |

> **Performance tip:** Shuffle automatically uses **CreateMultiple/UpdateMultiple/UpsertMultiple** bulk operations on Dataverse (online) for maximum throughput, falling back to **ExecuteMultipleRequest** for on-premises CRM 9.1 compatibility, and further falling back to individual operations for CRM 8.x and older. `BatchSize` controls how many records are grouped per API call. The default of 100 aligns with Microsoft's recommendation for standard tables. Larger values (up to 1000) may improve throughput for simple operations. For records with complex plug-ins, reduce the value or set to `1` to disable batching entirely.

> **UpsertMultiple optimization:** When importing with `Save="CreateUpdate"`, `CreateWithId="true"`, and match attributes defined, Shuffle automatically uses **UpsertMultiple** on Dataverse (eliminating the need for `PreRetrieveAll` queries). This can achieve **2-3× faster imports** by letting Dataverse decide whether to create or update each record. No configuration required — the system detects when Upsert is optimal and uses it automatically.

> **DeferStateAndOwner optimization:** When `DeferStateAndOwner="true"`, records with `statecode`, `statuscode`, or `ownerid` attributes are still imported using bulk operations — these attributes are temporarily stripped, the records are batched, and then state/owner changes are applied in a second pass. This can achieve **3-5× performance improvement** on datasets where most records include state or owner information. Use this when migrating data between environments where preserving state/owner is important.

`<Match>` — controls how the importer finds existing target records to decide whether to create or update:

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `PreRetrieveAll` | boolean | `false` | Fetch all existing target records up-front before import starts. **Note:** When UpsertMultiple is available (Dataverse + `CreateWithId="true"`), this flag is automatically bypassed since Upsert eliminates the need for pre-retrieval queries. On CRM 9.1 on-premises or older, this flag still provides significant performance benefits for large imports on small-to-medium target datasets. |

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

### UpsertMultiple bulk operation support
Import operations with `Save="CreateUpdate"` and `CreateWithId="true"` now automatically use **UpsertMultiple** on Dataverse (online), which provides significant performance benefits:

- **Eliminates PreRetrieveAll queries** — when Upsert is available, the system no longer needs to query existing records to determine create vs. update. Dataverse makes this decision automatically.
- **Single batch for all records** — instead of separate batches for creates and updates, all records go through a unified Upsert batch.
- **Same robust fallback chain** — automatically falls back through multiple tiers:
  1. **UpsertMultiple** (Dataverse online only)
  2. **ExecuteMultipleRequest with UpsertRequest** (CRM 9.1 on-premises)
  3. **Individual UpsertRequest** (fallback)
  4. **Individual Create/Update** (CRM 8.x and older)

**When Upsert is used automatically:**
- `Save="CreateUpdate"` (not CreateOnly or UpdateOnly)
- `CreateWithId="true"` (records have their source GUID preserved)
- `<Match>` attributes are defined
- `Delete` is set to `None` (no deletion of existing records)

**Example configuration:**
```xml
<DataBlock Name="Contacts" Entity="contact">
  <Import Save="CreateUpdate" CreateWithId="true" BatchSize="100" DeferStateAndOwner="true">
    <Match>
      <Attribute Name="contactid" />
    </Match>
  </Import>
</DataBlock>
```

**Performance impact:** For environment-to-environment migrations with `CreateWithId="true"`, imports can be **2-3× faster** because:
1. No `PreRetrieveAll` query overhead
2. No per-record match queries
3. Single unified batch instead of separate create/update batches

**Backwards compatibility:** Full support maintained for all CRM/Dataverse versions. The system automatically detects capabilities and selects the optimal API path.

### DeferStateAndOwner optimization for high-performance imports
A new **`DeferStateAndOwner`** attribute on `<Import>` enables a two-pass import strategy that dramatically improves performance when importing records with `statecode`, `statuscode`, or `ownerid` attributes:

- **Pass 1**: Strip state/owner attributes → records become batchable → imported via CreateMultiple/UpdateMultiple
- **Pass 2**: Apply state/owner changes in bulk using UpdateMultiple and batch Assign operations

**Performance impact**: Datasets that were previously ~7% batchable (due to state/owner attributes) can now achieve **~95%+ batchable rate**, resulting in **3-5× faster imports**. Enabled via:

```xml
<Import Save="CreateUpdate" DeferStateAndOwner="true" BatchSize="100">
```

This feature is opt-in (default: `false`) to maintain full backwards compatibility. Ideal for environment-to-environment data migrations where preserving record state and ownership is required.

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
