#!/usr/bin/env python3
"""Rebuild PeakShotgun embedded mesh + albedo from source art in this folder.

Requires: pymeshlab, Pillow

    python build_mesh.py

Inputs (defaults):
  shotgun.fbx           — stylized shotgun mesh
  textures/albedo.jpg   — base color

Outputs:
  ../src/PeakShotgun/models/shotgun.obj
  ../src/PeakShotgun/models/shotgun_albedo_1k.png
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
import pymeshlab
from PIL import Image

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
MODELS = REPO / "src" / "PeakShotgun" / "models"
DEFAULT_TARGET_FACES = 25_000
ALBEDO_SIZE = 1024


def find_fbx() -> Path:
    preferred = ROOT / "shotgun.fbx"
    if preferred.is_file():
        return preferred
    matches = sorted(ROOT.glob("*.fbx"))
    if not matches:
        raise FileNotFoundError(f"No shotgun.fbx (or other .fbx) under {ROOT}")
    return matches[0]


def find_albedo() -> Path:
    preferred = ROOT / "textures" / "albedo.jpg"
    if preferred.is_file():
        return preferred
    tex = ROOT / "textures"
    for pattern in ("albedo.*", "*.jpg", "*.jpeg", "*.png"):
        matches = sorted(p for p in tex.glob(pattern) if p.is_file())
        if matches:
            return matches[0]
    raise FileNotFoundError(f"No albedo texture under {tex}")


def count_obj_uvs(path: Path) -> int:
    count = 0
    with path.open(encoding="utf-8", errors="ignore") as handle:
        for line in handle:
            if line.startswith("vt "):
                count += 1
    return count


def prepare_meshset(fbx: Path, target_faces: int) -> pymeshlab.MeshSet:
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(str(fbx))
    mesh = ms.current_mesh()
    print(f"Loaded {fbx.name}: {mesh.face_number()} faces, {mesh.vertex_number()} verts")
    if not mesh.has_wedge_tex_coord():
        raise RuntimeError("Source mesh has no UVs — FBX export may be broken.")

    wedge_uv = mesh.wedge_tex_coord_matrix()
    print(f"Wedge UVs: {wedge_uv.shape[0]} corners")

    if mesh.face_number() > target_faces:
        ms.apply_filter(
            "meshing_decimation_quadric_edge_collapse",
            targetfacenum=target_faces,
            preservenormal=True,
            preservetopology=True,
            preservetexture=True,
            optimalplacement=True,
        )
        mesh = ms.current_mesh()
        print(f"Decimated to {mesh.face_number()} faces")
        if not mesh.has_wedge_tex_coord():
            raise RuntimeError("Decimation dropped UVs; lower --target-faces or fix source.")

    return ms


def preview_loader_bounds(ms: pymeshlab.MeshSet) -> None:
    """Same center + max-extent normalize as PeakShotgun.ObjMeshLoader."""
    vs = ms.current_mesh().vertex_matrix().astype(np.float64)
    center = vs.mean(axis=0)
    v = vs - center
    max_extent = float(np.max(np.abs(v)))
    if max_extent > 1e-6:
        v = v / max_extent
    print(
        f"After in-game normalize — X [{v[:, 0].min():.3f}, {v[:, 0].max():.3f}] "
        f"(barrel should be +X), Y [{v[:, 1].min():.3f}, {v[:, 1].max():.3f}], "
        f"Z [{v[:, 2].min():.3f}, {v[:, 2].max():.3f}]"
    )


def scrub_meshlab_sidecars(obj_path: Path) -> None:
    """Drop MeshLab material junk — the plugin embeds albedo separately, not via .mtl."""
    text = obj_path.read_text(encoding="utf-8", errors="ignore")
    lines = [
        line
        for line in text.splitlines(keepends=True)
        if not line.startswith("mtllib ") and not line.startswith("usemtl ")
    ]
    obj_path.write_text("".join(lines), encoding="utf-8")

    for junk in (
        obj_path.with_suffix(".obj.mtl"),
        obj_path.parent / "dummy.png",
        obj_path.parent / f"{obj_path.stem}.mtl",
    ):
        if junk.is_file():
            junk.unlink()
            print(f"Removed {junk.name}")


def write_obj(ms: pymeshlab.MeshSet, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    ms.save_current_mesh(str(path))
    scrub_meshlab_sidecars(path)
    mesh = ms.current_mesh()
    uv = count_obj_uvs(path)
    if uv == 0:
        raise RuntimeError(f"{path.name} exported with zero vt lines — use pymeshlab export, not trimesh.")
    print(
        f"Wrote {path} ({path.stat().st_size // 1024} KiB, "
        f"{mesh.vertex_number()} verts, {mesh.face_number()} faces, {uv} vt)"
    )


def write_albedo(src: Path, dest: Path) -> None:
    img = Image.open(src).convert("RGB")
    if img.size != (ALBEDO_SIZE, ALBEDO_SIZE):
        img = img.resize((ALBEDO_SIZE, ALBEDO_SIZE), Image.Resampling.LANCZOS)
    dest.parent.mkdir(parents=True, exist_ok=True)
    img.save(dest, optimize=True)
    print(f"Wrote {dest} ({dest.stat().st_size // 1024} KiB) from {src.name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fbx", type=Path, help="Override source FBX")
    parser.add_argument("--albedo", type=Path, help="Override base-color image")
    parser.add_argument(
        "--target-faces",
        type=int,
        default=DEFAULT_TARGET_FACES,
        help=f"Quadric decimation cap (default {DEFAULT_TARGET_FACES})",
    )
    args = parser.parse_args()

    fbx = args.fbx or find_fbx()
    albedo_src = args.albedo or find_albedo()
    MODELS.mkdir(parents=True, exist_ok=True)

    ms = prepare_meshset(fbx, args.target_faces)
    preview_loader_bounds(ms)

    write_obj(ms, MODELS / "shotgun.obj")
    write_albedo(albedo_src, MODELS / "shotgun_albedo_1k.png")
    print("Done. Rebuild PeakShotgun.dll to embed the new assets.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
