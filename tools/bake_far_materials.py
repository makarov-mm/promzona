"""Bake the imported low-detail meshes from their matching close-detail models.

Run from the source root with Python 3, NumPy and Pillow. Each axis projection
retains its own colour and glass/metal mask, including side and end views.
"""
import io
import json
from pathlib import Path
import struct

import numpy as np
from PIL import Image


def read_glb(path):
    data = path.read_bytes()
    length = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20 + length])
    binary = data[28 + length:]

    def array(index):
        acc = doc["accessors"][index]
        view = doc["bufferViews"][acc["bufferView"]]
        dtype = np.dtype({5121: "u1", 5123: "<u2", 5125: "<u4", 5126: "<f4"}[acc["componentType"]])
        width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[acc["type"]]
        offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        return np.ndarray((acc["count"], width), dtype, binary, offset,
                          (view.get("byteStride", width * dtype.itemsize), dtype.itemsize))

    prim, = doc["meshes"][0]["primitives"]
    attr = prim["attributes"]
    return (array(attr["POSITION"]), array(attr["NORMAL"]), array(attr["TEXCOORD_0"]),
            array(prim["indices"]).reshape(-1, 3))


# Horizontal and vertical texture axes and depth axis (greater is nearer).
AXES = [(2, 1, 0), (2, 1, 0), (0, 2, 1), (0, 2, 1), (0, 1, 2), (0, 1, 2)]
TILE = 128
PAD = 6


def projection(pos, face, bounds):
    u, v, d = AXES[face]
    lo, hi = bounds
    scale = (TILE - 2 * PAD - 1) / np.maximum(hi - lo, .001)
    return np.column_stack((PAD + (pos[:, u] - lo[u]) * scale[u],
                            PAD + (hi[v] - pos[:, v]) * scale[v],
                            pos[:, d] * (-1 if face % 2 == 0 else 1)))


def bake_view(parts, face, bounds):
    colour = np.zeros((TILE, TILE, 4), dtype=np.uint8)
    depth = np.full((TILE, TILE), -np.inf)
    for pos, uv, triangles, bitmap, tint, alpha in parts:
        projected = projection(pos, face, bounds)
        for ids in triangles:
            a, b, c = projected[ids]
            edge1, edge2 = b - a, c - a
            det = edge1[0] * edge2[1] - edge2[0] * edge1[1]
            if abs(det) < .0001:
                continue
            triangle = projected[ids, :2]
            x0, y0 = np.maximum(0, np.ceil(triangle.min(axis=0)).astype(int))
            x1, y1 = np.minimum(TILE - 1, np.floor(triangle.max(axis=0)).astype(int))
            if x1 < x0 or y1 < y0:
                continue
            y, x = np.ogrid[y0:y1 + 1, x0:x1 + 1]
            u = ((x - a[0]) * edge2[1] - (y - a[1]) * edge2[0]) / det
            v = ((y - a[1]) * edge1[0] - (x - a[0]) * edge1[1]) / det
            z = a[2] + edge1[2] * u + edge2[2] * v
            region = np.s_[y0:y1 + 1, x0:x1 + 1]
            mask = (u >= 0) & (v >= 0) & (u + v <= 1) & (z > depth[region])
            if not mask.any():
                continue
            rgb = tint
            if bitmap is not None:
                st = uv[ids[0]] + u[mask, None] * (uv[ids[1]] - uv[ids[0]]) + v[mask, None] * (uv[ids[2]] - uv[ids[0]])
                pixel = (np.mod(st, 1) * [bitmap.shape[1], bitmap.shape[0]]).astype(int)
                rgb = bitmap[pixel[:, 1], pixel[:, 0]] * tint / 255
            colour[region][:, :, :3][mask] = rgb
            colour[region][:, :, 3][mask] = alpha
            depth[region][mask] = z[mask]
    # Extend the nearest edge into uncovered texels so mipmaps never sample blank canvas.
    filled = np.isfinite(depth)
    if not filled.any():
        raise ValueError("Empty directional view")
    while not filled.all():
        grown = filled.copy()
        for axis, shift in ((0, 1), (0, -1), (1, 1), (1, -1)):
            adjacent = np.roll(filled, shift, axis)
            if axis == 0:
                adjacent[0 if shift == 1 else -1, :] = False
            else:
                adjacent[:, 0 if shift == 1 else -1] = False
            take = adjacent & ~grown
            colour[take] = np.roll(colour, shift, axis)[take]
            grown |= take
        filled = grown
    return colour


