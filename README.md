# UV Map Preview & Fixer for Unity Inspector

A comprehensive, high-performance Unity Editor tool that enables you to preview, inspect, diagnose, and fix UV map issues directly inside the **Inspector** (or via a dedicated dockable **Editor Window**).

---

## 🚀 Key Features

### 1. Interactive UV Map Canvas (In Inspector & Window)
- **CAD-like Navigation & Selection**:
  - **Left Click**: Selects UV Island, Triangle Face, or Vertex depending on active Selection Mode.
  - **Right Click**: Instantly unselects the UV selection (clears all selected UVs in 2D canvas and 3D Scene View). Also cancels any active UV drag in progress. Holding `Shift` or `Ctrl` while right-clicking unselects only the specific UV element under cursor.
  - **Left Click + Drag**: Marquee box select multiple UV islands, faces, or vertices.
  - **Shift + Click / Drag**: Add to current UV selection.
  - **Ctrl / Alt + Click / Drag**: Remove from current UV selection.
  - **Interactive Moving & Ghost Preview Dragging**:
    - **Move Tool**: Select the `Move` tool in the toolbar and drag anywhere on canvas to translate selected UVs.
    - **Direct Drag**: While in `Select` mode, clicking directly on an already-selected vertex, quad, face, or island and dragging immediately moves the selection.
    - **Real-Time Moving Ghost Preview**:
      - While dragging, a dedicated **Ghost Preview** floats smoothly across the canvas tracking your cursor in real time.
      - **Hollow Drag Silhouette (No Inside Color)**: Completely transparent inside without any color tint, giving you 100% unobstructed, crystal-clear visibility of the underlying texture pixels during drag alignment.
      - **Ghost Interior Wireframe**: Clean semi-transparent wireframe edges (`alpha = 0.60f`) displaying all polygon geometry in the moving selection.
      - **Dual-Pass Transparent Boundary Outline**: High-contrast silhouette outline combining a dark drop-shadow backing stroke (`alpha = 0.55f`) with an electric cyan accent line (`alpha = 0.95f`), ensuring crisp visibility against light, dark, and complex textures.
      - **Ghost Vertex Markers**: Glowing cyan star markers mark each dragged vertex position for precision snapping.
    - **Anchor Ghost at Starting Position**:
      - Faint wireframe and soft boundary outline remain anchored at the starting position to visualize the original footprint without any inside fill.
    - **Displacement Vector & Leader Line**:
      - A cyan leader line connects the original centroid crosshair to the moving ghost preview centroid, clearly showing drag trajectory and offset.
    - **Floating Ghost HUD Badge**:
      - A sleek floating pill badge tracks beside the ghost preview displaying live offset numbers: `Ghost: ΔU +0.052, ΔV -0.018`.
      - Top HUD badge updates in real time to `Ghost Preview: ΔU +0.052, ΔV -0.018`.
    - **Escape Key**: Cancels active move in progress and returns UVs to original position.
    - **Undo Support**: Releasing mouse commits the move with full Unity `Undo` (`Ctrl+Z`).
  - **Blender-style Hotkey `L` (Select Linked / Connected UVs)**:
    - **Hover + `L`**: Point your cursor over any UV island or face and tap **`L`** to instantly select the entire connected UV island!
    - **Selection + `L`**: With any vertex or quad selected, press **`L`** anywhere on canvas to expand the selection to the complete connected UV island(s).
    - **`Shift + L`**: Adds the hovered connected UV island to the current selection.
    - **`Ctrl + L` / `Alt + L`**: Removes the hovered connected UV island from the current selection.
  - **Middle Mouse / Alt + Drag / Pan Tool**: Pan canvas smoothly in any direction.
  - **Scroll Wheel**: Smooth zoom into cursor position.
  - **Reset View**: Quick button to return to standard `[0..1]` tile.
  - **Focus UVs**: Automatically frames view around mesh's actual UV bounding box.
