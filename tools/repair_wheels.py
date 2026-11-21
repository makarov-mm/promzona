"""Put the wheels of imported vehicles back on their axles.

Run from the source root with Python 3 and NumPy:

    python tools/repair_wheels.py                      report on every imported vehicle
    python tools/repair_wheels.py --write NAME...      repair the named ones

A vehicle is cut into parts: the body, the front doors and, from part 3 on, the wheels, each
of which the game turns about the pivot that manifest.json gives it. Where the importer did
not find an axle it wrote a guess (three tenths of the length from the middle, as high as a
guessed radius) and left pieces of the wheel arches in the wheel parts. Such a wheel swings
round a point beside itself, and scraps of the body fly round with it.

In the mesh the wheel is easy to tell: it is the piece that goes all the way round an axis
across the vehicle. The tool finds that axis and the radius of the tyre, writes them down as
the pivot and the radius, moves whatever else lies in a wheel part back to the body and
brings to the wheel part what the body had kept of the wheel.
"""
import json
from pathlib import Path
import struct
import sys

import numpy as np

ROOT = Path("Content/Models/Imported")
SECTORS = 12
TYPES = {5123: "<u2", 5125: "<u4", 5126: "<f4"}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}


class Glb:
    """One part: a single primitive with positions, normals, texture coordinates and indices."""

    def __init__(self, path):
        self.path = path
        data = path.read_bytes()
        length = struct.unpack_from("<I", data, 12)[0]
        self.doc = json.loads(data[20:20 + length])
        self.binary = data[28 + length:]
        prim, = self.doc["meshes"][0]["primitives"]
        if set(prim["attributes"]) != {"POSITION", "NORMAL", "TEXCOORD_0"} or len(self.doc["meshes"]) != 1:
            raise ValueError(f"{path}: not a part this tool can rewrite")
        self.prim = prim
        self.pos, self.normal, self.uv = (self.array(prim["attributes"][name]).astype("<f4")
                                          for name in ("POSITION", "NORMAL", "TEXCOORD_0"))
        self.tris = self.array(prim["indices"]).astype(np.int64).reshape(-1, 3)

    def array(self, index):
        acc = self.doc["accessors"][index]
        view = self.doc["bufferViews"][acc["bufferView"]]
        dtype, width = np.dtype(TYPES[acc["componentType"]]), WIDTHS[acc["type"]]
        offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        return np.ndarray((acc["count"], width), dtype, self.binary, offset,
                          (view.get("byteStride", width * dtype.itemsize), dtype.itemsize))

    def take(self, keep):
        """The triangles left out, as loose vertices; the part keeps those of `keep`."""
        gone = self.tris[~keep].ravel()
        loose = self.pos[gone], self.normal[gone], self.uv[gone]
        used, self.tris = np.unique(self.tris[keep], return_inverse=True)
        self.tris = self.tris.reshape(-1, 3)
        self.pos, self.normal, self.uv = self.pos[used], self.normal[used], self.uv[used]
        return loose

    def add(self, pos, normal, uv):
        self.tris = np.concatenate((self.tris, len(self.pos) + np.arange(len(pos)).reshape(-1, 3)))
        self.pos, self.normal, self.uv = (np.concatenate(pair) for pair in
                                          ((self.pos, pos), (self.normal, normal), (self.uv, uv)))

    def save(self):
        # The document is kept as the importer wrote it; only the geometry in it is replaced.
        doc, attr = self.doc, self.prim["attributes"]
        index = doc["accessors"][self.prim["indices"]]
        if index["componentType"] == 5123 and len(self.pos) > 65535:
            raise ValueError(f"{self.path}: too many vertices for its index type")
        fresh = {attr["POSITION"]: self.pos, attr["NORMAL"]: self.normal, attr["TEXCOORD_0"]: self.uv,
                 self.prim["indices"]: self.tris.astype(TYPES[index["componentType"]]).reshape(-1, 1)}
        chunks = {}
        for number, values in fresh.items():
            acc = doc["accessors"][number]
            acc["count"] = len(values)
            acc.pop("byteOffset", None)
            chunks[acc["bufferView"]] = values.tobytes()
        position = doc["accessors"][attr["POSITION"]]
        position["min"], position["max"] = self.pos.min(axis=0).tolist(), self.pos.max(axis=0).tolist()
        binary = bytearray()
        for number, view in enumerate(doc["bufferViews"]):
            data = chunks.get(number)
            if data is None:
                start = view.get("byteOffset", 0)
                data = bytes(self.binary[start:start + view["byteLength"]])
            view.pop("byteStride", None)
            binary.extend(bytes(-len(binary) % 4))
            view["byteOffset"], view["byteLength"] = len(binary), len(data)
            binary.extend(data)
        binary.extend(bytes(-len(binary) % 4))
        doc["buffers"][0]["byteLength"] = len(binary)
        text = json.dumps(doc, separators=(",", ":")).encode()
        text += b" " * (-len(text) % 4)
        self.path.write_bytes(struct.pack("<4s4I", b"glTF", 2, 28 + len(text) + len(binary), len(text), 0x4e4f534a)
                              + text + struct.pack("<2I", len(binary), 0x004e4942) + binary)


