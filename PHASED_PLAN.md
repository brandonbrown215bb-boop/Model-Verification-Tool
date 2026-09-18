# Final Implementation Plan



## 1. Product definition



Build a lightweight Windows desktop application that validates an Inventor assembly against its associated Excel engineering calculator.



The application will support two workflows:



### Mode A — Inspect Current Model



- Open a disposable copy of the current IAM.

- Recalculate the selected calculator.

- Do not apply calculator values to Inventor.

- Compare current model geometry against `Channel Loc`.

- Show parameter, suppression, and geometry discrepancies.



This should be the default because it answers: **“Does the existing model match the calculator?”**



### Mode B — Apply Calculator to Copy, Then Inspect



- Create a disposable assembly/calculator workspace.

- Recalculate and save the disposable Excel workbook.

- Repoint assembly and child part OLE spreadsheet references (`FileDescriptor.ReplaceReference`) to the disposable Excel workbook copy.

- Trigger Inventor to refresh linked `TableParameters`, natively driving sketch dimensions, feature parameters (hole diameters, pattern spacings), and plane offsets across the model.

- Run the assembly’s master iLogic rule (`Name1`) to update component suppressions.

- Update/rebuild assembly.

- Compare the resulting geometry against `Channel Loc`.



This answers: **“If the calculator is applied, does the resulting model match its outputs?”**



Neither mode modifies the source Vault workspace.



---



# 2. Confirmed target environment



- Windows 11 x64

- Autodesk Inventor 2020 and 2024

- Microsoft Excel 2016/365

- Calculator formats:

&#x20; - `.xls`

&#x20; - `.xlsx`

- Models are selected from an already-downloaded local Vault workspace.

- No Vault SDK, checkout, check-in, or Vault authentication

- Approximately 5–10 users

- One assembly/session at a time

- English-only UI

- Portable deployment

- One self-contained executable, with no installer or admin rights



The executable will still require locally installed Excel and Inventor. “Single file” applies to the delivered application, not Autodesk or Office dependencies.



---



# 3. Important corrections to the investigation conclusions



The local investigation is sufficient to proceed, but two findings should not be implemented literally.



## 3.1 Do not infer suppression mappings from suffixes



The report suggests:



```text

Part_<PartNum>_<Suffix> → <PartNum>:<Suffix>

```



That works for some rows, but the actual iLogic rule proves that it is not generally valid.



Examples include:



```vb

Part_091_30102_459

&#x20;   → 091-30102-459:1

&#x20;   → 091-30102-460:1

&#x20;   → 091-30102-485:1

&#x20;   → 091-30102-486:1

```



and:



```vb

Part_091_30102_496_2

&#x20;   → 091-30102-517:1

```



Therefore, the application must:



1. Apply suppression control values to existing Inventor parameters when safe.

2. Run the existing `Name1` iLogic rule.

3. Read the resulting occurrence states.

4. Never recreate the suppression map with a general regex.



The iLogic rule remains the source of truth.



## 3.2 Do not use ±0.030 in as the primary hole-diameter tolerance



The investigated parts contain hole diameters such as:



- `0.203 in`

- `0.218 in`

- `0.312 in`



A diameter tolerance of ±`0.030 in` would not distinguish `0.203` from `0.218`.



Use:



- Default diameter tolerance: ±`0.002 in`

- Expand to ±`0.005 in` only if model precision requires it

- If expected diameter cannot be established, match by:

&#x20; - Referenced occurrence

&#x20; - Expected position

&#x20; - Array direction

&#x20; - Count

&#x20; - Spacing

&#x20; - Repeated diameter cluster



Diameter should be a candidate filter, not the sole matching key.



## 3.3 Validate only worksheet-defined dimensions



`Channel Loc` defines:



- Floor/roof: X array position and Z location

- South/north wall: Y array position and Z location



It does not necessarily define the hole center’s transverse coordinate on the flange.



Therefore:



- Classification should use the dimensions actually defined by the worksheet.

- Full `ΔX`, `ΔY`, `ΔZ`, and 3D distance may still be shown as diagnostics.

- An undefined transverse-axis offset must not cause an otherwise valid row to fail.



For example, a wall-channel row should normally be classified using `ΔY` and `ΔZ`, not an unrelated flange-depth `ΔX`.



## 3.4 Native OLE spreadsheet links vs manual COM parameter injection



The assembly and detail part models in production do not rely on an external automation script writing CAD parameters one-by-one (`Parameter.Expression = value`).

Empirical investigation proved that:

