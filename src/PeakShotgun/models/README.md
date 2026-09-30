# Embedded game assets

Only these files are referenced from `PeakShotgun.csproj`:

| File | Role |
|------|------|
| `shotgun.obj` | Custom mesh (Wavefront, with UVs) |
| `shotgun_albedo_1k.png` | Base color embedded as `PeakShotgun.models.shotgun_albedo.png` |

Regenerate both from the FBX under `src-mesh/`:

```bash
cd src-mesh
python build_mesh.py
```

Do not commit MeshLab sidecars (`.mtl`, `dummy.png`) — `build_mesh.py` strips them.
