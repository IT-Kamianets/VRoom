"""Builds the wrought-iron candlestick with its candle for VRoom and the flame texture, and exports them to Unity.

Run it inside Blender (Scripting tab, or `exec(open(path).read())`). The candlestick stands on the floor plane (z = 0),
0.59 m tall with the candle: a round foot, a stem with two knots, a drip pan and a cup; the wax candle has a melted top
and drips. Material slots: Iron, Wax, Wick. It writes Assets/VRoom/Models/Candlestick.fbx and the textures
Iron_Albedo/Iron_Normal/Flame to Assets/VRoom/Textures/, then removes the temporary objects from the open file.
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
rng = np.random.default_rng(23)
RING = 40


# ---------------------------------------------------------------- textures
def blur(a, sigma):
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    return np.real(np.fft.ifft2(np.fft.fft2(a) * np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))))


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


def iron_textures(size=1024):
    """Hand-forged iron: dark with hammer dents, rust patches and a worn lighter polish on the edges."""
    dents = norm01(blur(rng.standard_normal((size, size)), 5.0))
    fine = norm01(blur(rng.standard_normal((size, size)), 1.5))
    rust_n = norm01(blur(rng.standard_normal((size, size)), 30.0))
    rust = smooth01(rust_n, 0.52, 0.80) * (0.5 + 0.5 * fine)
    polish = smooth01(norm01(blur(rng.standard_normal((size, size)), 12.0)), 0.6, 0.9)
    base = np.array([0.075, 0.07, 0.066])
    shade = 0.7 + 0.6 * (dents - 0.5) + 0.25 * (fine - 0.5)
    albedo = base[None, None, :] * shade[..., None]
    rust_col = np.array([0.34, 0.15, 0.07])
    albedo = albedo * (1 - rust[..., None] * 0.8) + rust_col[None, None, :] * (0.6 + 0.6 * fine[..., None]) * rust[..., None] * 0.8
    albedo = albedo + polish[..., None] * np.array([0.05, 0.05, 0.05])[None, None, :]
    save_png("Iron_Albedo", albedo)
    height = dents * 1.0 + fine * 0.25 + rust * 0.4
    gy, gx = np.gradient(height)
    nx, ny, nz = -gx * 20.0, -gy * 20.0, np.ones_like(height)
    length = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    save_png("Iron_Normal", np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5)


def flame_texture(w=256, h=512):
    """A candle flame on black, for additive blending: blue base, bright white-yellow core, orange teardrop edge."""
    x = np.linspace(-1, 1, w)[None, :] * np.ones((h, 1))
    y = np.linspace(0, 1, h)[::-1][:, None] * np.ones((1, w))  # 0 at the bottom row of the image
    y = np.linspace(0, 1, h)[:, None] * np.ones((1, w))
    width = np.minimum(2.2 * np.power(y, 0.45) * np.power(np.clip(1 - y, 0, 1), 0.9), 0.95) * smooth01(y, 0.0, 0.07) + 0.0001
    # teardrop: widest low, pointed top
    d = np.abs(x) / width
    body = np.clip(1 - d ** 2, 0, 1) * (y < 0.98)
    body = body ** 0.8
    core_w = width * 0.45
    core = np.clip(1 - (np.abs(x) / (core_w + 1e-4)) ** 2, 0, 1) * smooth01(y, 0.05, 0.25) * np.clip(1 - y * 1.25, 0, 1)
    blue = np.clip(1 - (np.abs(x) / (width * 0.9 + 1e-4)) ** 2, 0, 1) * np.clip(1 - y * 6.0, 0, 1)
    orange = np.array([1.0, 0.42, 0.06])
    yellow = np.array([1.0, 0.82, 0.30])
    white = np.array([1.0, 0.95, 0.80])
    bluec = np.array([0.20, 0.35, 1.0])
    rgb = (orange[None, None, :] * (body ** 1.4)[..., None] * 0.75
           + yellow[None, None, :] * (body ** 3.0)[..., None] * 0.6
           + white[None, None, :] * (core ** 1.3)[..., None] * 0.9
           + bluec[None, None, :] * (blue ** 1.5)[..., None] * 0.55)
    # a soft flicker of the tip
    edge_soft = blur(rgb[..., 0], 2.0)
    rgb = rgb * 0.85 + edge_soft[..., None] * np.array([0.5, 0.25, 0.05])[None, None, :] * 0.4
    save_png("Flame", rgb)


# ---------------------------------------------------------------- geometry
IRON = [(0, 0.0), (0.070, 0.0), (0.076, 0.004), (0.076, 0.010), (0.070, 0.016), (0.050, 0.026), (0.030, 0.034),
        (0.016, 0.044), (0.011, 0.055), (0.011, 0.100), (0.016, 0.105), (0.022, 0.115), (0.016, 0.125), (0.011, 0.130),
        (0.011, 0.170), (0.015, 0.175), (0.019, 0.185), (0.015, 0.195), (0.011, 0.200), (0.011, 0.262),
        (0.016, 0.268), (0.026, 0.282), (0.016, 0.296), (0.011, 0.302), (0.011, 0.330), (0.014, 0.340),
        (0.022, 0.346), (0.056, 0.352), (0.059, 0.356), (0.055, 0.360), (0.030, 0.358), (0.024, 0.364),
        (0.024, 0.384), (0.0245, 0.386), (0.0200, 0.386), (0.0198, 0.384), (0.0198, 0.368), (0, 0.368)]

CANDLE_R = 0.0188
WAX = [(0, 0.372), (CANDLE_R, 0.372), (CANDLE_R, 0.45), (CANDLE_R * 0.995, 0.54), (CANDLE_R * 0.985, 0.565),
       (CANDLE_R * 0.93, 0.575), (0.0125, 0.572), (0.0045, 0.566), (0, 0.565)]
WICK = [(0, 0.563), (0.0019, 0.563), (0.0019, 0.588), (0, 0.5895)]


def lathe(bm, profile, mat_index, uv_scale, wobble=None):
    """Revolves a (radius, height) outline around Z. The outline runs bottom centre -> outside -> top -> inside, so the
    faces point outwards. UV: U goes round (uv_scale times), V follows the outline length."""
    uv = bm.loops.layers.uv.verify()
    lengths = [0.0]
    for (r0, z0), (r1, z1) in zip(profile[:-1], profile[1:]):
        lengths.append(lengths[-1] + math.hypot(r1 - r0, z1 - z0))
    rows = []
    for (r, z) in profile:
        if r <= 1e-9:
            rows.append([bm.verts.new((0, 0, z))] * RING)
            continue
        row = []
        for j in range(RING):
            th = 2 * math.pi * j / RING
            rr, zz = (wobble(r, z, th) if wobble else (r, z))
            row.append(bm.verts.new((rr * math.cos(th), rr * math.sin(th), zz)))
        rows.append(row)
    for i in range(len(profile) - 1):
        for j in range(RING):
            j2 = (j + 1) % RING
            quad = [rows[i][j], rows[i][j2], rows[i + 1][j2], rows[i + 1][j]]
            seen, verts, coords = [], [], []
            us = [j / RING * uv_scale, (j + 1) / RING * uv_scale, (j + 1) / RING * uv_scale, j / RING * uv_scale]
            vs = [lengths[i], lengths[i], lengths[i + 1], lengths[i + 1]]
            for v, u, vv in zip(quad, us, vs):
                if v in seen:
                    continue
                seen.append(v)
                verts.append(v)
                coords.append((u, vv * 6.0))
            if len(verts) < 3:
                continue
            try:
                f = bm.faces.new(verts)
            except ValueError:
                continue
            f.material_index = mat_index
            for loop, c in zip(f.loops, coords):
                loop[uv].uv = c


def wax_wobble(r, z, th):
    """A hand-dipped candle: slightly uneven, and melted lower on one side near the top."""
    r = r * (1 + 0.012 * math.sin(3 * th + 9 * z) + 0.008 * math.sin(5 * th - 4 * z))
    if z > 0.54:
        k = (z - 0.54) / 0.036
        z -= 0.006 * k * (0.5 + 0.5 * math.sin(th + 1.0))
    return r, z


def drip(bm, theta, z_top, length, radius=0.0034):
    """A thin bead of wax running down the candle."""
    uv = bm.loops.layers.uv.verify()
    ret = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=8, radius=1.0)
    verts = ret["verts"]
    cx, cy = (CANDLE_R + 0.0004) * math.cos(theta), (CANDLE_R + 0.0004) * math.sin(theta)
    for v in verts:
        local = v.co.copy()
        # stretch along Z, flatten against the candle, then place it
        x, y, z = local.x * radius * 0.7, local.y * radius, local.z * length * 0.5
        v.co.x = cx + (x * math.cos(theta) - y * math.sin(theta))
        v.co.y = cy + (x * math.sin(theta) + y * math.cos(theta))
        v.co.z = z_top - length * 0.5 + z
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = 1
        for loop in f.loops:
            loop[uv].uv = (0.0, 0.0)


# ---------------------------------------------------------------- build
iron_textures()
flame_texture()

bm = bmesh.new()
lathe(bm, IRON, 0, 4.0)
lathe(bm, WAX, 1, 1.0, wobble=wax_wobble)
lathe(bm, WICK, 2, 1.0)
for theta, z_top, length in [(0.6, 0.548, 0.07), (2.5, 0.536, 0.045), (4.1, 0.55, 0.10), (5.4, 0.545, 0.03)]:
    drip(bm, theta, z_top, length)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])

mesh = bpy.data.meshes.new("Candlestick")
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new("Candlestick", mesh)
bpy.context.scene.collection.objects.link(obj)
names = ["CandlestickIron", "CandleWax", "CandleWick"]
for name in names:
    obj.data.materials.append(bpy.data.materials.get(name) or bpy.data.materials.new(name))
mesh.shade_smooth()

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
path = os.path.join(MODEL_DIR, "Candlestick.fbx")
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
for name in names:
    m = bpy.data.materials.get(name)
    if m:
        bpy.data.materials.remove(m)
print("Exported", path)