- The top assembly (`391-10006-023.iam`) and child IPT files contain native **OLE link descriptors** (`ReferencedOLEFileDescriptors`, type 3331) pointing directly to the calculator workbook (`Calc_...xls`).

- All 618 driving parameters in `Sheet1` enter Inventor natively as **`TableParameters`** (`kTableParameterObject`, COM type `50349312`).

- Sketch dimensions (e.g. `Thickness = BtmBhdThk`, `d0 = T1BHD_Width`), feature parameters, and plane offsets (`d14 = IW`, `d8 = Side_BH_to_Shell_Clearance`) are parametric equations referencing these `TableParameters`.

- Attempting to write directly to `TableParameters` via COM is either blocked as read-only or risks corrupting the live table link.

- In disposable workspaces (`C:Temp...`), copied CAD files retain internal absolute OLE paths pointing to the source Vault workbook. The application must repoint these OLE references to the copied workbook using `FileDescriptor.ReplaceReference(...)`.

- Once repointed and refreshed, Inventor’s native constraint solver updates all sketch dimensions, hole arrays, and plane offsets across the entire assembly hierarchy without fragile manual injection.



## 3.5 Dynamic multi-archetype parsing across 56+ workbooks



An automated audit of all 56 engineering workbooks in `Calc_Sheet_Inventory` across 21 product families (`391-10002` through `391-10033`) established the following universal requirements:

- **100% Tab Presence:** All 56 workbooks contain `Sheet1`, `Channel Loc`, and `Data`.

- **Two `Sheet1` Archetypes:**

  1. *Archetype 1 (Standard 4-Column Table, ~84%):* Columns `Parameter`, `Value`, `UM`, `Comments`. (Occasional variations: headerless or title in cell A1).

  2. *Archetype 2 (Multi-Table Fan/Base Skids, ~16% — e.g. `391-10004-xxx`):* `Sheet1` contains side-by-side tables: Columns A–I are hole schedules (`HOLE`, `XDIM`, `YDIM`, `DESCRIPTION`), while Columns K–M hold the actual driving parameters (`NAME`, `VALUE`, `COMMENT`), and Columns O–R contain the cut list BOM.

  - *Rule:* The parser must dynamically locate the parameter table signature (`Parameter/Value` or `NAME/VALUE`) across the sheet rather than assuming Columns A–D.

- **`Channel Loc` Dynamic Group & Column Resolution:**

  - All 56 workbooks define the 4 primary groups: `FLOOR CHANNELS`, `ROOF CHANNELS`, `SOUTH WALL CHANNELS`, `NORTH WALL CHANNELS`.

  - Column positions vary across workbooks (Column A is frequently blank, group headers appear in Column F/G, extra description columns exist).

  - The parser must locate group anchors dynamically and map columns by header text (`Z_LOC*`, `OFFSET*`, `QTY*`, `SPAC*`), filtering out inactive template rows (`Qty <= 0`, `Z <= 0`) and legacy `#REF!` rows.



---



# 4. Recommended technology



## Application



- C#

- .NET 8

- WPF

- x64

- MVVM-lite without a large framework

- Self-contained, single-file publish



Example publishing profile:



```xml

<PropertyGroup>

&#x20; <TargetFramework>net8.0-windows</TargetFramework>

&#x20; <UseWPF>true</UseWPF>

&#x20; <RuntimeIdentifier>win-x64</RuntimeIdentifier>

&#x20; <SelfContained>true</SelfContained>

&#x20; <PublishSingleFile>true</PublishSingleFile>

&#x20; <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>

&#x20; <PublishTrimmed>false</PublishTrimmed>

</PropertyGroup>

```



Do not enable trimming because reflection, COM, and dynamic automation can break under aggressive trimming.



## COM strategy



Use thin application-owned wrappers around late-bound COM automation.



This avoids distributing a build tied tightly to only the Inventor 2020 or 2024 primary interop assemblies.



Create:



- `ExcelAutomationSession`

- `InventorAutomationSession`

- `ILogicAutomationService`

- `ComReleaseScope`



All Excel and Inventor COM calls must remain on dedicated STA threads. Do not pass live COM objects between UI and worker threads.



## Theme



- Detect the current Windows application theme at startup.

- Provide:

&#x20; - System

&#x20; - Light

&#x20; - Dark

- Store the override under `%LocalAppData%`.

- Use WPF resource dictionaries for colors and controls.



WPF does not automatically provide a complete Windows dark theme, so this must be implemented explicitly.



---



# 5. High-level architecture



Keep the application as one deployable executable, but organize the source into clear internal modules.



