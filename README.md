# Xrm.Shuffle for [XrmToolBox](https://www.xrmtoolbox.com/)

### Shuffle Builder 👷‍♀️, Shuffle Runner 🏃 and Shuffle Deployer 🚚
*Empower yourself to achieve more.*

Created by [@rappen](https://github.com/rappen)
<br/>
Improving by [@imranakram](https://github.com/imranakram) & [@rappen](https://github.com/rappen)

[XrmToolBox](http://www.xrmtoolbox.com) tools to help compose and run/test **Shuffle Schema Definitions**.


---
### *Shuffle tools are now available in the XrmToolBox Tool Library!* 🥳
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
- Replaced O(n^2) attribute filtering in SelectAttributes with single-pass LINQ approach
- Update failures now log the exception message for easier diagnostics

## Home page
https://jonasr.app/shuffle/


## Articles
https://jonasr.app/2017/04/devops-i/ <br/>
These describe the outline of the tools in this repository.

https://saralagerquist.com/2019/12/02/mvp-advent-calendar-transport-data-between-environments-with-saras-favorite-tool/<br/>
Sara Lagerquist explains an example how to use it.