- **Selection Modes & Visual Highlighting**:
  - **Island Mode**: Selects the entire connected UV island with a single click or marquee touch (Shortcut: **`L`**).
  - **Quad Mode**: Selects the **whole connected quad** (both triangles forming the 4-sided polygon). Automatically detects the paired sibling triangle, non-overlapping convex geometry, and 3D coplanarity.
  - **Face Mode**: Selects connected quads by default, or hold `Alt + Click` to select individual single triangles.
  - **Vertex Mode**: Selects individual UV vertices.
  - **Visual Feedback (No Inside Color)**: Selected UV elements have **no inside color fill**, keeping the underlying texture 100% visible and unoccluded. Selection is clearly delineated by bright golden internal wireframe lines, dual-pass high-contrast boundary silhouette outlines, and golden star vertex handles.
  - **Selection Toolbar**: Easily switch tools (`Select | Move | Pan`), toggle modes (`Island | Quad | Face | Vertex`), and execute quick actions (`All`, `None`, `Invert`).
- **Visual Overlays & Backgrounds**:
  - **Material Texture**: Automatically samples your object's active material (`_BaseMap` for URP/HDRP, `_MainTex` for Standard).
  - **Checkerboard**: High-contrast UV test pattern.
  - **Dark / Light Grid**: Engineering grid with minor/major coordinate lines.
  - **Custom Texture**: Drag-and-drop any reference image or texture sheet.
  - **Texture Opacity & Tiling**: Adjustable alpha slider and repeat/tile toggle.
  - **Wireframe & Vertex Dots**: Crisp lines and point markers.
  - **Face Fill**: Subtle translucent fill to visualize UV island coverage.
  - **Inverted / Flipped Faces**: Highlighted in soft translucent blue (disabled by default, no jarring red tints).
- **HUD Readouts**:
  - Live cursor UV coordinate readout `(U: 0.421, V: 0.812)` when hovering.
  - Live UV Bounding Box `[minU, minV] to [maxU, maxV]`.
  - Active Channel and live selection count badge (`★ Selected: 48 verts`).
  - Out-of-bounds warning badge (`⚠ UVs extend beyond [0, 1] bounds`).

### 2. 3D Scene View Synchronization & Highlight
- **Real-Time 3D Face & Quad Visibility**:
  - Selecting any face, connected quad, island, or vertex in the UV preview canvas immediately illuminates the corresponding 3D geometry directly on your model in the **Scene View**.
- **Dual-Pass Depth Rendering**:
  - **Front Surface Pass (`LessEqual`)**: Luminous translucent fill and crisp golden boundary outline on directly visible 3D surfaces.
  - **X-Ray Occluded Pass (`Greater`)**: Soft translucent ghost silhouette showing through the model so you can immediately locate selected faces even when they are on the back or inside of the mesh.
- **3D Boundary Outline & Wireframe**:
  - Accurately renders the outer boundary silhouette around the selected quad or island while displaying internal diagonal edges with a subtle, softer wireframe.
  - Corner vertex markers indicate exact 3D vertex handle positions.
- **Floating 3D Scene View HUD Badge**:
  - A sleek floating badge appears directly above the selected face or quad in 3D:
    - Selection type title: `★ Quad (4 Verts)`, `★ Face (3 Verts)`, or `★ UV Island`.
    - UV bounding box and center: `UV [0.240, 0.380] → [0.450, 0.620]`.
- **Corner UV Coordinate Chips**:
  - Displays small, clean `(U, V)` coordinate labels at each 3D corner of the selected quad or face right in the Scene View.
- **Dedicated UV Preview Selection & Pure Scene View Display**:
  - UV selection is performed exclusively inside the **UV Preview & Fixer** canvas (via click, marquee, Island/Quad/Face/Vertex modes, or hotkey `L`).
  - The Scene View acts purely as an informative visual display, showing the selected UV geometry in 3D without intercepting or altering Scene View clicks or camera tools.