```text

InventorValidator.exe

│

├── UI

│   ├── Session setup

│   ├── Parameter preview

│   ├── Validation results

│   └── Settings/theme

│

├── Session

│   ├── Workspace creation

│   ├── Session manifest

│   └── Cleanup

│

├── Excel

│   ├── Dedicated Excel process

│   ├── Calculation

│   ├── Data parser

│   ├── Sheet1 parser

│   └── Channel Loc parser

│

├── Inventor

│   ├── Version discovery

│   ├── Dedicated process

│   ├── Document inventory

│   ├── Safe parameter application

│   ├── iLogic execution

│   ├── Geometry extraction

│   └── ClientGraphics overlays

│

├── Validation

│   ├── Parameter matching

│   ├── Suppression verification

│   ├── Expected-point generation

│   ├── Actual-hole matching

│   └── Result classification

│

└── Infrastructure

&#x20;   ├── Preferences

&#x20;   ├── COM cleanup

&#x20;   ├── Cancellation

&#x20;   └── Session diagnostics

```



No database, web service, authentication, or background server is needed.



---



# 6. Session workflow



## Step 1 — Select files



The main window asks for:



- IAM path

- Calculator path

- Mode:

&#x20; - Inspect Current Model

&#x20; - Apply Calculator to Copy, Then Inspect

- Inventor version:

&#x20; - Automatic

&#x20; - Inventor 2020

&#x20; - Inventor 2024



Remember recent folders, not necessarily full proprietary filenames unless desired.



## Step 2 — Preflight validation



Check:



- IAM exists and has `.iam` extension.

- Calculator exists and is `.xls` or `.xlsx`.

- Selected Inventor version is installed.

- Excel is installed.

- Source folder is readable.

- At least 1–2 GB of temporary disk space is available.

- Workbook contains:

&#x20; - `Data`

&#x20; - `Sheet1`

&#x20; - `Channel Loc`

- IAM is not already inside the application’s temporary workspace.

- Source calculator and IAM are not modified by the application.



Warnings should not stop the workflow unless a required engine or top-level file is unusable.



## Step 3 — Create disposable workspace



Create:



```text

C:TempInventorValidator<SessionId>

```



Copy the complete IAM directory recursively while preserving relative structure.



After copying:



- Clear `ReadOnly` on copied files only.

- Preserve original source attributes.

- Record source-to-copy paths in a session manifest.

- Detect and avoid junction/reparse-point loops.

- Verify the copied IAM and calculator exist.

- Never modify source files.



The investigated reference package is approximately 95 MB, so a directory-level copy is acceptable and simpler than custom Pack and Go logic.



## Step 4 — Recalculate the calculator



Start a dedicated Excel process:



```text

Visible = false

DisplayAlerts = false

AskToUpdateLinks = false

EnableEvents = false

```



Also set Office automation security to disable macros during automated opening. The investigated workbooks contain no VBA, but this is a defensive measure.



Open the copied workbook with external link updates disabled.



Run:



1. `Calculate`

2. `CalculateFull`

3. `CalculateFullRebuild`



Use the minimum operation demonstrated to be reliable, with `CalculateFullRebuild` available as the safe first-release default.



Wait until:



```text

Application.CalculationState == xlDone

```



Apply a configurable timeout, initially 120 seconds.



Capture:



- Cell values

- Formulas

- Excel error values

- Named ranges

- Data validation descriptions

- `Error_Check`

- Calculation completion state



Close only the workbook and Excel process created by the application.



## Step 5 — Parse calculator data



### `Data`



Find fields by header text rather than fixed row numbers.



Extract:



- Input parameter

- Current value

- Description

- Allowed values from column D

- Error-check value from column E

- Workbook-level `Error_Check`



### `Sheet1`



Find columns by:



```text

Parameter

Value

UM

Comments

```



For each row:



- Preserve original parameter name.

- Create a trimmed normalized name.

- Record raw value and displayed value.

- Record unit.

- Record formula error.

- Classify the row.



Proposed categories:



```text

Dimension

UnitlessCount

SuppressionControl

FeatureControl

Property

Informational

Unknown

```



Classification rules should be conservative. A row being named does not mean it is safe to write.



### `Channel Loc`



Locate groups by normalized header text:



- Floor Channels

- Roof Channels

- South Wall Channels

- North Wall Channels



Do not rely only on fixed row numbers.



Parse:



- Channel name

- Z location

- Array axis

- Array offset

- Quantity

- Spacing

- Referenced part

- Formula-error status



Rows containing `#REF!`, `#VALUE!`, invalid quantities, or missing numeric fields become:



```text

Skipped – Invalid Calculator Row

```



The rest of the sheet continues processing.



