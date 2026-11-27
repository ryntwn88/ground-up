# AGENTS.md — Ground Up! Engineering & Architecture Rules

## 1. Strict Simulation Separation (`GroundUp.Core` vs. `godot/`)
- **`src/GroundUp.Core` MUST have zero `Godot` namespace or engine dependencies.** Use `System.Numerics` (`Vector2`, `Vector3`, `MathF`) and plain C# records/classes/structs so all simulation logic compiles and runs fast in standard `.NET` CLI unit tests.
- **`godot/` (`GroundUp.Godot`)** is a thin presentation, input, camera, IK, and procedural mesh/UI shell that reads and mutates `GroundUp.Core` state via clean adapter types.
- Every new gameplay rule (gear placement holding force, rope fall impulse, rock cleaning, drilling progress, route star-rating, NPC climber evaluation, crag access rules) belongs in `GroundUp.Core` first, backed by deterministic unit tests in `tests/GroundUp.Core.Tests`.

## 2. Semantic Geology Is the Single Source of Truth
- Never infer mechanical properties (crack width, flare, rock friability, choss hollowness, hook lip angle) from raw 3D collision triangles.
- Always generate or author `GroundUp.Core.Geology` features first; procedural 3D meshes, graybox debug visuals, physics holding checks, and route star-rating evaluations all read from those exact semantic parameters.

## 3. Coding & Testing Standards
- Target `.NET 8.0` (`net8.0`) with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- Use explicit SI/climbing units in property and parameter names (`Millimeters`, `Meters`, `Degrees`, `Kilonewtons`, `Seconds`) to prevent unit-conversion bugs in physics math.
- Run `dotnet test` after any change to `src/GroundUp.Core` before touching Godot scenes.