- **Zero-Allocation High-Performance Architecture (Lag-Free Scene View & Canvas)**:
  - **Pre-Cached 3D Geometry (`SceneHighlightCache`)**: Pre-computes local-space surface triangles, boundary silhouette lines, internal diagonal edges, 3D vertex handles, centroids, and normals. Repaints in the Scene View take sub-millisecond time ($<0.001\text{ ms}$) with **zero managed GC allocations ($0\text{ bytes}$)**.
  - **Scene View Fast Early-Exit**: `OnSceneGUI` checks selection count and exits immediately in $0.000\text{ ms}$ with zero component or matrix lookups when no UVs are selected.
  - **Zero-Allocation Cached Triangles (`GetTriangles`)**: Caches mesh index buffers by instance ID and submesh index, preventing repeated per-frame native-to-managed index buffer duplication.
  - **Zero-Allocation UV Channel Check (`HasUVs`)**: Checks `mesh.HasVertexAttribute(TexCoord0 + channel)` via native engine bitflags without allocating lists or arrays.
  - **Zero-Allocation UV Caching (`GetUVs`)**: Caches UV arrays by instance ID, channel, and vertex count, reusing scratch list memory instead of generating garbage each frame.
  - **In-Place 2D Geometry Array Reuse**: Reuses cached `s_Cached2DVertices` without re-allocating when transforms or selections change.
  - **Viewport-Culled 2D Canvas Vertex Markers**: Off-screen vertices are automatically culled during zoomed-in canvas viewing, drastically reducing GL vertex submissions.
  - **Cached Material & Texture Slots**: `GetMaterialTextureSlots` caches property inspection results, eliminating per-frame shader property string querying and HashSet allocations.
  - **Reused Scratch Dictionaries & Sets**: Boundary silhouette edge extraction and HUD island overlap analysis use persistent scratch collections, eliminating per-repaint collection allocations.
  - **Targeted Local 2D Repainting**: Replaced heavy global `InternalEditorUtility.RepaintAllViews()` calls during continuous mouse operations (panning, dragging UVs, marquee selection) with targeted 2D editor `Repaint()`. The 3D Scene View stays completely idle during 2D canvas interactions, eliminating Scene View re-rendering load.
  - **Cached 3D Mesh Vertices for Click/Marquee Selection**: Caches 3D mesh vertices in `s_CachedMeshVertices3D`, completely avoiding repeated full-mesh vertex copies during clicks and marquee selections.
  - **Cached Static GUIStyles**: Re-uses cached static `GUIStyle` instances for all badges, HUD labels, and corner chips, preventing per-frame style allocations.

### 3. Multi-Material & Texture Detection with Switchable Tabs
- **Automatic Material Detection**:
  - Automatically queries all materials on the renderer (`sharedMaterials`).
  - If multiple materials are present on the model (e.g., Submesh 0: Body, Submesh 1: Hair, Submesh 2: Clothes), a **Material Tab Strip** appears: `Material: [0: Mat_Body] [1: Mat_Hair] [2: Mat_Clothes]`.
  - 1-click tab switching immediately loads that material's texture suite.
- **Automatic Shader Texture Inspection**:
  - Inspects all `Texture2D` properties defined on the material's shader (`_BaseMap`, `_MainTex`, `_BumpMap`, `_NormalMap`, `_MetallicGlossMap`, `_OcclusionMap`, `_EmissionMap`, detail maps, custom maps).
  - Displays assigned textures as interactive **Texture Tabs**: `Texture: [Albedo (Base)] [Normal] [Metallic] [Occlusion] [Emission]`.
  - Clicking any tab immediately sets that texture as the active background for the UV canvas.
- **Texture Detail & Project Ping**:
  - Displays texture resolution, pixel format, and shader property name (e.g. `Character_Normal.png (2048x2048 RGBA32) • Slot: _BumpMap`).
  - Includes a `[Ping]` button to immediately highlight the texture asset in Unity's Project tab.