## Step 6 — Launch dedicated Inventor



Use a visible, application-owned Inventor process.



### Inventor 2020



The registered ProgID points to Inventor 2020, so `Activator.CreateInstance` can be used.



### Inventor 2024



Do not assume the unversioned ProgID selects 2024.



At runtime:



1. Inspect versioned Inventor ProgIDs in the registry.

2. Prefer a confirmed version-specific ProgID if available.

3. Otherwise launch:

&#x20;  ```text

&#x20;  Inventor.exe /Automation

&#x20;  ```

4. Bind to the new process’s ROT object.

5. Verify the returned application version and process ID before opening files.



The 2024 path must have an automated regression test. The investigation demonstrated the likely mechanism but the implementation must verify it consistently on target PCs.



Track:



- Process ID

- Inventor version

- Documents opened by this session

- Whether the process was created by the application



Never call `Quit()` on an Inventor process not created by the application.



## Step 7 — Open copied IAM and inventory model



Open the copied IAM and wait for document readiness.



Recursively inventory:



- Documents

- Occurrence paths

- Occurrence names

- Part numbers

- Suppression states

- Model states/LOD representations

- User parameters

- Model parameters

- Reference parameters

- Parameter expressions

- Evaluated values

- Units

- iProperties

- Work planes

- Features

- Hole features

- Pattern features



The representative assembly has no top-level user parameters and approximately 474 model parameters. The code must not assume that user parameters exist.



## Step 8 — Match calculator outputs



Build candidate matches using:



1. Exact case-sensitive parameter name

2. Exact case-insensitive parameter name

3. Trimmed name

4. Hyphen/underscore normalization



These weaker matches should not silently authorize writes.



Each result gets one of:



```text

Unique Exact Match

Unique Normalized Match

Ambiguous Match

No Match

Read-Only Match

Expression-Driven Match

Informational Only

```



### Safe-write policy



Automatically writable:



- User parameter with a literal expression

- Model parameter with a literal numeric expression

- Compatible units

- Unique match

- Not driven or reference-only



Not automatically writable:



- Reference parameters

- Formula-driven model parameters

- Ambiguous matches

- Normalized-only matches without approval

- Parameters with incompatible units

- Informational outputs

- Occurrence properties without a demonstrated write path



Use Inventor `Parameter.Expression` rather than the raw internal `Value` property:



```text

83.000 in

27 ul

```



This avoids mistakes caused by Inventor’s internal length unit representation.



## Step 9 — Show preview



For Apply mode, show:



| Excel parameter | Excel value | Unit | Target document | Target parameter | Current expression | Current value | Proposed action |

|---|---:|---|---|---|---|---:|---|

| `IH` | 118 | in | Top IAM | `IH` | `118 in` | 118 | Apply |

| `d69` | 41.413 | in | Child IPT | `d69` | `Support_Loc4` | 41.413 | Compare only |

| `Part_...` | 1 | ul | Top IAM | Control parameter | `0 ul` | 0 | Apply, then iLogic |

| `Unknown` | 4.25 | in | — | — | — | — | Warning |



Allow session-only manual overrides.



An override must:



- Be visually marked.

- Retain the original Excel value in the table.

- Be included in validation results.

- Not write back to the original calculator.

- Require confirmation before Apply.



## Step 10 — Apply, iLogic, and rebuild



Apply mode sequence:



1. Capture before-state snapshot of parameters and suppressions.

2. Recalculate and save the disposable Excel workbook copy.

3. Repoint all OLE spreadsheet links in copied IAM and child IPTs (`FileDescriptor.ReplaceReference`) to the disposable Excel workbook copy.

4. Trigger Inventor to refresh linked `TableParameters`, automatically propagating updated values to sketch dimensions, feature parameters, and plane offsets.

5. Verify the required `iLogic` representation.

6. Run `Name1`.

7. Update/rebuild assembly (`asmDoc.Update2(true)`).

8. Wait for Inventor to finish.

9. Capture after-state snapshot.

10. Read actual suppression states.

11. Continue to geometry extraction.



### Inventor 2020 representation



Verify that the Level of Detail named:



```text

iLogic

```



exists.



### Inventor 2024 representation



Check whether migration exposes it as:



- A compatibility LOD

- A Model State named `iLogic`



If `Name1` itself successfully activates it, continue. If not, stop Apply mode with a clear rule compatibility error. Do not rewrite the iLogic rule automatically.



### iLogic invocation



Obtain the iLogic add-in automation object and invoke:



```text

Name1

```



If `Name1` does not exist:



- List available top-level rules.

- Let the user select one for that session.

