# Channel Loc Precedent & Formula Tracing

## 1. Overview
The `Channel Loc` worksheet defines expected channel positions for modular structural bulkheads. Each table group (Floor Channels, Roof Channels, South Wall Channels, North Wall Channels) specifies:
- `Z_LOCATION`: Longitudinal location along the unit depth.
- `ARRAY_OFFSET`: Initial array starting coordinate.
- `ARRAY_QTY`: Total hole count along the array.
- `ARRAY_SPACING`: Spacing between consecutive hole centers.
- `Referenced Part`: Detail drawing part number (e.g. `091-30102-458`).

## 2. Coordinate System Definition & Reference Origins
From Row 1-2 annotations and formula tracing:
1. **Primary Z Plane (`SEGMENT START`):**
   - Formula: `'Excel Calcs'!B9 - ('Excel Calcs'!H6 / 2)`
   - Where `'Excel Calcs'!B9 = SegStart = 1.5 in` (or `3.625 in` if `UpstreamSplit = YES`).
   - Where `'Excel Calcs'!H6 = 1.5 in` (channel flange depth).
   - Result: `Z_LOCATION = 1.5 - (1.5 / 2) = 0.750 in`.
   - In Inventor, this aligns with the assembly origin plane `Bhd Loc from Seg Start`.
2. **IW Zero Location (X-Axis):**
   - Corresponds to assembly `YZ Plane` (X = 0).
   - South/Left Wall sits at `X = IW = 179.0 in`.
   - North/Right Wall sits at `X = 0.0 in`.
   - `Side BH to Shell Clearance` (`'Excel Calcs'!B4 = 0.310 in`) offsets the structural components inward from the external shell.
3. **Floor and Roof Reference (Y-Axis):**
   - Floor Channels sit on the base floor datum (`Y ~ 0`).
   - Roof Channels sit at unit inside height (`Y = IH = 118.0 in`).

## 3. Detailed Precedent Chains by Channel Group

### A. Floor Channels

#### Row 6: `FLOOR_CHAN_1` (Part: `091-30102-458`)
- **Z_LOCATION:** `='Excel Calcs'!B9-('Excel Calcs'!H6/2) = 0.750 in`
- **X_ARRAY_OFFSET:** `='Excel Calcs'!B4+'Excel Calcs'!E45+'Excel Calcs'!H51 = 0.31 + 6.69 + 2.0 = 9.000 in`
  - `B4 = Side_BH_to_Shell_Clearance = 0.310 in`
  - `E45 = 6.690 in` (calculated end-clearance from coil width and hand)
  - `H51 = B8 = 2.000 in` (edge distance margin)
- **X_ARRAY_QTY:** `='Excel Calcs'!H52 = ROUNDUP((H42 - H51*2) / MaxBHHoleSpc_X, 0) + 1 = 16.0`
- **X_ARRAY_SPACING:** `='Excel Calcs'!H53 = (H42 - H51*2) / (H52 - 1) = 5.267 in`
- **Linear Array Equation:** `X[i] = 9.000 + i * 5.267` for `i = 0..15`.
- **Model Geometry Comparison:** The live model part `091-30102-458` does NOT contain this 16-hole uniform array, demonstrating a known legacy discrepancy between the Excel calculation sheet and the actual manufactured/modeled part.

#### Row 9: `FLOOR_CHAN_4` (Part: `091-30102-461`)
- **Z_LOCATION:** `0.750 in`
- **X_ARRAY_OFFSET:** `='Excel Calcs'!B4+'Excel Calcs'!E53 = 0.31 + 2.0 = 2.310 in`
- **X_ARRAY_QTY:** `2.0`
- **X_ARRAY_SPACING:** `2.690 in`
- **Points:** Hole 0 = `2.310 in`, Hole 1 = `5.000 in`.
- **Model Match:** Confirmed in model at `X = 2.310 in` and `X = 5.000 in` (`DeltaX = 0.0000 in`).

#### Row 10: `FLOOR_CHAN_5` (Part: `091-30102-462`)
- **Z_LOCATION:** `0.750 in`
- **X_ARRAY_OFFSET:** `175.000 in`
- **X_ARRAY_QTY:** `2.0`
- **X_ARRAY_SPACING:** `1.690 in`
- **Points:** Hole 0 = `175.000 in`, Hole 1 = `176.690 in`.
- **Model Match:** Confirmed in model at `X = 175.000 in` and `X = 176.690 in` (`DeltaX = 0.0000 in`).

### B. Wall Channels

#### Row 45: South Wall (`LEFT_HAND_CHAN_1`, Part: `091-30102-462`)
- **Z_LOCATION:** `=FLOOR_CHAN_5_Z_LOC = 0.750 in`
- **Y_ARRAY_OFFSET:** `='Excel Calcs'!E16 = B8 - B54 = 2.000 - 0.10501 = 1.895 in`
- **Y_ARRAY_QTY:** `='Excel Calcs'!E17 = 22.0`
- **Y_ARRAY_SPACING:** `='Excel Calcs'!E18 = 5.429 in`
- **Model Match:** Holes 0 through 21 match along Y with delta `+0.0087 in` (within 0.010 in tolerance).

#### Row 57: North Wall (`RIGHT_HAND_CHAN_1`, Part: `091-30102-461`)
- **Z_LOCATION:** `=FLOOR_CHAN_4_Z_LOC = 0.750 in`
- **Y_ARRAY_OFFSET:** `='Excel Calcs'!E16 = 1.895 in`
- **Y_ARRAY_QTY:** `22.0`
- **Y_ARRAY_SPACING:** `5.429 in`
- **Model Match:** In the model, the hole array begins at `Y = 2.000 in`, introducing a systematic `+0.105 in` offset equal to the bottom bulkhead thickness `BtmBhdThk`.

### C. Legacy `#REF!` Rows
- **Row 30 (`ROOF_CHAN_3`):** `=IF('Excel Calcs'!#REF!=0,0,'Excel Calcs'!B9-('Excel Calcs'!H6/2))`
- **Row 35 (`ROOF_CHAN_8`):** `=IF('Excel Calcs'!#REF!=0,0,'Excel Calcs'!B9-('Excel Calcs'!H6/2))`
- **Analysis:** Precedent cells in `Excel Calcs` were removed during past template revisions, leaving dangling `#REF!` formulas. These rows should be treated as orphaned template noise and flagged as skipped in the validation engine.
