# Ammo pile source art

| File | Role |
|------|------|
| `ammo_pile.fbx` | Ammo pile mesh |
| `textures/albedo.jpg` | Base color |

Rebuild the embedded game assets:

```bash
cd src-mesh
python - <<'PY'
from pathlib import Path
import pymeshlab
from PIL import Image

ROOT = Path("ammo_pile")
MODELS = Path("../src/PeakShotgun/models")
ms = pymeshlab.MeshSet()
ms.load_new_mesh(str(ROOT / "ammo_pile.fbx"))
obj = MODELS / "ammo_pile.obj"
ms.save_current_mesh(str(obj))
text = obj.read_text(encoding="utf-8", errors="ignore")
obj.write_text("".join(l for l in text.splitlines(keepends=True) if not l.startswith("mtllib ") and not l.startswith("usemtl ")), encoding="utf-8")
for junk in (obj.with_suffix(".obj.mtl"), MODELS / "dummy.png", MODELS / f"{obj.stem}.mtl"):
    if junk.is_file():
        junk.unlink()
img = Image.open(ROOT / "textures" / "albedo.jpg").convert("RGB").resize((1024, 1024), Image.Resampling.LANCZOS)
img.save(MODELS / "ammo_pile_albedo_1k.png", optimize=True)
print("Done.")
PY
```

Then `dotnet build Peak.Shotgun.slnx -c Release`.