- Remember selection by assembly family only if desired.



`Name4` is not required; equivalent camera behavior can be handled directly through the Inventor API.



---



# 7. Geometry extraction design



## 7.1 Expected arrays



Generate expected positions using:



```text

Position[i] = Offset + i × Spacing

i = 0 ... Quantity - 1

```



Reject or skip rows where:



- Quantity is not a positive integer.

- Spacing is invalid.

- Offset is invalid.

- Z is invalid.

- Referenced part is missing.

- Any required cell is an Excel error.



## 7.2 Expected coordinate adapters



Use a separate adapter for each group.



### Floor



Authoritative dimensions:



- X = array position

- Z = `Z_LOCATION`



Y is derived only for visualization and candidate filtering.



### Roof



Authoritative dimensions:



- X = array position

- Z = `Z_LOCATION`



Use roof occurrence/face orientation for candidate filtering.



### South wall



Authoritative dimensions:



- Y = array position

- Z = `Z_LOCATION`



X is derived from the wall occurrence or assembly width only for display.



### North wall



Authoritative dimensions:



- Y = array position

- Z = `Z_LOCATION`



X is diagnostic unless explicitly defined by a future workbook schema.



Store both:



```text

ValidatedAxes

DiagnosticAxes

```



## 7.3 Actual-hole extraction



For active referenced occurrences:



1. Inspect `HoleFeature` objects.

2. Inspect `RectangularPatternFeature` elements.

3. Fall back to cylindrical B-Rep faces.

4. Extract:

&#x20;  - Axis

&#x20;  - Radius/diameter

&#x20;  - Center

&#x20;  - Face boundaries

&#x20;  - Feature name where available

5. Deduplicate coaxial faces representing the two sides of one through-hole.

6. Transform local centers through the complete occurrence transform chain into top-level assembly coordinates.



A deduplication key can use:



- Collinear axis within angular tolerance

- Same radius within diameter tolerance

- Projected centers within position tolerance

- Overlapping axial ranges



## 7.4 Expected diameter resolution



Resolve expected diameter in this order:



1. Known corresponding Excel parameter for the referenced channel/feature

2. Matching named Inventor feature or pattern diameter

3. Dominant repeated diameter among holes near expected positions

4. No diameter constraint, with reduced confidence



The result record must identify whether diameter was:



```text

Explicit

Inferred

Unavailable

```



## 7.5 Matching



Candidate filtering:



- Correct active occurrence or referenced part number

- Compatible array direction

- Near expected Z

- Compatible diameter, if known

- Reasonable search radius



Use a one-to-one minimum-cost assignment. Given the small data size, a Hungarian assignment implementation is acceptable, although sorted 1D matching can be used when the candidate set is unambiguous.



The match cost should prioritize:



1. Error on authoritative array axis

2. Error in Z

3. Diameter mismatch

4. Transverse-axis distance

5. Feature confidence



An actual hole may match only one expected hole.



## 7.6 Classification



Default positional thresholds:



```text

Match:   error <= 0.010 in

Warning: error > 0.010 in and <= 0.031 in

Failure: error > 0.031 in

```



Use the distance over authoritative worksheet dimensions for the primary status.



Also record:



- `ΔX`

- `ΔY`

- `ΔZ`

- Authoritative error

- Full 3D distance

- Diameter difference

- Match confidence



Result classes:



```text

Match

Warning

Mislocated Hole

Missing Expected Hole

Extra Actual Hole

Wrong Count

Wrong Spacing

Wrong Start Offset

Wrong Z Location

Referenced Part Suppressed

Referenced Part Missing

Ambiguous Occurrence

Invalid Calculator Row

Unsupported Geometry

```



Extra holes appear in a separate informational section by default.



## 7.7 Pattern-level diagnostics



After point matching, calculate:



- Expected count versus actual count

- Average spacing

- Maximum spacing deviation

- First-hole offset

- Last-hole offset

- Mean signed error

- Standard deviation of signed error



This allows the UI to identify patterns such as:



```text

Systematic +0.105 in Y offset

```



rather than displaying only 22 nearly identical failures.



---



# 8. Inventor visual overlays



Use `ClientGraphics` and `GraphicsDataSets`.



Suggested colors:



- Expected center: cyan

- Matched actual center: green

- Warning: amber

- Failure/mislocated: red

- Missing expected hole: red cross/ring

- Extra actual hole: purple

- Expected-to-actual connector: red or amber line



Organize nodes by:



```text

Floor

Roof

South Wall

North Wall

Selected Result

```



UI toggles control each node’s visibility.



When a result is selected:



