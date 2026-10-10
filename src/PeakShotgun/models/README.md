# Embedded game assets

Only these files are referenced from `Peak.Shotgun.csproj`:

| File | Role |
|------|------|
| `shotgun.obj` | Custom mesh (Wavefront, with UVs) |
| `shotgun_albedo_1k.png` | Base color embedded as `Peak.Shotgun.models.shotgun_albedo.png` |
| `ammo_pile.obj` | Campfire ammo pile mesh |
| `ammo_pile_albedo_1k.png` | Ammo pile base color embedded as `Peak.Shotgun.models.ammo_pile_albedo.png` |

Regenerate the shotgun from the FBX under `src-mesh/`:

```bash
cd src-mesh
python build_mesh.py
```

Ammo pile source art and rebuild notes: `src-mesh/ammo_pile/`.

Do not commit MeshLab sidecars (`.mtl`, `dummy.png`) — `build_mesh.py` strips them.
