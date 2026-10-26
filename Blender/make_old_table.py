"""Builds the old wooden table of the VRoom entrance room and exports it to Unity.

Run it inside Blender (Scripting tab, or `exec(open(path).read())`). The table is 1.6 x 0.9 m, 0.78 m high, origin at
the centre of the floor: a top of six worn boards with rounded edges, four legs and the rails under the top. It writes
Assets/VRoom/Models/OldTable.fbx and the wood textures to Assets/VRoom/Textures/, then removes the temporary objects.
The wood texture tiles once per metre, its grain runs along U.
"""
import math
import os

import bmesh
import bpy
import numpy as np

PROJECT = r"D:\work\VRoom"
MODEL_DIR = os.path.join(PROJECT, "Assets", "VRoom", "Models")
TEXTURE_DIR = os.path.join(PROJECT, "Assets", "VRoom", "Textures")
os.makedirs(MODEL_DIR, exist_ok=True)
os.makedirs(TEXTURE_DIR, exist_ok=True)

LENGTH, DEPTH, HEIGHT, TOP_T = 1.6, 0.9, 0.78, 0.06
LEG = 0.075
LEG_X, LEG_Y = 0.72, 0.38
BOARDS = 6
rng = np.random.default_rng(11)


# ---------------------------------------------------------------- texture
def aniso_blur(a, sx, sy):
    """Gaussian blur through the FFT with different widths along x and y (the result wraps, so it tiles)."""
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * np.pi ** 2 * ((sx * fx) ** 2 + (sy * fy) ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * k))


def norm01(a):
    return (a - a.min()) / (a.max() - a.min() + 1e-9)