1. Clear the previous selection overlay.

2. Highlight the occurrence and actual face if available.

3. Show expected and actual markers.

4. Draw a connecting line.

5. Show an offset label where practical.

6. Move the camera to frame the selected geometry.



All graphics must remain transient and be deleted:



- When the model changes

- When a new session begins

- When the user clicks Clear

- Before closing Inventor



The overlays must not dirty the document.



---



# 9. User interface plan



## Main layout



### Top toolbar



- Select IAM

- Select calculator

- Inventor version

- Validation mode

- Start

- Cancel

- Theme selector



### Left navigation



1. Setup

2. Calculator

3. Parameter Preview

4. Channel Results

5. Diagnostics



### Main results view



Summary cards:



```text

Matched

Warnings

Failures

Missing

Skipped

Extra

```



Filter controls:



- Channel group

- Status

- Part number

- Channel name

- Text search

- Show extra holes

- Overlay category toggles



Results table:



| Status | Group | Channel | Index | Part | Expected | Actual | Δ Axis | ΔZ | Total | Notes |

|---|---|---|---:|---|---|---|---:|---:|---:|---|



Double-click or selection zooms to geometry.



## Progress display



Show concrete phases:



```text

Copying workspace

Recalculating Excel

Reading calculator

Starting Inventor 2020

Opening copied assembly

Inventorying parameters

Running iLogic

Updating model

Extracting hole geometry

Matching Floor Channels

Rendering overlays

```



Do not display the UI as frozen during 30–45 second inventory operations.



## Severity behavior



### Continue with warning



- Missing individual parameter

- Ambiguous parameter

- `#REF!` in an orphaned channel row

- Missing optional occurrence

- Unsupported individual geometry

- Extra holes



### Block Apply mode



- Excel calculation does not complete

- Workbook-wide `Error_Check` indicates invalid calculation

- Top-level IAM cannot open

- Required `Name1` rule fails

- Required representation cannot activate

- Copied references cannot resolve

- Inventor version mismatch

- No safe parameter targets are available



Inspect mode may still continue where useful.



---



# 10. Settings and diagnostics



Store preferences in:



```text

%LocalAppData%CompanyNameInventorValidatorsettings.json

```



Include only:



- Theme

- Last-used folders

- Preferred Inventor version

- Default validation mode

- Tolerance overrides

- Whether to show extra holes



Do not require permanent audit logs.



Keep an in-memory session diagnostic stream and provide:



```text

Copy Diagnostics

Export Results CSV

```



On an unhandled crash, optionally save a small technical error report without model data or workbook contents.



---



# 11. Temporary workspace lifecycle



## Normal close



1. Delete ClientGraphics.

2. Close application-opened Inventor documents without saving further.

3. Quit only the application-owned Inventor process.

4. Release COM references.

5. Close application-owned Excel process if still running.

6. Delete the temporary workspace.

7. If deletion fails, mark it for cleanup on the next application start.



## Export Validated Copy



If selected:



- Prompt for an explicit destination outside the Vault source folder.

- Copy the session workspace there.

- Warn that the exported copy is not checked into Vault.

- Do not overwrite existing files without confirmation.



## Crash recovery



On startup, inspect only the application’s own temp root for stale session folders. Offer:



- Delete stale sessions

- Open folder

- Ignore



Never delete arbitrary folders from `C:Temp`.



---



# 12. COM reliability requirements



- Use dedicated STA automation threads.

- Wrap each COM call in contextual error handling.

- Release child COM objects before parent objects.

- Avoid `foreach` over COM collections where it creates hidden RCWs; use indexed loops.

- Call `Marshal.FinalReleaseComObject` only for objects owned by the session.

- Never kill Excel or Inventor processes by executable name.

- Kill by tracked PID only as a last-resort cleanup action and only after confirming ownership.

- Add cancellation between phases, not during unsafe mid-COM mutations.

- Set operation timeouts for:

&#x20; - Excel calculation

&#x20; - Inventor startup

&#x20; - Document opening

&#x20; - Update/rebuild

&#x20; - iLogic execution



If cancellation occurs during Apply mode, finish the current safe COM call, then close the disposable session.



---



# 13. Testing strategy



## Unit tests



Test without Excel or Inventor:



- Header normalization

- `Sheet1` row classification

- `Channel Loc` parser

- Excel error handling

- Array generation

- Unit parsing

- Parameter-name matching

- Coaxial-hole deduplication

- Coordinate transforms

- One-to-one matching

- Tolerance classification

- Systematic-offset detection

- Session manifest and cleanup rules



## Integration tests



### Excel



- `.xls`

