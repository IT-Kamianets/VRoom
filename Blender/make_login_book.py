"""Builds the login book for VRoom and exports it to Unity.

Run it inside Blender (Scripting tab, or `exec(open(path).read())`). It makes an open hardcover book lying
flat (spine along Y, pages left and right, top is +Z), with leather cover, curved page stacks with sheet edges,
and a ribbon bookmark. It writes Assets/VRoom/Models/LoginBook.fbx and the textures to Assets/VRoom/Textures/,
then removes the temporary objects from the open file.

Page size: 0.28 x 0.38 m; the flat reading area of each page is about 0.24 x 0.34 m, centred at x = +-0.16.
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

COVER_W, COVER_H, COVER_T = 0.60, 0.40, 0.006
PAGE_X0, PAGE_X1, PAGE_H = 0.002, 0.284, 0.38
STACK = 0.030
rng = np.random.default_rng(7)


# ---------------------------------------------------------------- textures
def blur(a, sigma):
    """Gaussian blur through the FFT (wraps around, so the result tiles)."""
    h, w = a.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    k = np.exp(-2 * (np.pi * sigma) ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * k))


def norm01(a):
    return (a - a.min()) / (a.max() - a.min() + 1e-9)


def save_png(name, rgb, alpha=None):
    h, w, _ = rgb.shape
    img = bpy.data.images.new(name, w, h, alpha=False)
    out = np.ones((h, w, 4), dtype=np.float32)
    out[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = os.path.join(TEXTURE_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def leather_textures(size=1024):
    n1 = norm01(blur(rng.standard_normal((size, size)), 1.2))
    n2 = norm01(blur(rng.standard_normal((size, size)), 4.0))
    # pebbled grain: cells of a fine noise, plus soft large-scale wear
    height = norm01(n1 * 0.7 + n2 * 0.3)
    grain = np.abs(blur(rng.standard_normal((size, size)), 2.2))
    height = norm01(height + 0.8 * norm01(grain))
    gy, gx = np.gradient(height)
    strength = 14.0
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(height)
    length = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    normal = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5
    base = np.array([0.34, 0.045, 0.06])
    wear = (n2 - 0.5) * 0.35
    shade = 0.78 + 0.45 * (height - 0.5) + wear
    albedo = base[None, None, :] * shade[..., None]
    save_png("LoginBook_Leather_Albedo", albedo)
    save_png("LoginBook_Leather_Normal", normal)


def paper_texture(size=512):
    fine = blur(rng.standard_normal((size, size)), 0.8)
    fiber = blur(rng.standard_normal((size, size)), 6.0)
    v = 0.965 + 0.018 * norm01(fine) - 0.010 + 0.020 * (norm01(fiber) - 0.5)
    albedo = np.stack([v * 0.99, v * 0.955, v * 0.86], axis=-1)
    save_png("LoginBook_Paper_Albedo", albedo)


def page_edge_texture(w=64, h=1024):
    v = np.zeros(h)
    i = 0
    while i < h:
        step = int(rng.integers(3, 6))
        v[i:i + step] = 0.80 + 0.14 * rng.random()
        v[i:i + 1] = 0.55 + 0.10 * rng.random()  # the thin shadow between two sheets
        i += step
    shade = np.repeat(v[:, None], w, axis=1)
    albedo = np.stack([shade * 0.97, shade * 0.93, shade * 0.82], axis=-1)
    save_png("LoginBook_PageEdge_Albedo", albedo)


# ---------------------------------------------------------------- geometry
def smoothstep(a, b, x):
    t = min(max((x - a) / (b - a), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def top_height(ax):
    """Height of the page stack top at distance ax from the spine: a gutter dip, a gentle droop at the outer edge."""
    z = COVER_T + STACK
    z -= 0.011 * math.exp(-(ax / 0.022) ** 2)
    z -= 0.002 * smoothstep(0.25, PAGE_X1, ax)
    return z


def page_block(bm, side, nx=40, ny=2):
    top, bot = {}, {}
    y0 = -PAGE_H / 2
    for i in range(nx + 1):
        ax = PAGE_X0 + (PAGE_X1 - PAGE_X0) * i / nx
        x = side * ax
        for j in range(ny + 1):
            y = y0 + PAGE_H * j / ny
            top[i, j] = bm.verts.new((x, y, top_height(ax)))
            bot[i, j] = bm.verts.new((x, y, COVER_T))

    def quad(a, b, c, d, mat):
        f = bm.faces.new((a, b, c, d))
        f.material_index = mat
        return f

    for i in range(nx):
        for j in range(ny):
            quad(top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1], 1)  # paper
    for i in range(nx):
        quad(top[i, 0], bot[i, 0], bot[i + 1, 0], top[i + 1, 0], 2)  # bottom edge (front)
        quad(top[i, ny], top[i + 1, ny], bot[i + 1, ny], bot[i, ny], 2)  # top edge (back)
    for j in range(ny):
        quad(top[0, j], top[0, j + 1], bot[0, j + 1], bot[0, j], 2)  # spine side
        quad(top[nx, j], bot[nx, j], bot[nx, j + 1], top[nx, j + 1], 2)  # outer edge


def cover(bm):
    hx, hy = COVER_W / 2, COVER_H / 2
    v = [bm.verts.new(p) for p in [(-hx, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0),
                                   (-hx, -hy, COVER_T), (hx, -hy, COVER_T), (hx, hy, COVER_T), (-hx, hy, COVER_T)]]
    for idx in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        bm.faces.new([v[i] for i in idx]).material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.0016, segments=3, affect="EDGES")


def ribbon(bm):
    """A red bookmark lying in the gutter, over the bottom edge of the book and hanging down."""
    w = 0.009
    pts = []
    n = 14
    for i in range(n + 1):
        t = i / n
        y = -0.15 - 0.19 * t
        if y > -PAGE_H / 2 + 0.0:
            z = top_height(0.0045) + 0.0007
        else:
            drop = (-PAGE_H / 2 - y)
            z = max(top_height(0.0045) - drop * 1.6, -0.12) + 0.0007
            if drop < 0.012:
                z = top_height(0.0045) - drop * 0.4 + 0.0007
        pts.append((y, z))
    rows = []
    for (y, z) in pts:
        rows.append((bm.verts.new((-w / 2, y, z)), bm.verts.new((w / 2, y, z))))
    for a, b in zip(rows[:-1], rows[1:]):
        f = bm.faces.new((a[0], b[0], b[1], a[1]))
        f.material_index = 3


def assign_uvs(bm):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        for loop in f.loops:
            x, y, z = loop.vert.co
            m = f.material_index
            if m == 1:  # paper top
                loop[uv].uv = (x * 3.4, y * 2.6)
            elif m == 2:  # page edge: stripes follow the height
                s = (y if abs(n.x) > abs(n.y) else x)
                loop[uv].uv = (s * 8.0, (z - COVER_T) / STACK)
            elif m == 3:  # ribbon
                loop[uv].uv = (x * 40.0, y)
            else:  # leather
                if abs(n.z) > 0.5:
                    loop[uv].uv = (x * 5.0, y * 5.0)
                elif abs(n.x) > abs(n.y):
                    loop[uv].uv = (y * 5.0, z * 5.0)
                else:
                    loop[uv].uv = (x * 5.0, z * 5.0)


# ---------------------------------------------------------------- build
leather_textures()
paper_texture()
page_edge_texture()

bm = bmesh.new()
cover(bm)
page_block(bm, +1)
page_block(bm, -1)
ribbon(bm)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
assign_uvs(bm)

mesh = bpy.data.meshes.new("LoginBook")
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new("LoginBook", mesh)
bpy.context.scene.collection.objects.link(obj)
mesh.shade_smooth()

names = ["BookCover", "BookPaper", "BookPageEdge", "BookRibbon"]
for name in names:
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    obj.data.materials.append(m)

# smooth only where it helps: keep the page stack edges and paper crisp
for poly in mesh.polygons:
    poly.use_smooth = poly.material_index in (0, 1, 3)

# export
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
path = os.path.join(MODEL_DIR, "LoginBook.fbx")
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

# clean the open file
bpy.data.objects.remove(obj, do_unlink=True)
bpy.data.meshes.remove(mesh)
for name in names:
    m = bpy.data.materials.get(name)
    if m:
        bpy.data.materials.remove(m)
print("Exported", path)