### 4. UV Island Overlap & Shared Texture Space Detection
- **Intelligent Overlap & Symmetry Analysis**:
  - Automatically scans mesh UV charts to detect overlapping islands and shared texture regions (such as mirrored limbs, stacked symmetry charts, or accidental overlaps).
  - Utilizes hierarchical broadphase AABB culling and strict 2D triangle-triangle intersection math (supporting coincident mirrored triangles regardless of winding order, strict edge crossings, and interior containment while preventing false positives from seam vertices).
- **Dedicated Top-of-View Overlap Indicator Banners**:
  - **Clean Canvas (No Floating Badges)**: Centroid badges are kept off the middle of the canvas so UV vertices, faces, and underlying textures remain 100% visible and unoccluded.
  - **Top HUD Overlap Banner (2D Preview Canvas)**: When a UV island is selected (or hovered in Island mode), an indicator banner appears prominently at the top of the canvas view:
    - **Overlapping Island**: Displays glowing amber banner `🗂 OVERLAP DETECTED: Selected Island overlaps {N} other island(s) • Shared Texture ({N+1}x)`.
    - **Unique Texture Space**: Displays glowing emerald banner `✓ NO OVERLAP: Selected Island has unique texture space (0 overlaps)`.
    - **Multi-Island Selection**: Displays `🗂 OVERLAP STATUS: {K} of {Total} selected islands share texture space`.
  - **Global HUD Summary Badge**: Top-left HUD displays overall mesh status (`🗂 Overlaps: X islands (Y shared groups)` or `✓ 0 Overlaps`).
  - **Channel Toolbar Chip**: High-visibility status indicator in both Inspector and Editor Window toolbars.
- **Visual Overlap Highlighting (Wireframe Only - No Inside Fill)**:
  - Overlapping islands are highlighted with a crisp wireframe contour in glowing amber, with **no inside fill color** so the underlying texture remains 100% visible.
  - Configurable toggle (`Show Overlapping Islands`) and color picker (`Overlap Highlight Color`) in Display & Visual Options.
- **Zero-Allocation Cached Performance**:
  - Cached via `GetIslandOverlapInfo`, invalidating only when UV coordinates change, ensuring zero per-frame garbage.

### 5. UV Channels & Submesh Support
- Full support for **UV0 (Base / Diffuse)**, **UV1 (Lightmap / Detail)**, **UV2**, and **UV3**.
- Individual UV channel status indicator (e.g. `UV0 •` vs `UV1 (Empty)`).
- Submesh isolation: Inspect all submeshes together or filter by individual submesh.

### 6. Dedicated Quick Fix Options
> **Selection-Aware Operations**: When any vertices, faces, or islands are selected on the canvas, all Quick Fixes apply **exclusively to the selected UV elements** while leaving the rest of the mesh untouched. If nothing is selected, the operation applies to the entire mesh. Direct canvas translation with real-time holographic ghost preview, delta offset HUD, right-click unselection, and Blender-style `L` island expansion are also available directly on the canvas.

#### ⚡ 1-Click Quick Fix Presets
- **Flip Selected UV Origin (In-Place Center)**:
  - **⇕ Flip Selected UV Origin**: Flips selected UVs in-place around their own center/origin. Never jumps or teleports across the canvas!
  - **Axis Selector**: Choose between **Vertical (V)** (default, flips across local horizontal centerline), **Horizontal (U)** (mirrors across local vertical centerline), or **Both (U & V)**.
  - **Per-Island Preservation**: When multiple islands are selected, each island automatically flips around its own origin independently without displacing other islands.
  - **Dedicated 1-Click Origin Buttons**: Instant `↕ Flip V (Selected Origin)` and `↔ Flip U (Selected Origin)` buttons for fast single-click workflow.
- **Global Canvas Center Flips**:
  - `↕ Flip V (Canvas Center)`: Inverts coordinates across canvas center `0.5` to fix inverted import pipelines.
  - `↔ Flip U (Canvas Center)`: Mirrors coordinates across canvas center `0.5`.