def write_glb(path, pos, normal, uv, indices, png):
    binary = bytearray()
    views, accessors = [], []

    def view(data):
        binary.extend(bytes(-len(binary) % 4))
        views.append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(data)})
        binary.extend(data)
        return len(views) - 1

    for values, dtype, component, kind in ((pos, "<f4", 5126, "VEC3"), (normal, "<f4", 5126, "VEC3"),
                                            (uv, "<f4", 5126, "VEC2"), (indices, "<u2", 5123, "SCALAR")):
        acc = {"bufferView": view(values.astype(dtype).tobytes()), "componentType": component,
               "count": len(values), "type": kind}
        if not accessors:
            acc.update(min=pos.min(axis=0).tolist(), max=pos.max(axis=0).tolist())
        accessors.append(acc)
    image = view(png)
    binary.extend(bytes(-len(binary) % 4))
    doc = {"asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
           "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2},
                                       "indices": 3, "material": 0}]}],
           "materials": [{"pbrMetallicRoughness": {"baseColorTexture": {"index": 0}}}],
           "textures": [{"source": 0}], "images": [{"bufferView": image, "mimeType": "image/png"}],
           "buffers": [{"byteLength": len(binary)}], "bufferViews": views, "accessors": accessors}
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    path.write_bytes(struct.pack("<4s4I", b"glTF", 2, 28 + len(text) + len(binary), len(text), 0x4e4f534a)
                     + text + struct.pack("<2I", len(binary), 0x004e4942) + binary)


def bake(folder):
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf8"))
    far, = manifest["lods"][-1]
    if far["material"] != -1:
        return
    path = folder / far["file"]
    pos, normals, _, tris = read_glb(path)
    parts = []
    textures = {}
    for part in manifest["lods"][1]:
        points, _, uv, indices = read_glb(folder / part["file"])
        mat = manifest["materials"][part["material"]]
        texture = mat.get("colorMap")
        if texture and texture not in textures:
            with Image.open(folder / texture) as im:
                textures[texture] = np.asarray(im.convert("RGB"))
        mask = 12 if mat.get("glass") else round(255 - 100 * mat.get("metal", 0))
        parts.append((points, uv, indices, textures.get(texture), 255 * np.array(mat.get("color", [1,1,1])[:3]), mask))
    points = np.concatenate([pos] + [p[0] for p in parts])
    bounds = points.min(axis=0), points.max(axis=0)
    atlas = Image.new("RGBA", (3 * TILE, 2 * TILE))
    for face in range(6):
        atlas.paste(Image.fromarray(bake_view(parts, face, bounds)), ((face % 3) * TILE, (face // 3) * TILE))
    png = io.BytesIO()
    atlas.save(png, format="PNG")
    avg = normals[tris].mean(axis=1)
    axis = np.abs(avg).argmax(axis=1)
    faces = 2 * axis + (avg[np.arange(len(axis)), axis] >= 0)
    # Split vertices only at projection seams. Deduplication by attributes makes a rebake idempotent.
    verts = np.column_stack((pos[tris.ravel()], normals[tris.ravel()], np.repeat(faces, 3)))
    verts, indices = np.unique(verts, axis=0, return_inverse=True)
    uv = np.zeros((len(verts), 2))
    for face in range(6):
        mask = verts[:, 6] == face
        xy = projection(verts[mask, :3], face, bounds)[:, :2] + .5
        uv[mask] = (xy / TILE + [face % 3, face // 3]) / [3, 2]
    assert len(verts) < 65536
    (folder / "far-color.png").write_bytes(png.getvalue())
    write_glb(path, verts[:, :3], verts[:, 3:6], uv, indices.ravel(), png.getvalue())
    manifest_file = folder / "manifest.json"
    # Preserve the rest of the hand-edited manifest, including its formatting.
    text = manifest_file.read_text(encoding="utf8")
    if '"farUvTopDown": true' in text:
        manifest_file.write_text(text.replace('"farUvTopDown": true', '"farUvTopDown": false'), encoding="utf8")
    print(folder.name, flush=True)


if __name__ == "__main__":
    for file in sorted(Path("Content/Models/Imported").glob("*/far-color.png")):
        bake(file.parent)

