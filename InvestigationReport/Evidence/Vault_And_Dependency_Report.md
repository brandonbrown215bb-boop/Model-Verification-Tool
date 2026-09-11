# Vault Boundary and Dependency Report

## 1. Executive Summary
Inspection of the representative assembly (`391-10006-023.iam`) and associated components demonstrates that all referenced CAD models are completely contained within the local workspace directory. The validation workflow does not require the Autodesk Vault SDK or direct Vault API integration.

## 2. Dependency Audit Results
- **Assembly Document:** `391-10006-023.iam`
- **Total Referenced Documents:** 64 parts and sub-assemblies
- **Internal Directory References:** 64 (100%)
- **External / Network / Vault Server References:** 0 (0%)
- **Unresolved References:** 0
- **Content Center Parts:** None in this assembly family (all parts are custom sheet metal components prefixed with `091-30102-` or coil models).

## 3. Local Vault Workspace Characteristics
1. **File Attributes:** Files downloaded from Autodesk Vault retain `Read-Only` file attributes until checked out.
   - *Impact on Validation:* When copying files to the disposable workspace (`C:\Temp\...`), the validation tool must programmatically clear the `ReadOnly` attribute on copied files so that parameter modification, rebuild, and iLogic execution can proceed without IO exceptions.
2. **Project Files (`.ipj`):**
   - Several subdirectories contain localized `.ipj` files (e.g. `391-10026-002.ipj`, `391-10006-026.ipj`).
   - For `391-10006-023.iam`, reference resolution works directly via relative directory search without requiring an active Vault project file.
3. **Vault SDK Necessity:**
   - **Recommendation:** Do NOT include the Vault SDK in the first release (Scope B).
   - Designers should perform their normal "Get" / download operation via the installed Autodesk Vault Client.
   - The validation tool should operate exclusively on the local working copy.
