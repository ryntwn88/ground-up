# Ground Up!

**Ground Up!** is a technical rock climbing route development and crag management simulator—combining high-consequence ground-up climbing, rock cleaning, trad/aid protection, and hand-drilling physics with a *RollerCoaster Tycoon*-style crag management, land-access, and community simulation.

## Repository Structure

- `src/GroundUp.Core/` — Engine-agnostic C# simulation core:
  - **Semantic Geology:** Parametric rock types (`Granite`, `Sandstone`, `Limestone`, `Basalt`), cracks/seams, edges/flakes/choss, horns, stances, and surface moss/dirt masks.
  - **Protection Physics:** Cam lobe expansion ranges (`#0.1`–`#4`), flare & friability penalties, skyhook lip-angle stability, piton seam holding power, and horn slings.
  - **Rope & Fall Dynamics:** Automated ground-anchor lead-solo tether, dynamic rope stretch, Fall Factor (`FF`), peak `kN` impact force, and sequential gear zippering cascades.
  - **Forearm Stamina & Pump:** Continuous pump accumulation from wall overhang, hold ergonomics, CoM balance, and one-handed stance locks vs. stance/gear recovery.
  - **Climber & Session Controller (`GroundUp.Core.Climbing`):** 4-limb reach kinematics, smart cursor limb selection + manual overrides, rack inventory, hollow-choss breakoffs, static gear weighting, and full Module 2 (Development) + Module 3 (First Free Ascent) state machine.
  - **Ground-Up Bolting & Star-Rating (`GroundUp.Core.Bolting`, `GroundUp.Core.Scoring`):** Progressive hand/power drilling, dust choke & blow-tube physics, hex-nut wrench torquing, permanent fixed bolts, and algorithmic `1.0–4.0` Star Rating + YDS Grade evaluation.
- `tests/GroundUp.Core.Tests/` — Deterministic 51-scenario xUnit test suite verifying climbing, bolting, FFA redpointing, star-rating ethics, choss trundling, whipper zippering, and stamina scenarios.
- `godot/` — Godot 4 (.NET / C#) 3D client project referencing `GroundUp.Core` (graybox 10m cliff, 4-limb analytic IK climber, 3D bolting workbench, quickdraw clipping, FFA completion & star-rating modal, dynamic rope renderer, hybrid wall/close-up camera, and procedural acoustics).
- [`PROJECT_PLAN.md`](./PROJECT_PLAN.md) — Full 7-phase development roadmap from Phase 0 sandbox to Steam Early Access.
- [`AGENTS.md`](./AGENTS.md) — Architectural boundaries and engineering conventions.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Godot Engine 4.x (.NET / C# Edition)](https://godotengine.org/)

## Quick Start (CLI Build & Physics Tests)

```powershell
dotnet build GroundUp.sln
dotnet test GroundUp.sln
```

## Opening & Playing the Phase 1 Graybox Wall

Open `godot/project.godot` in **Godot 4.7 (.NET / Mono Edition)** and press **F5**:

- **Left Click:** Step onto starting holds / move limb (or apply active tool in stance mode)
- **Right Mouse Drag / `W` `S` / Scroll:** Orbit camera, scout up/down the cliff, or zoom
- **`Q` / `E` / `Z` / `C`:** Optional manual override for Left Hand / Right Hand / Left Foot / Right Foot (`Esc` returns to Smart Auto)
- **`F`:** Lock 3-point stance (dollies into close-up inspection camera) / resume Free Climbing
- **`1` / `2` / `3`:** Hammer (sound rock solidity), Wire Brush (clean moss), Prybar (trundle hollow choss)
- **`4`–`7` + Mouse Wheel or `[` `]`:** Select Cams (`#0.1`–`#4`), Pitons, Skyhooks, or Slings and cycle sizes
- **`8` + Left Click:** Ready Hand Drill and position drill bit on any reachable spot on the rock face
- **Hold `Space`:** Operate hammer on positioned drill (drill bit rotates automatically between strikes)
- **`B` / `T` / `V`:** Blow out rock dust (`B`), insert bolt & wrench hex nut to 100% torque (`T`), hang & clip quickdraw (`V`)
- **`M`:** Switch between **Route Development Mode** and **First Free Ascent (FFA) Redpoint Mode**
- **`G`:** Weight placed gear/skyhook/bolt to hang and recover forearm pump
- **`P`:** Cycle forearm pump intensity (`1.0x Standard` -> `0.5x Relaxed` -> `0.25x Low` -> `OFF` -> `1.5x Hardcore`)
- **`R`:** Lower to ground (cleans removable gear while keeping installed permanent bolts on the cliff)
- **`F3`:** Toggle developer physics telemetry (`mm`, `deg`, `kN`, CoM balance) + **Dev-Only 24V Power-Drill Toggle**
