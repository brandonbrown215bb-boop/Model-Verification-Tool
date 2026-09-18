# ADR-0001: Native OLE Spreadsheet Link Repointing and Dynamic Calculator Tracing

## Status
**Accepted** (2026-09-15)

## Context
The Model Verification Tool was initially planned with the assumption that applying calculator outputs to an Autodesk Inventor assembly (Mode B: "Apply Calculator to Copy, Then Inspect") would require:
1. Parsing `Sheet1` as a fixed 4-column table (`Parameter`, `Value`, `UM`, `Comments`).
2. Matching parsed parameters against Inventor model and user parameters using a multi-tier naming heuristic.
3. Writing approved values one-by-one into Inventor using `Parameter.Expression = "..."` via COM.
4. Attempting to infer component suppression states using regular expression mappings (e.g. `Part_<num>_<suffix>` -> `<PartNum>:<Suffix>`).

### Empirical Discoveries & Inventory Audit
An in-depth empirical investigation of live Autodesk Inventor assemblies (`391-10006-023.iam`, `091-30102-458.ipt`) and an automated audit of all **56 engineering workbooks across 21 product families** in `Calc_Sheet_Inventory` (`391-10002` through `391-10033`) disproved these assumptions:

1. **Native OLE Spreadsheet Parameter Linkage:**
   - Inventor models at Johnson Controls are fundamentally built around Inventor's native **"Link to Spreadsheet"** capability (`Parameters -> Link`).
   - The top-level assembly `391-10006-023.iam` and child IPT files contain **OLE link descriptors** (`ReferencedOLEFileDescriptors`, type 3331) pointing directly to `Calc_10006_023.xls`.
   - The assembly contains **zero user parameters**; instead, all 618 driving parameters (`IW`, `IH`, `TopBhdThk`, `BtmBhdThk`, `SegStart`, `CoilsHigh`, `Part_...`) exist inside Inventor as **`TableParameters`** (`kTableParameterObject`, COM type `50349312`).
   - CAD model parameters (`d8`, `d10`, `d14`, etc.) and component sketch dimensions (`Thickness = BtmBhdThk`, `d0 = T1BHD_Width`) are parametric equations directly consuming these `TableParameters`.
   - The assembly's master iLogic rule `Name1` directly reads these linked parameters as variables to evaluate component suppression.

2. **The Transmission Vehicle is the Spreadsheet File:**
   - In production, designers do not inject parameters individually via CAD macros. When a calculation changes, the Excel file is recalculated and saved on disk.
   - When Inventor refreshes its linked spreadsheet, Inventor's internal constraint solver natively updates sketch dimensions, feature parameters (hole diameters, pattern spacings), and workplane offsets throughout the entire assembly hierarchy.
   - Attempting to overwrite `TableParameters` individually via `p.Expression = "..."` is blocked as read-only or risks corrupting the live table link.

