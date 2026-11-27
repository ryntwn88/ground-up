# Ground Up!

**Ground Up!** is a technical rock climbing route development and crag management simulator—combining high-consequence ground-up climbing, rock cleaning, trad/aid protection, and hand-drilling physics with a *RollerCoaster Tycoon*-style crag management, land-access, and community simulation.

## Repository Structure

- `src/GroundUp.Core/` — Engine-agnostic C# simulation core:
  - **Semantic Geology:** Parametric rock types (`Granite`, `Sandstone`, `Limestone`, `Basalt`), cracks/seams, edges/flakes/choss, horns, stances, and surface moss/dirt masks.
  - **Protection Physics:** Cam lobe expansion ranges (`#0.1`–`#4`), flare & friability penalties, skyhook lip-angle stability, piton seam holding power, and horn slings.
  - **Rope & Fall Dynamics:** Automated ground-anchor lead-solo tether, dynamic rope stretch, Fall Factor (`FF`), peak `kN` impact force, and sequential gear zippering cascades.
  - **Forearm Stamina & Pump:** Continuous pump accumulation from wall overhang, hold ergonomics, and one-handed stance locks vs. stance/gear recovery.
- `tests/GroundUp.Core.Tests/` — Deterministic xUnit test suite verifying climbing, gear-weighting, whipper, zippering, and stamina scenarios.
- `godot/` — Godot 4 (.NET / C#) 3D client project referencing `GroundUp.Core`.
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

## Opening the 3D Project

Open `godot/project.godot` in **Godot 4 (.NET Edition)** and press **F5** to build and run the Phase 0/1 sandbox scene.