def smooth01(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def save_png(name, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    out = np.ones((h, w, 4), dtype=np.float32)
    out[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = os.path.join(TEXTURE_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def scratch_layer(size, count, length=(80, 500), along=0.7):
    layer = np.zeros((size, size))
    for _ in range(count):
        x0, y0 = rng.random(2) * size
        ang = (rng.normal(0, 0.12) if rng.random() < along else rng.random() * math.pi)
        steps = int(rng.integers(*length))
        t = np.arange(steps)
        xs = (x0 + np.cos(ang) * t).astype(int) % size
        ys = (y0 + np.sin(ang) * t).astype(int) % size
        layer[ys, xs] = 0.3 + 0.7 * rng.random()
    return np.clip(aniso_blur(layer, 0.9, 0.9) * 6.0, 0, 1)


def wood_textures(size=2048):
    """Old dark wood: growth rings with warp, fine fibres, dark grain cracks, grey patina, stains, cup rings, scratches."""
    yy = np.linspace(0, 1, size)[:, None] * np.ones((1, size))
    warp = norm01(aniso_blur(rng.standard_normal((size, size)), 220, 10)) - 0.5
    rings = 0.5 + 0.5 * np.sin(2 * np.pi * (13 * yy + 2.2 * warp))
    fibres = norm01(aniso_blur(rng.standard_normal((size, size)), 60, 0.9))
    pores = norm01(aniso_blur(rng.standard_normal((size, size)), 14, 0.7))
    crack_n = norm01(aniso_blur(rng.standard_normal((size, size)), 320, 1.3))
    cracks = smooth01(1.0 - np.abs(crack_n * 2 - 1), 0.965, 0.997)
    patina = smooth01(norm01(aniso_blur(rng.standard_normal((size, size)), 90, 60)), 0.45, 0.8)
    stains = norm01(aniso_blur(rng.standard_normal((size, size)), 80, 80))
    scr = scratch_layer(size, 80)

    # cup rings and a dark spill
    xx = np.arange(size)[None, :] * np.ones((size, 1))
    rim = np.zeros((size, size))
    for cx, cy, r in [(520, 700, 150), (1480, 1250, 135), (900, 1650, 120)]:
        d = np.sqrt((xx - cx) ** 2 + (np.arange(size)[:, None] - cy) ** 2)
        rim = np.maximum(rim, np.exp(-((d - r) / 7.0) ** 2) * 0.9 + np.exp(-(d / (r * 0.9)) ** 4) * 0.12)
    rim = aniso_blur(rim, 2.0, 2.0)

    base = np.array([0.30, 0.185, 0.105])
    tone = 0.62 + 0.55 * (rings * 0.55 + fibres * 0.35 + pores * 0.10 - 0.5) + 0.30 * (stains - 0.5)
    albedo = base[None, None, :] * tone[..., None]
    grey = np.array([0.38, 0.34, 0.29])
    albedo = albedo * (1 - patina[..., None] * 0.30) + grey[None, None, :] * tone[..., None] * patina[..., None] * 0.30
    albedo = albedo * (1 - cracks[..., None] * 0.8)
    albedo = albedo * (1 - rim[..., None] * 0.55)
    albedo = albedo + scr[..., None] * np.array([0.05, 0.04, 0.03])[None, None, :]
    save_png("OldWood_Albedo", albedo)

    height = rings * 0.20 + fibres * 0.30 + pores * 0.15 - cracks * 1.0 - scr * 0.18 - rim * 0.10
    gy, gx = np.gradient(height)
    nx, ny, nz = -gx * 16.0, -gy * 16.0, np.ones_like(height)
    length = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    save_png("OldWood_Normal", np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5)


# ---------------------------------------------------------------- geometry
def add_box(bm, cx, cy, cz, sx, sy, sz, bevel):
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    v = [bm.verts.new((cx + dx * hx, cy + dy * hy, cz + dz * hz))
         for dx, dy, dz in [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                            (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]]
    faces = [bm.faces.new([v[i] for i in idx])
             for idx in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]]
    edges = {e for f in faces for e in f.edges}
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    bmesh.ops.bevel(bm, geom=list(edges), offset=bevel, segments=2, affect="EDGES")


def build(bm):
    board = DEPTH / BOARDS
    for k in range(BOARDS):
        cy = -DEPTH / 2 + board * (k + 0.5)
        # the old boards sit a little uneven
        dz = float(rng.normal(0, 0.0007))
        add_box(bm, 0, cy, HEIGHT - TOP_T / 2 + dz, LENGTH, board - 0.0016, TOP_T, 0.0042)
    for sx in (-1, 1):
        for sy in (-1, 1):
            add_box(bm, sx * LEG_X, sy * LEG_Y, (HEIGHT - TOP_T) / 2, LEG, LEG, HEIGHT - TOP_T, 0.0035)
    rail_h, rail_t = 0.09, 0.04
    zc = HEIGHT - TOP_T - rail_h / 2
    for sy in (-1, 1):
        add_box(bm, 0, sy * (LEG_Y - LEG / 2 + rail_t / 2), zc, 2 * LEG_X - LEG, rail_t, rail_h, 0.003)
    for sx in (-1, 1):
        add_box(bm, sx * (LEG_X - LEG / 2 + rail_t / 2), 0, zc, rail_t, 2 * LEG_Y - LEG, rail_h, 0.003)
    # a low stretcher between the legs, as on old farm tables
    add_box(bm, 0, 0, 0.16, 2 * LEG_X - LEG, 0.045, 0.045, 0.003)


def assign_uvs(bm):
    uv = bm.loops.layers.uv.verify()
    board = DEPTH / BOARDS
    offsets = [float(rng.random()) for _ in range(BOARDS)]
    for f in bm.faces:
        n = f.normal
        c = f.calc_center_median()
        cx, cy, cz = c
        is_leg = abs(cx) > 0.675 and abs(cy) > 0.335 and cz < HEIGHT - TOP_T - 0.0005
        is_top = cz > HEIGHT - TOP_T - 0.002
        is_stretcher = cz < 0.25
        board_i = min(max(int((cy + DEPTH / 2) / board), 0), BOARDS - 1)
        for loop in f.loops:
            x, y, z = loop.vert.co
            if is_top:
                off = offsets[board_i]
                if abs(n.z) > 0.5:
                    uvv = (x, y + off)
                elif abs(n.x) > abs(n.y):
                    uvv = (y, z + off)
                else:
                    uvv = (x, z + off)
            elif is_leg:
                if abs(n.z) > 0.5:
                    uvv = (x + 0.37, y)
                elif abs(n.x) > abs(n.y):
                    uvv = (z, y + 0.5)
                else:
                    uvv = (z, x + 0.5)
            elif is_stretcher:
                if abs(n.z) > 0.5:
                    uvv = (x, y + 0.2)
                elif abs(n.x) > abs(n.y):
                    uvv = (y, z)
                else:
                    uvv = (x, z + 0.3)
            else:  # rails under the top
                if abs(n.z) > 0.5:
                    uvv = (x, y + 0.7)
                elif abs(n.x) > abs(n.y):
                    uvv = (y, z + 0.1)
                else:
                    uvv = (x, z + 0.6)
            loop[uv].uv = uvv


# ---------------------------------------------------------------- build
wood_textures()

bm = bmesh.new()
build(bm)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
assign_uvs(bm)
mesh = bpy.data.meshes.new("OldTable")
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new("OldTable", mesh)
bpy.context.scene.collection.objects.link(obj)
mat = bpy.data.materials.get("OldWood") or bpy.data.materials.new("OldWood")
obj.data.materials.append(mat)
for poly in mesh.polygons:
    poly.use_smooth = False

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
path = os.path.join(MODEL_DIR, "OldTable.fbx")
bpy.ops.export_scene.fbx(
    filepath=path,
    use_selection=True,
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    mesh_smooth_type="FACE",
    add_leaf_bones=False,
)

bpy.data.objects.remove(obj, do_unlink=True)
bpy.data.meshes.remove(mesh)
bpy.data.materials.remove(mat)
print("Exported", path)
