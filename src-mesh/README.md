# PeakShotgun mesh compile

Rebuild the in-game shotgun mesh and albedo from the source art in this folder.

## Source files

| File | Role |
|------|------|
| `shotgun.fbx` | Stylized shotgun mesh |
| `textures/albedo.jpg` | Base color (embedded as 1024² PNG) |
| `textures/normal.jpg` | Normal map (not used in-game yet) |
| `textures/metallic.jpg` | Metallic map (not used in-game yet) |
| `textures/roughness.jpg` | Roughness map (not used in-game yet) |

## Build

```bash
cd src-mesh
python build_mesh.py
```

Dependencies: `pymeshlab`, `Pillow`.

Writes only:

- `../src/PeakShotgun/models/shotgun.obj`
- `../src/PeakShotgun/models/shotgun_albedo_1k.png`

MeshLab `.mtl` / `dummy.png` sidecars are stripped — the plugin loads the albedo as an embedded resource, not via Wavefront materials.

Optional decimation (default cap 25k faces):

```bash
python build_mesh.py --target-faces 12000
```

Then from the repo root:

```bash
dotnet build peak-shotgun.slnx -c Release
```