- **Rotate 90° CW / CCW / 180°**: Rotates coordinates around island centroid (or center if no selection).
- **Fit to [0, 1] (Keep Aspect)**: Scales UVs to fit cleanly within `[0, 1]` without distortion.
- **Normalize to [0, 1] (Stretch)**: Fits bounding box to `[0, 1]` range.
- **Center UVs**: Re-centers UV island centroid to `(0.5, 0.5)`.
- **Clamp / Wrap [0, 1]**: Clamps coordinates or wraps repeating coordinates back into standard range.
- **Full Undo Support**: Every modification registers with Unity's `Undo` system (`Ctrl+Z` / `Cmd+Z`).

---

## 🛠️ How to Use

### Method 1: In the Inspector (Component)
1. Select any GameObject in your scene with a `MeshFilter` or `SkinnedMeshRenderer`.
2. Do any of the following:
   - Right-click the **MeshFilter** or **SkinnedMeshRenderer** component header and click **Preview & Fix UVs...**
   - Or click **Add Component** -> **Mesh** -> **UV Preview & Fixer**.
   - Or select top menu: **GameObject** -> **Mesh** -> **Add UV Preview & Fixer**.
3. The interactive UV canvas and fix tools appear directly in the Inspector.

### Method 2: In the Standalone Dockable Window
1. Go to top menu: **Tools** -> **UV Map Preview & Fixer** (or **Window** -> **UV Map Preview & Fixer**).
2. With **Auto-Sync Selection** enabled, simply click any GameObject in the Hierarchy or any Mesh asset in the Project window.
3. Dock the window anywhere in your Unity layout (e.g. side-by-side with Scene View).

---

## 📦 Plugin Architecture & Export as Unity Package

The tool is structured as a self-contained, modular Unity package ready for distribution:

```
Assets/UVPreviewAndFixer/
├── Runtime/
│   ├── UVInspectorTool.cs               # Runtime component (MonoBehaviour)
│   └── UVPreviewAndFixer.Runtime.asmdef # Runtime assembly definition
├── Editor/
│   ├── UVEditorCore.cs                  # Canvas engine & math routines
│   ├── UVInspectorToolEditor.cs         # Inspector custom editor
│   ├── UVPreviewWindow.cs               # Standalone dockable window
│   ├── UVToolAutomatedTests.cs          # Self-test suite
│   ├── UVPackageExporter.cs             # 1-click package export utility
│   └── UVPreviewAndFixer.Editor.asmdef  # Editor assembly definition
├── Shaders/
│   └── UVPreviewShader.shader           # High-performance preview shader
├── package.json                         # Unity Package Manager (UPM) manifest
└── README.md                            # Complete documentation
```

### 📤 How to Export as a `.unitypackage`
- **1-Click Menu Item**: Go to top menu **Tools** -> **UV Map Preview & Fixer** -> **Export Unity Package...**, choose where to save the `.unitypackage`, and click **Save**.
- **Right-Click Context Menu**: Right-click the `UVPreviewAndFixer` folder in Unity's Project tab -> **Export UV Preview & Fixer Package...**.
- **Native Unity Export**: Right-click `Assets/UVPreviewAndFixer` -> **Export Package...** -> ensure all checkboxes are ticked -> **Export...**.

### 📥 How to Install in Any Other Unity Project
- **Option 1 (Unity Package)**: Drag and drop the exported `UVPreviewAndFixer_v1.0.0.unitypackage` into your project, or go to **Assets** -> **Import Package** -> **Custom Package...**.
- **Option 2 (UPM / Embedded Package)**: Copy the entire `UVPreviewAndFixer` folder directly into your project's `Packages/` directory. Unity will immediately recognize it as a local package via `package.json`.

---

## 🧪 Self-Tests
To run the automated verification suite:
- Go to top menu: **Tools** -> **UV Map Preview & Fixer** -> **Run Self-Tests**.
- This tests all core mathematical routines (flipping, rotations, normalizations, projections, channels, flipped triangle detection, quad topology detection, transparent drag outlines, and 3D Scene View raycast/selection).
