# Ground Up! — Master Project Plan

**Vision Source:** `Ground Up! Brainstorm.docx`  
**Repository:** https://github.com/ryntwn88/ground-up  
**Target Platform:** PC (Steam Early Access trajectory)  
**Tech Stack:** Godot 4 (.NET / C#) + decoupled `GroundUp.Core` pure-C# simulation library + xUnit test suite

---

## Locked-In Architectural Decisions

1. **Engine & Code Architecture:** **Godot 4 (.NET / C#)** paired with an engine-agnostic **`GroundUp.Core`** C# class library. All geology parameters, gear holding physics, rope/fall-factor impulses, forearm pump equations, route star-rating rules, and crag tycoon simulation live in pure, unit-tested C# decoupled from 3D scene nodes.
2. **Belay & Anchor Abstraction:** Automated ground-anchor lead-solo tether that tracks active rope out, slack, dynamic rope elongation, **Fall Factor** (`FF = FallDistance / ActiveRopeLength`), and sequential gear "zippering" without bogging gameplay down in manual knot or anchor-rigging minigames.
3. **Flexible Mid-Stage Design Hooks:** Detailed design of the **power-drill unlock challenge/puzzle** and granular bolting-ethics modifiers remain modular until **Phase 2 / Phase 4** once the core climbing and hand-drilling loop is playable.
4. **Project Organization:** Version control, milestone tracking, and phase issues are organized in https://github.com/ryntwn88/ground-up.

---

## 1. Strategic Philosophy: "Concentric Rings of Fidelity"

*Ground Up!* combines a high-consequence physical simulation (free climbing, rock cleaning, trad/aid protection, hand-drilling) with a macro management game (*RollerCoaster Tycoon*-style crag operations, land access politics, marketing, and custom crag creation).

Rather than building all five brainstormed modules horizontally at once, we build **inside-out** in concentric rings:

`Phase 0 (Pure C# Physics Sandbox)` -> `Phase 1 (10m Graybox Wall)` -> `Phase 2 (Bolting, FFA & Star Rating)` -> `Phase 3 (Procedural Crags & 3D Scouting)` -> `Phase 4 (Crag Tycoon, Access & MP Parody)` -> `Phase 5 (Custom Crag Creator & Polish)` -> `Phase 6 (Steam Demo & Early Access)`

**Golden Rule of Scope Control:** Every phase ends with a playable or deterministically verifiable game loop. We never build photorealistic art or macro management UI around a core mechanic until that mechanic is proven fun in graybox form.

---

## 2. Solving the "Lack of Data" Problem: Semantic Procedural Geology

Real-world cliff photogrammetry lacks mechanical meaning: a raw triangle mesh cannot tell code whether a fissure is a parallel `#0.75` cam placement, a flaring piton seam, or a hollow death flake.

Instead of relying on external scan datasets, *Ground Up!* generates its own ground truth using a **3-Layer Semantic Rock Model**:

1. **Macro Cliff Surface:** Parametric wall surface defined by height, aspect (sun/shade), overhang profile, and **Geology Type** (`Granite`, `Sandstone`, `Limestone`, `Basalt`), which sets baseline friction, drill hardness, and friability.
2. **Semantic Feature Graph (The Physics Truth):** Procedural or player-authored features embedded onto the wall with exact mathematical properties:
   - **Cracks & Seams:** 3D spline with continuous width (mm), flare angle, depth, and roughness -> drives cam lobe expansion validity, cam walk, and piton driving resistance.
   - **Edges, Pockets & Flakes:** Lip incut angle, depth, width, and **structural integrity** (`0.0` hollow choss to `1.0` solid stone) -> drives free-climbing grip pump, skyhook/talon stability, and whether tapping/sounding reveals loose rock that must be trundled.
   - **Horns & Knobs:** Neck radius and collar angle -> drives runner/sling security under directional pull.
   - **Stances & Ledges:** Foot surface area and slope -> governs hands-free rests vs. strenuous calf/core pump while hand-drilling.
   - **Surface Mask:** Lichen, moss, and dirt layers -> penalizes friction and hook security until wire-brushed.
3. **Visual Synthesis:** In Phases 1-2, semantic features render as clean, color-coded 3D graybox shapes. In Phases 3-5, the *exact same feature graph* carves high-detail 3D meshes and drives PBR triplanar rock shaders—guaranteeing visuals and physics never desynchronize.

---

## 3. Core UX State Machine (Module 2: Ground-Up Development)

- `FreeClimbing` -> Lock 3-Point Stance -> `StanceInspect` (1 hand free, steady pump)
- `StanceInspect` -> Select Brush / Prybar / Cam / Hook / Piton / Sling -> `CleaningAndGear`
- `StanceInspect` -> Weight Placed Gear / Skyhook -> `HangingOnGear` (tests holding force; blows into `WhipperFall` if insufficient)
- `StanceInspect` or `HangingOnGear` -> Initiate Drill -> `DrillingBolt` (stance drains pump fast; gear hangs risk marginal hook/cam failure)
- `DrillingBolt` -> Set Bolt & Hanger -> `BoltClipped` -> Resume `FreeClimbing`
- `WhipperFall` -> Rope stretch + Fall Factor impulse -> Sequential zippering check -> Caught by highest holding piece (or ground fall!)

---

## 4. Phased Roadmap

### Phase 0: Foundation & Pure-C# Physics Sandbox (Weeks 1-2 — Completed)
- [x] Repository scaffold (`src/GroundUp.Core`, `tests/GroundUp.Core.Tests`, `godot/`) configured for `ryntwn88/ground-up`.
- [x] **Protection Physics v1:** Cam lobe expansion range curves (`#0.1` to `#4`), flare & rock-friability multipliers, skyhook lip-angle slip thresholds, piton seam deformation, and horn sling pull-out security.
- [x] **Fall & Rope Impulse Calculator:** Dynamic rope elongation, Fall Factor, peak kN impact on top piece, and sequential gear zippering cascades.
- [x] **Stamina / Pump Model:** Continuous forearm pump accumulation from wall overhang + grip difficulty + one-handed stance locks vs. recovery on ledges or while hanging on gear.
- [x] **Exit Gate:** Automated CLI xUnit suite simulating 33 climbing, aid-weighting, whipper, and stamina scenarios.

### Phase 1: "The 10-Meter Graybox Wall" — First Playable (Weeks 3-8 — Completed)
- [x] Static 3D graybox cliff in Godot 4 with semantic test features (splitter crack, flaring seam, hookable edges, horn, stances, moss, hollow choss flake).
- [x] Hybrid third-person climber controller (4-limb IK snapping + center-of-mass balance + pump HUD).
- [x] Tactile close-up camera for sounding/trundling choss, brushing moss, and slotting/weighting cams, hooks, pitons, and slings, plus dynamic whipper arrest.
- [x] **Exit Gate:** 40 deterministic CLI xUnit scenarios verifying full ground-to-summit ascent, choss snap whippers, mossy skyhook blowouts, CoM balance pump, and gear weighting.

### Phase 2: Ground-Up Bolting, FFA Mode & Route Star-Rating (Weeks 9-15)
- Hand-drilling mechanic scaled by rock hardness, dust blow-out, stance vs. gear hang trade-offs, plus modular power-drill interface.
- **Module 3 (Pure Free Climbing / FFA):** Climb equipped lines cleanly clipping quickdraws and placing supplemental trad gear to claim the FFA, grade, and name the route.
- **Algorithmic Route Star-Rating Engine (1.0-4.0 Stars):** Scores safety (ground/ledge fall danger, choss bolts), clipping ergonomics, natural-pro ethics, and rock quality/flow.

### Phase 3: Procedural Cliffs & Crag Exploration UX (Weeks 16-22)
- Multi-sector procedural cliff generator (`Granite`, `Limestone`, `Sandstone`, `Basalt`).
- **Module 1 ("Google Earth" 3D Scouting Camera):** Orbital crag overview to close-up binocular zoom, sun/shade preview, and 3D Topo Chalk line sketching.
- Displaced procedural rock meshes + triplanar PBR rock/moss/chalk shaders.

### Phase 4: Crag Tycoon, Access Politics & Marketing Loop (Weeks 23-32)
- **Module 4 (Bird's-Eye Crag Management):** Autonomous NPC climbers traverse your routes' semantic hold graphs, take whippers, and leave reviews + star ratings aggregating into Crag Star Rating.
- Marketing channels (*Mountain Project* parody, Word of Mouth, Guidebook publishing).
- Land designations (Private, BLM/USFS, State/Federal Wilderness power-drill bans & raptor closures), route open/close toggles with occasional NPC poachers, and the Power-Drill Unlock Challenge.

### Phase 5: Custom Crag Creator & High-Fidelity Polish (Weeks 33-42)
- **Module 5 (Custom Crag Creation):** Sculpt size, weather (wet sandstone breakage!), geology, cliff dimensions/aspect, and rock features with Workshop-ready sharing.
- Micro-normal PBR polish, acoustic hammer/hollow-rock audio feedback, and full UX polish.

### Phase 6: Steam Demo, Balancing & Early Access Launch (Weeks 43-52+)
- Curated + procedural Steam Next Fest Demo, climber playtest balancing, Steamworks integration, and Early Access release.