- `.xlsx`

- Automatic calculation

- Full rebuild

- `#REF!`

- Invalid `Error_Check`

- Missing sheet

- Dedicated process isolation

- User Excel already running



### Inventor 2020



- Dedicated process

- Open copied sample

- Inventory

- LOD `iLogic`

- Run `Name1`

- Rebuild

- Extract geometry

- Render/delete ClientGraphics

- Existing user Inventor remains untouched



### Inventor 2024



Repeat the same tests, specifically verifying:



- Correct process/ROT binding

- File migration occurs only on copies

- `iLogic` LOD/Model State compatibility

- `Name1` execution

- Clean shutdown independent of another running session



## Regression fixtures



Use `391-10006-023.iam` and `Calc_10006_023.xls`.



Expected regression results should include:



- `FLOOR_CHAN_4`: match

- `FLOOR_CHAN_5`: match

- South wall: approximately `+0.0087 in`, classified as match

- North wall: approximately `+0.105 in` Y discrepancy, classified as failure

- `FLOOR_CHAN_1`: missing/mismatched 16-hole array

- `ROOF_CHAN_3`: skipped `#REF!`

- `ROOF_CHAN_8`: skipped `#REF!`



Also test the second calculator family to ensure the parser is based on headers, not only the first workbook’s row addresses.



---



# 14. Implementation phases



## Phase 1 — Foundation and session management



Deliver:



- WPF shell

- Theme support

- File selection

- Settings

- Temporary workspace copy

- Read-only attribute clearing

- Session cleanup

- Progress and cancellation framework



Estimated effort: 1 week.



## Phase 2 — Excel automation and parsing



Deliver:



- Dedicated Excel process

- `.xls` and `.xlsx` opening

- Full recalculation

- `Data`, `Sheet1`, and `Channel Loc` parsers

- Error-cell handling

- Calculator preview



### Phase 2 Cleanup & Retrofit Items (from 56-sheet audit):

- *`Sheet1TabParser`:* Upgrade from hardcoded 4-column layout to dynamic archetype detection. Must handle Archetype 2 (multi-table layout on `391-10004` fan skids with hole schedules in Cols A–I and driving parameters in Cols K–M) and headerless sheets (e.g. `391_10006_021.xls`).

- *`ChannelLocTabParser`:* Upgrade to dynamic header column mapping. Must handle blank Column A, group headers placed in Columns F/G, extra description columns, and filter out inactive template rows (`Qty <= 0`, `Z <= 0`).



Estimated effort: 1–1.5 weeks.



## Phase 3 — Inventor startup and inventory



Deliver:



- 2020 process launch

- 2024 version selection and ROT binding

- Dedicated process lifecycle

- IAM opening

- Recursive document/occurrence inventory

- Parameter and feature inventory

- Read-only Inspect mode



### Phase 3 Cleanup & Retrofit Items:

- *`WorkspaceManager`:* Add OLE spreadsheet reference repointing (`FileDescriptor.ReplaceReference`) during workspace creation so copied assemblies point to the local disposable calculator rather than source Vault paths.

- *`InventorModelInventoryService`:* Ensure complete enumeration of `TableParameters` (`kTableParameterObject`, COM type `50349312`), which represent the 618 driving parameters originating from the linked calculator.

- *`ParameterMatchingService`:* Remove obsolete regex-based suppression guessing (`TryMatchSuppression`). Refactor comparison logic to report differences between calculator values and current CAD state without assuming a direct COM parameter write path.



Estimated effort: 1.5–2 weeks.



## Phase 4 — Native OLE repointing, calculator refresh, and Apply mode [COMPLETED]

Status: **Completed** (2026-09-15)

Deliver:



- Disposable calculator update and recalculation

- Assembly and child IPT OLE spreadsheet link repointing (`ReplaceReference`)

- Native Inventor link refresh (`TableParameters`), propagating sketch dimensions, features, and plane offsets

- `Name1` master iLogic rule execution

- LOD/Model State preflight verification

- Assembly update/rebuild (`asmDoc.Update2(true)`)

- Before/after snapshot delta display (parameters, suppressions, and geometry changes)

- Interactive preview and confirmation gate in UI



Estimated effort: 1–1.5 weeks.



## Phase 5 — Geometry comparison [COMPLETED]

Status: **Completed** (2026-09-16)

Deliver:

