# SilkGrass

A real-time grass renderer for C# and OpenGL 4.6, built on
[Silk.NET](https://github.com/dotnet/Silk.NET). It draws an endless,
wind-swept meadow of about a million visible blades per frame (picked from 11.6
million candidates), with trampling, a rolling swell, rain and temporal
anti-aliasing. Every setting can be changed live in an ImGui panel.

It has no assets. The blades, terrain, sky, clouds and noise textures are all
generated in code and shaders.

## Running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU
with OpenGL 4.6 drivers.

```
dotnet run -c Release
```

### Controls

| Input | Action |
| --- | --- |
| Right mouse (hold) | Look around |
| Left mouse (hold) | Trample the grass under the cursor |
| W A S D | Move |
| Space / E, Ctrl / Q | Up, down |
| Shift | Move faster |
| Mouse wheel | Change move speed |
| F1 | Show or hide the tuning panel |
| F12 | Save a screenshot to `screenshots/` next to the executable |
| Esc | Quit |

### Command line

```
--size 1920x1080                 window size
--camera x,y,z,yaw,pitch         start pose (y above the ground, angles in degrees)
--weather clear|rain|storm       start weather
--aa taa|msaa|off                anti-aliasing (default taa)
--fidelity high|medium|low       grass shader fidelity (default high)
--no-panel                       start with the panel hidden
--capture shot.png --frames 180  render, save a screenshot, exit
```

## How it works

Each frame:

1. **Trample simulation** (`shaders/sim/trample.comp`): two toroidal RGBA16F
   textures follow the camera. A fine one covers the near field, with diffusion
   and a short wave depression. A coarse one covers the mid field. The brush (or
   the camera, if enabled) stamps into both, and trampled grass recovers over a
   few seconds.
2. **CPU pre-cull** (`Grass/GrassCellCuller.cs`): each ring of candidate blades
   is sorted into a 32x32 grid. Cells that cannot hold a visible blade are
   skipped, and the rest are listed nearest-first so the depth test can reject
   hidden blades early.
3. **GPU cull** (`shaders/grass/grass_cull.comp`): one thread per candidate.
   Each blade is wrapped around the camera (so a finite buffer covers an endless
   field), kept or dropped by a continuous density curve, frustum-tested and
   snapped to the terrain. Survivors are compacted into four distance buckets,
   and their counts go straight into indirect draw commands.
4. **Grass draw** (`shaders/grass/grass.vert`, `grass.frag`): one indirect draw
   per bucket. Each bucket uses a variant of a single shader source:
   - **Near:** full physics and micro-flutter.
   - **Mid:** trail physics.
   - **Far and distant:** static blades, with the whole colour pipeline run per
     vertex.

   Colour and fog maths are shared by all variants, so no seam shows where the
   buckets meet. Every bucket writes TAA motion vectors.
5. **Ground, horizon carpet, sky, rain**, then **post-processing**: TAA
   resolve and sharpening, bloom, distance blur, ACES tonemap, split toning,
   colour grade.

### Level of detail

Blade density follows one curve, `density(distance)`. Four capacity rings
supply the blades through a ladder: denser rings supply first and fade out at
their edges, so the rendered density follows the curve without rings or seams.
Blade width is not set directly. It is derived from a coverage target (density
x width), so lowering the density makes blades wider instead of thinning the
field. The *Grass density and LOD* section of the panel plots the target
density, the density the rings actually supply, and the resulting coverage.

### Look

- Wind sway from a baked noise texture, with flutter in the near field.
- A resting lean for every blade.
- Metre-scale colour patches and large macro tint regions.
- A rolling sheen band.
- Drifting cloud shadows with a cool, saturated shade tint.
- Valley and hill height gradient.
- Map-style relief lighting on far slopes.
- Wet darkening and rain splashes.
- A height and distance fog with sun scatter.
- The swell: three wave trains that lift the grass and the ground under it,
  while the terrain stays still.

## Project layout

| Folder | Contents |
| --- | --- |
| `App/` | Entry point, window, input, command line |
| `Cameras/` | Free-fly camera |
| `Graphics/` | Shader compiler (includes, defines, in-place rebuilds), render targets, screenshot encoder |
| `Grass/` | Rings, cell culler, buckets, blade meshes, noise textures, trample sim, grass renderer, LOD settings |
| `Terrain/` | Terrain height (CPU mirror of the shader), ground and horizon meshes, swell |
| `Rendering/` | Frame orchestration, uniform blocks, sky, terrain, rain, post-processing, TAA velocity |
| `Settings/` | All tunables and weather presets |
| `UI/` | Tuning panel |
| `shaders/` | GLSL (copied next to the executable at build time) |

## License

MIT, see [LICENSE](LICENSE).