def axle(points):
    """Height and place along the vehicle of the axis most points lie on rings about."""
    y, z = points[:, 1], points[:, 2]
    centre = np.array([np.median(y), np.median(z)])
    for span in (.3, .03, .003):
        offsets = np.linspace(-span, span, 31)
        scores = [[(np.histogram(np.hypot(y - centre[0] - a, z - centre[1] - b),
                                 bins=np.arange(0, 2, .004))[0].astype(float) ** 2).sum()
                   for b in offsets] for a in offsets]
        i, j = np.unravel_index(np.argmax(scores), (31, 31))
        centre += offsets[i], offsets[j]
    return centre


def pieces(parts):
    """A number for every triangle of the parts: that of the connected piece it belongs to."""
    points = np.concatenate([part.pos for part in parts])
    starts = np.cumsum([0] + [len(part.pos) for part in parts])
    tris = np.concatenate([part.tris + start for part, start in zip(parts, starts)])
    weld = np.unique(np.round(points / 2e-4).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    parent = np.arange(weld.max() + 1)

    def root(node):
        while parent[node] != node:
            parent[node] = node = parent[parent[node]]
        return node

    for a, b, c in weld[tris]:
        parent[root(b)] = parent[root(c)] = root(a)
    return points, tris, np.array([root(node) for node in weld[tris[:, 0]]])


def wheel_of(own, bodies, centre=None):
    """The axle of the wheel, its tyre and, for each wheel part and each body part, which
    of its triangles are the wheel."""
    points = np.concatenate([part.pos for part in own])
    if centre is None:
        centre = axle(points)
    radius = np.hypot(points[:, 1] - centre[0], points[:, 2] - centre[1])
    sector = ((np.arctan2(points[:, 1] - centre[0], points[:, 2] - centre[1]) + np.pi)
              * SECTORS / (2 * np.pi)).astype(int) % SECTORS
    # How far the part goes on each side of the axle. Scraps of the body go further on some
    # sides and a tyre with a bite out of it not so far on others: the tyre is what is usual.
    reach = np.zeros(SECTORS)
    np.maximum.at(reach, sector, radius)
    if not reach.all():
        return None
    tyre = np.median(reach)
    # The wheel is every piece of the whole vehicle that the wheel part has a share of and
    # that does not go out of the tyre: a piece that does is the body, whatever of it lies within.
    points, tris, piece = pieces(own + bodies)
    radius = np.hypot(points[:, 1] - centre[0], points[:, 2] - centre[1])
    piece = np.unique(piece, return_inverse=True)[1].ravel()
    furthest = np.zeros(piece.max() + 1)
    np.maximum.at(furthest, piece, radius[tris].max(axis=1))
    shared = np.zeros(piece.max() + 1, bool)
    shared[piece[:sum(len(part.tris) for part in own)]] = True
    wheel = (furthest[piece] <= tyre * 1.015 + .002) & shared[piece]
    if not wheel.any():
        return None
    across = points[np.unique(tris[wheel]), 0]
    masks = np.split(wheel, np.cumsum([len(part.tris) for part in own + bodies])[:-1])
    return centre, tyre, (across.min() + across.max()) / 2, masks[:len(own)], masks[len(own):]


def repair(folder, write):
    manifest_file = folder / "manifest.json"
    text = manifest_file.read_bytes().decode("utf8")
    manifest = json.loads(text)
    wheels = manifest.get("wheels", [])
    if not wheels:
        return
    slip, scraps, lost, changed = [], 0, 0, {}
    for level, lod in enumerate(manifest["lods"]):
        if not any(3 <= part["part"] < 3 + len(wheels) for part in lod):
            continue
        loaded = {part["file"]: Glb(folder / part["file"]) for part in lod if part["part"] < 3 + len(wheels)}
        bodies = [part for part in lod if part["part"] == 0]
        for number, wheel in enumerate(wheels):
            entries = [part for part in lod if part["part"] == 3 + number]
            if not entries:
                continue
            # The far levels are thinner copies of the first: the axle is where it was found there.
            found = wheel_of([loaded[part["file"]] for part in entries], [loaded[part["file"]] for part in bodies],
                             np.array(wheel["pivot"][1:]) if level else None)
            if found is None:
                print(f"{folder.name}: no wheel in part {3 + number} of level {level}; left as it is")
                return
            centre, tyre, middle, own, others = found
            if level == 0:
                slip.append((float(np.hypot(*(centre - wheel["pivot"][1:]))), wheel["radius"], float(tyre)))
                wheel["pivot"] = [round(float(middle), 4), round(float(centre[0]), 4), round(float(centre[1]), 4)]
                wheel["radius"] = round(float(tyre), 4)
            # Scraps of the body go out of the wheel parts, and what the body kept of the
            # wheel comes back to them; each to the part of its own material.
            moves = []
            for part, keep in zip(entries, own):
                home = next((other for other in bodies if other["material"] == part["material"]), None)
                if home and not keep.all():
                    moves.append((home, loaded[part["file"]].take(keep)))
                    scraps += int((~keep).sum())
                    changed[part["file"]] = loaded[part["file"]]
            for part, mask in zip(bodies, others):
                home = next((other for other in entries if other["material"] == part["material"]), None)
                if home and mask.any():
                    moves.append((home, loaded[part["file"]].take(~mask)))
                    lost += int(mask.sum())
                    changed[part["file"]] = loaded[part["file"]]
            for home, loose in moves:
                loaded[home["file"]].add(*loose)
                changed[home["file"]] = loaded[home["file"]]
        for part in lod:
            if part["file"] in changed:
                part["triangles"] = len(changed[part["file"]].tris)
    worst = max(s[0] for s in slip)
    if worst < .01 and not scraps and not lost and all(abs(s[1] - s[2]) < .01 for s in slip):
        print(f"{folder.name}: in order")
        return
    print(f"{folder.name}: wheels up to {worst:.2f} m off their axles, radius {slip[0][1]:.2f} -> {slip[0][2]:.2f} m, "
          f"{scraps} triangles of the body in the wheel parts, {lost} of the wheels in the body"
          + ("; repaired" if write else ""))
    if not write:
        return
    for part in changed.values():
        part.save()
    manifest["wheelRepair"] = {"version": 1, "returnedBodyTriangles": scraps, "returnedWheelTriangles": lost}
    # The manifest keeps the line ends it had.
    fresh = json.dumps(manifest, indent=2) + ("\n" if text.endswith("\n") else "")
    manifest_file.write_bytes((fresh.replace("\n", "\r\n") if "\r\n" in text else fresh).encode("utf8"))


if __name__ == "__main__":
    names = [name for name in sys.argv[1:] if name != "--write"]
    for manifest in sorted(ROOT.glob("*/manifest.json")):
        if not names or manifest.parent.name in names:
            try:
                repair(manifest.parent, "--write" in sys.argv and manifest.parent.name in names)
            except ValueError as error:
                print(f"{manifest.parent.name}: {error}")