- Pure C# geometry calculation and matching engine (`InventorValidator.Geometry`) with zero COM dependencies for testability
- Expected-array generation from Channel Loc parameters (`ExpectedHoleGenerator`) with Segment Start Z offset
- Assembly-wide B-Rep cylinder face extraction (`InventorHoleExtractionService`) transformed into top-level coordinates
- Coaxial deduplication (`CoaxialHoleDeduplicator`) merging dual surface faces of through-holes into 1 physical hole count
- Segment Start reference datum detection (work plane `Bhd Loc from Seg Start` / `Segment Start` or Z=0 origin)
- Authoritative 2D grading (Floor/Roof: X & Z; South/North: Y & Z) with diagnostic transverse depth
- Dominant repeated diameter clustering and inference
- Globally optimal 1-to-1 hole pairing using Kuhn-Munkres (Hungarian algorithm)
- Pattern diagnostics (`PatternDiagnosticAnalyzer`) reporting count, spacing consistency, and systematic shifts (e.g. +0.105" Y shift)
- Complete desktop UI Channel Results tab (`ChannelResultsViewModel` + `MainWindow.xaml`) with summary KPI cards, filter toolbar, diagnostic banner, and CSV export
- Unit test suite verifying benchmark matches, tolerances, deduplication, and Hungarian matching

Estimated effort: 2–3 weeks.



## Phase 6 — Visualization and usability [COMPLETED]

Status: **Completed** (2026-09-18)

Deliver:

- Interactive in-app 2D Visual Schematic Map (`ChannelMapControl`) in WPF with smooth pan/zoom, channel track beams, hole markers, and discrepancy vectors
- Unfolded multi-group layout (Roof, North Wall, South Wall, Floor) with quick filter pills
- Dedicated Autodesk Inventor 3D `ClientGraphics` and `GraphicsDataSets` visual overlays with transient lifecycle and document cleanliness preservation
- 3D camera close-up framing (~4–6" field of view) and native `HighlightSet` occurrence highlighting upon result selection
- Two-way synchronized selection between 2D Map, compact DataGrid, and Autodesk Inventor viewport
- Interactive KPI cards functioning as one-click status filter toggles (Matched, Warnings, Discrepancies, Missing, Extra)
- Resizable split-pane layout between 2D visual schematic and compact results table
- Unit test suite verifying overlay service safety, view model filter commands, and selection defaults (108 tests passing)

Estimated effort: 1–1.5 weeks.



## Phase 7 — Hardening and deployment



Deliver:



- 2020/2024 regression testing

- Existing-session isolation tests

- Error recovery

- Stale workspace cleanup

- Single-file publish

- User documentation

- Pilot release for 2–3 designers



Estimated effort: 1–2 weeks.



### Overall estimate



For one developer familiar with C#, COM, and Inventor:



```text

9–12 developer-weeks

```



The geometry and multi-version Inventor integration are the highest-risk portions. A less experienced Inventor API developer should allow additional time.



---



# 15. First-release acceptance criteria



The first release is acceptable when it can:



1. Run from one self-contained executable on Windows 11.

2. Follow Windows light/dark theme and allow an override.

3. Open `.xls` and `.xlsx` through a dedicated Excel process.

4. Recalculate the workbook without affecting the user’s Excel session.

5. Copy the selected model folder to a disposable workspace.

6. Leave source and Vault files unchanged.

7. Start the selected Inventor version in a dedicated process.

8. Leave an existing user Inventor session untouched.

9. Parse `Data`, `Sheet1`, and all four `Channel Loc` groups by headers.

10. Show a safe parameter preview.

11. Never overwrite formula-driven or reference parameters automatically.

12. Run `Name1` successfully in Apply mode.

13. Rebuild the copied assembly.

14. Extract and deduplicate physical holes.

15. Transform hole centers into top-level assembly coordinates.

16. Match expected and actual holes one-to-one.

17. Report axis deviations and authoritative validation error.

18. Detect the known sample defects.

19. Skip orphaned `#REF!` rows without terminating validation.

20. Render and remove temporary overlays without dirtying the IAM.

21. Export results to CSV.

22. Cleanly close only application-owned Excel and Inventor processes.

23. Remove the temporary workspace or clearly report why cleanup failed.



---



# 16. Recommended delivery boundary



The first release should deliberately exclude:



- Vault SDK integration

- Checkout/check-in

- Batch processing

- Command-line mode

- Database or central server

- User accounts

- Drawing updates

- PDF/DXF/STEP export

- Permanent Inventor annotations

- Automatic modification of formula-driven parameters

- Automatic rewriting of iLogic

- Generic validation of arbitrary workbook layouts

- A separate embedded 3D viewer



This keeps the application focused on its primary purpose: **showing designers whether calculator-defined channel locations agree with live Inventor geometry, without risking their production model or current Inventor session.**

