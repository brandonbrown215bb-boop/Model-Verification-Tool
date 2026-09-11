# Process Isolation Spike Notes

## Objective
Determine whether the validation application can launch and control dedicated instances of Microsoft Excel and Autodesk Inventor without interfering with active user sessions (e.g. running Excel PID 25736 or Inventor PID 5500).

## 1. Microsoft Excel COM Automation
- **Baseline:** Active user process `EXCEL.EXE` (PID 25736) with workbook `Calc_10006_023.xls` open.
- **Mechanism Tested:** `Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"))` / `win32com.client.DispatchEx("Excel.Application")`.
- **Result:**
  - Windows COM spawned a completely dedicated, isolated process `EXCEL.EXE` (PID 17660).
  - Headless execution (`Visible = False`, `DisplayAlerts = False`) was completely silent.
  - Active session (PID 25736) remained untouched, responsive, and did not receive any document loads or commands.
  - Calling `wb.Close(False)` followed by `app.Quit()` and releasing COM references cleanly terminated the dedicated process.
- **Verdict:** **Confirmed Isolated.**

## 2. Autodesk Inventor COM Automation
- **Baseline:** Active user process `Inventor.exe` (PID 5500) started at 6:06:08 AM.
- **Mechanism Tested:**
  1. `Marshal.GetActiveObject("Inventor.Application")`:
     - Returned `MK_E_UNAVAILABLE` (`0x800401E3`), confirming the running interactive user session did not expose an active automation lock or force interception.
  2. `Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application"))`:
     - Successfully launched a new separate `Inventor.exe` process (PID 11000 and PID 31468).
     - Connected directly to the new instance.
     - Did NOT redirect automation calls to PID 5500.
     - Cleanly terminated upon `app.Quit()` and `Marshal.ReleaseComObject`.
     - PID 5500 remained active and responsive throughout all tests.
- **Headless vs Visible Mode:**
  - Invisible mode (`app.Visible = false`): Certain dynamic geometric queries (such as work plane evaluation or graphics-dependent features) can experience blocking stalls or require a full update cycle.
  - Visible mode (`app.Visible = true`): Fast, fully functional graphics pipeline, supports `ClientGraphics` rendering, camera manipulation, and occurrence selection.
- **Multi-Version Coexistence (2020 vs 2024):**
  - Registry inspection shows `Inventor.Application` ProgID points to Inventor 2020 (`24.0`).
  - Inventor 2024 is installed at `C:\Program Files\Autodesk\Inventor 2024\Bin\Inventor.exe`.
  - For targeting Inventor 2024 without re-registering system ProgIDs, the application can launch `Inventor.exe /Automation` directly via `Process.Start` and bind to its ROT entry.
- **Verdict:** **Confirmed Isolated with dedicated instance lifecycle tracking.**
