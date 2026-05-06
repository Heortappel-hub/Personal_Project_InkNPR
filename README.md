# Chinese Ink-Wash Rendering — Unity Scene

A stylized Chinese ink-wash rendering setup for Unity's Built-in Render Pipeline. It contains:

- **Object shader**: `NPR/IW_mat` (wet outline + dry "flying-white" stroke + half-Lambert ink ramp + brush detail + optional rim).
- **Camera post-process**: `NPR/IW_PP` (luminance → edge detection → stipple → ink bleed → composite with paper / ink textures).
- **Runtime UI**: `InkRuntimeUI`, an IMGUI panel that lets you tweak parameters in a built player.

---

## 1. Requirements

- Unity **Built-in Render Pipeline**.
- Tested on Unity 6000.2.14f; both legacy and new Input System are supported.
- The camera's `DepthTextureMode.Depth` is enabled automatically — no manual setup needed.

---

## 2. Importing into an Existing Project

1. Close the target Unity project.
2. Copy the following folders from this repo's `Assets/` into the target project's `Assets/`:
   - `Assets/Shader/` (`Ink-wash_PP.shader`, `InkWash_Mat.shader`, …)
   - `Assets/Script/` (`firs.cs`, `InkRuntimeUI.cs`, `Camera.cs`)
3. Reopen Unity and wait for asset reimport and shader compilation.
4. Make sure the project is on the Built-in pipeline.


---

## 3. Scene Setup

### 3.1 Objects (using the `NPR/IW_mat` material)

1. Select the meshes you want rendered as ink-wash.
2. Assign them a material that uses `NPR/IW_mat`.
3. Hook up the textures on the material:
   - `Brush Noise`: greyscale noise (drives stroke jitter and flying-white discard).
   - `Ink Ramp (1D)`: tonal ramp texture (a horizontal gradient strip).
   - `Brush Detail`: brush / paper grain texture.
   - `Wash Noise`: noise that distorts the ramp UV.

### 3.2 Camera (post-process `NPR/IW_PP`)

1. Select the main camera.
2. Add Component → `Ink` (the `firs.cs` script).
3. Assign on the `Ink` component:
 - `Ink Shader`: `NPR/IW_PP` (i.e. `Ink-wash_PP.shader`).
   - `Paper Texture`: paper background.
   - `Ink Texture`: ink color texture.
   - `Blue Noise`: blue-noise texture.
4. Press Play.

### 3.3 Runtime Tweak Panel

1. Create an **empty GameObject** in the Hierarchy.
2. Add the `InkRuntimeUI` component to it.
3. At runtime, press **Tab** to show / hide the panel. The two top tabs split parameters into "post-process" and "ink-brush material".

---

## 4. Runtime Hotkeys

| Key | Action |
|---|---|
| `Tab` | Show / hide the InkRuntimeUI panel |
| `Space` | Toggle the post-process |
| `1` / `2` / `3` / `4` | Switch edge-detection operator (Contrast / Sobel-Feldman / Prewitt / DoG) |
| `W` / `A` / `S` / `D` | Move Camera in Front/Left/Back/Right |
| `Hold RMB` | Rotate the view |

---

## 5. Parameter Reference

### 5.1 Post-process (`Ink` script, backed by `NPR/IW_PP`)

**General / Edge**
- `Edge Detector`: which edge operator to use (Contrast / Sobel / Prewitt / DoG).
- `Edge Threshold (contrastThreshold)`: edge-strength threshold; higher keeps only stronger edges.
- `Luminance Contrast`: contrast applied to the source greyscale.
- `Luminance Correction`: gamma correction on the greyscale, brightens or darkens overall.

**DoG**
- `DoG Sigma`: sigma of the first Gaussian, sets the base scale.
- `DoG K`: sigma multiplier of the second Gaussian relative to the first; controls line thickness.
- `DoG Gain`: gain on the DoG response; higher makes edges more visible.

**Stipple**
- `Stipple Size`: screen-space size of stipple dots.
- `Stipple World Scale`: world-space tiling density of the stipple noise (prevents drift when the camera moves).

**Ink Bleed**
- `Amount (bleedAmount)`: overall bleed strength.
- `Radius (bleedRadius)`: bleed sampling radius (in pixels).
- `Irregularity`: random perturbation of bleed direction / distance.
- `Iterations`: number of bleed blit iterations; more = softer.
- `Density`: shaping coefficient on the bleed sample density.
- `World Scale (bleedWorldScale)`: world-space tiling density of the bleed noise.

**Bleed - Dark Edge**
- `Dark Only`: only let dark edges bleed.
- `Dark Threshold`: luminance threshold for participating in bleed; lower = only the darkest pixels bleed.
- `Dark Softness`: softness of the dark-area gating transition.
- `Partial Threshold`: noise-driven culling of part of the contour, creates a broken / irregular look.

**Bleed - Fade**
- `Fade Gamma`: curve treating bleed as ink opacity (>1 fades to transparent quickly, <1 is softer).
- `Bleed Debug`: outputs the raw bleed greyscale so you can inspect the diffusion range.

**Debug Stage**
- `Debug Stage`: preview an intermediate stage (Final / Luminance / Edge / InkBleed / Stipple / CombineNoBleed).

**Master**
- `Post-Processing Enabled`: master switch; when off, the source image is passed through unchanged.

### 5.2 Ink-brush material (`NPR/IW_mat`, in `InkWash_Mat.shader`)

**Brush Stroke**
- `Ink Color`: stroke / ink color.
- `Stroke Width`: overall stroke width (view-space, depth-adaptive).
- `Stroke Jitter`: per-vertex jitter on stroke width.
- `Stroke Z Push`: amount the stroke geometry is pushed back along view Z to avoid Z-fighting.

**Wet Stroke (solid outline)**
- `Wet Noise Scale`: world-space tiling density of the wet-stroke noise.
- `Wet Cutoff`: wet-stroke discard threshold; higher discards more, creating gaps.
- `Wet Feather`: feather width around the wet-stroke threshold.

**Dry Stroke (flying-white)**
- `Dry Width Mul`: dry-stroke width multiplier relative to the wet stroke.
- `Dry Noise Scale`: dry-stroke noise tiling (kept different from the wet one to avoid alignment).
- `Dry Cutoff`: dry-stroke soft cutoff; controls flying-white coverage.
- `Dry Feather`: soft transition width on the flying-white edge.

**Ink Wash**
- `Wash UV Distortion`: strength of the noise distortion applied to the ramp UV.
- `Brush Detail Power`: gamma on the brush-detail texture; higher = more contrast.
- `Brush Detail Blend`: blend ratio between brush detail and base ink color.

**Smoothing (ramp)**
- `Smoothing Mode`: ramp sampling mode (Off / Box 5-tap / Anisotropic stretched along the tone axis).
- `Smooth Radius`: smoothing radius; larger gives softer tonal transitions.

**Rim**
- `Enable Rim`: toggle the rim light.
- `Rim Color` / `Rim Power` / `Rim Gain`: rim color / falloff exponent / intensity.


---

## 6. File Layout

```
Assets/
├─ Shader/
│  ├─ Ink-wash_PP.shader      # Post-process shader (NPR/IW_PP)
│  ├─ InkWash_Mat.shader       # Object ink-wash shader (NPR/IW_mat)
├─ Script/
│  ├─ firs.cs      # Post-process driver (class Ink)
│  ├─ InkRuntimeUI.cs         # Runtime IMGUI tweak panel
│  └─ Camera.cs     # Camera control
└─ ... paper / ink / brush / blue-noise / ink-ramp textures