3. **Workspace Isolation & OLE Link Path Trapping:**
   - When files are copied to a disposable workspace (`C:\Temp\InventorValidator\<SessionId>\`), the copied `.iam` and `.ipt` files still internally retain the absolute path to the **source** calculator in Vault.
   - If Inventor reloads links without repointing, it reads the original Vault file rather than the disposable copy. The disposable copy's OLE links must be explicitly repointed using Inventor's `FileDescriptor.ReplaceReference(...)` API.

4. **Multi-Archetype Structure Across 56 Workbooks:**
   - **`Sheet1` Archetype 1 (Standard 4-Column Table, ~84%):** Found in Coil Bulkheads, Filters, Transitions, Split Sections. Columns: `Parameter`, `Value`, `UM`, `Comments`.
     - *Variations:* Missing header rows (e.g. `391_10006_021.xls`), non-standard header titles (`"Parameters"`, `"Units"`).
   - **`Sheet1` Archetype 2 (Multi-Table Fan/Base Skids, ~16%):** Found in the `391-10004-xxx` family. `Sheet1` contains side-by-side tables:
     - Columns A–D: Hole Schedule 1 (`HOLE`, `XDIM`, `YDIM`, `DESCRIPTION`).
     - Columns F–I: Hole Schedule 2.
     - Columns K–M: Driving Dimensions (`NAME`, `VALUE`, `COMMENT` — e.g. `D1`, `D6 = 38.5` overall width, `D7 = 34.2` overall length, `D9`, `D10`, `D11` height).
     - Columns O–R: Cut List / Structural BOM.
     - *A parser expecting only Columns A–D completely misses the driving parameters on fan skids.*
   - **`Channel Loc` Structure:** All 56 workbooks define the 4 primary channel groups (`FLOOR CHANNELS`, `ROOF CHANNELS`, `SOUTH WALL CHANNELS`, `NORTH WALL CHANNELS`). However, column offsets vary (Column A is frequently blank, group headers appear in Column F or G, and extra descriptive columns exist). Inactive rows (`Qty = 0`, `Z = 0`) and legacy `#REF!` formulas must be filtered dynamically.

---

## Decision

1. **Native OLE Link Repointing Over Direct Parameter Injection:**
   In Mode B ("Apply Mode"), the application will:
   - Apply user input changes or overrides to the **disposable copy of the Excel workbook** in `C:\Temp\...`.
   - Recalculate and save the disposable workbook.
   - Open the copied assembly in dedicated Inventor and iterate through `ReferencedOLEFileDescriptors` across the assembly and referenced IPT documents.
   - Call `FileDescriptor.ReplaceReference(disposableXlsPath)` to repoint the link to the local disposable copy.
   - Trigger Inventor to update the spreadsheet links (`TableParameters`), allowing Inventor's native engine to update sketch dimensions, features, and plane offsets automatically.
   - Execute master iLogic rule `Name1` to update occurrence suppressions.
   - Rebuild the assembly (`asmDoc.Update2(true)`).

2. **Dynamic Multi-Region Parser for `Sheet1`:**
   Replace the hardcoded 4-column parser with a dynamic table-detection parser that:
   - Scans the sheet for known parameter table signatures (`Parameter / Value` OR `NAME / VALUE`).
   - If `HOLE / XDIM / YDIM` is detected in Columns A–D (Archetype 2 / Fan Skids), extracts the driving dimension parameters from Columns K–M, while also capturing the hole schedules for diagnostic cross-referencing.
   - Handles headerless sheets by detecting data rows starting with valid parameter identifier patterns.

3. **Header-Anchored Dynamic Parser for `Channel Loc`:**
   Replace fixed row/column indexing with anchor-based scanning:
   - Locate the 4 universal group blocks (`FLOOR CHANNELS`, `ROOF CHANNELS`, `SOUTH WALL CHANNELS`, `NORTH WALL CHANNELS`) by text search.
   - Dynamically identify column indices for `Channel Name`, `Z_LOCATION`, `ARRAY_OFFSET`, `ARRAY_QTY`, and `ARRAY_SPACING` by header text, regardless of leading empty columns or extra description columns.
   - Silently filter out inactive template rows (`Qty <= 0`, `Z <= 0`, `Spacing <= 0`) and flag legacy `#REF!` rows as skipped.

4. **Refactor Phase 4 Scope:**
   - Remove the requirement to manually write parameter expressions (`p.Expression = ...`) via COM.
   - Reframe Phase 4 as: **Disposable Calculator Input Application, OLE Link Repointing, Native Link Refresh, iLogic Execution, and Geometry Delta Audit**.

---

## Consequences

### Positive
- **High Fidelity:** Relies on Autodesk Inventor’s native parametric update engine, eliminating risk of breaking parametric equations, severing linked tables, or encountering COM unit conversion bugs.
- **Universal Compatibility:** Dynamic parsing natively handles all 56 surveyed workbooks across 21 product families and future releases without hardcoded row/column assumptions.
- **True Non-Destructive Isolation:** Repointing the OLE link in the disposable copy guarantees that Inventor never reads or writes to the original Vault files.
- **Architectural Simplicity:** Drastically reduces code complexity by eliminating brittle COM parameter write routines.

### Technical Debt & Cleanup Required in Previous Phases
The adoption of this architecture requires cleaning up and retrofitting implementations from previous phases:

1. **Phase 2 (Excel Parsers):**
   - **`Sheet1TabParser.cs`:** Must be rewritten to support Archetype 2 (multi-table layouts such as `391-10004` fan skids with parameters in Columns K–M) and headerless sheets.
   - **`ChannelLocTabParser.cs`:** Must be refactored to use dynamic column index resolution (handling blank Column A, extra description columns, and group headers in Columns F/G) and filter inactive template rows (`Qty == 0`, `Z == 0`).

2. **Phase 3 (Inventor Inventory & Parameter Matching):**
   - **`WorkspaceManager.cs`:** Must include an OLE link repointing routine (`ReplaceReference`) when preparing the disposable workspace, so the copied CAD files point to the copied Excel file.
   - **`InventorModelInventoryService.cs`:** Must properly enumerate `TableParameters` (COM type `50349312`) across the assembly and child parts, rather than assuming parameters are standard `UserParameters`.
   - **`ParameterMatchingService.cs`:** 
     - Remove the obsolete regex-based suppression guessing (`TryMatchSuppression`) that was previously flagged in Section 3.1.
     - Refactor the "Safe-to-Write" classification logic: since parameters are driven by the spreadsheet, the concept of "writing directly to CAD parameters" is obsolete. Discrepancy analysis becomes a comparison between the calculator outputs and the live CAD state.
