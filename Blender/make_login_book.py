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


def smooth01(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def scratches(size, count, length=(40, 320)):
    layer = np.zeros((size, size))
    for _ in range(count):
        x0, y0 = rng.random(2) * size
        ang = rng.random() * math.pi
        steps = int(rng.integers(*length))
        t = np.arange(steps)
        xs = (x0 + np.cos(ang) * t).astype(int) % size
        ys = (y0 + np.sin(ang) * t + np.sin(t * 0.05) * 3).astype(int) % size
        layer[ys, xs] = 0.4 + 0.6 * rng.random()
    return np.clip(blur(layer, 0.8) * 5.0, 0, 1)


def leather_textures(size=1024):
    """Old, rubbed leather: dark brown-red, pebbled grain, lighter worn patches, cracks and scratches."""
    n1 = norm01(blur(rng.standard_normal((size, size)), 1.2))
    n2 = norm01(blur(rng.standard_normal((size, size)), 4.0))
    grain = np.abs(blur(rng.standard_normal((size, size)), 2.2))
    height = norm01(norm01(n1 * 0.7 + n2 * 0.3) + 0.8 * norm01(grain))

    big = norm01(blur(rng.standard_normal((size, size)), 22.0))
    worn = smooth01(big, 0.50, 0.78)  # rubbed patches
    crack_noise = norm01(blur(rng.standard_normal((size, size)), 3.5))
    cracks = smooth01(1.0 - np.abs(crack_noise * 2 - 1), 0.955, 0.995) * (0.35 + 0.65 * worn)
    scr = scratches(size, 90)

    detail = height * 0.8 - cracks * 1.0 - scr * 0.35 - worn * 0.15
    gy, gx = np.gradient(detail)
    strength = 16.0
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(detail)
    length = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    normal = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5

    base = np.array([0.20, 0.075, 0.055])
    rubbed = np.array([0.42, 0.28, 0.19])
    shade = 0.80 + 0.40 * (height - 0.5) + (n2 - 0.5) * 0.30
    albedo = base[None, None, :] * shade[..., None]
    albedo = albedo * (1 - worn[..., None] * 0.65) + rubbed[None, None, :] * shade[..., None] * worn[..., None] * 0.65
    albedo = albedo + scr[..., None] * np.array([0.20, 0.15, 0.11])[None, None, :]
    albedo = albedo * (1 - cracks[..., None] * 0.75)
    save_png("LoginBook_Leather_Albedo", albedo)
    save_png("LoginBook_Leather_Normal", normal)


def paper_texture(h=1024, w=2048):
    """Yellowed old paper across the whole spread (left page u 0..0.5, right page u 0.5..1): stains, foxing, dark edges."""
    fine = norm01(blur(rng.standard_normal((h, w)), 0.8))
    fiber = norm01(blur(rng.standard_normal((h, w)), 6.0))
    stain_n = norm01(blur(rng.standard_normal((h, w)), 28.0))
    stain = smooth01(stain_n, 0.48, 0.95)
    v = 0.88 + 0.06 * fine + 0.07 * (fiber - 0.5)
    base = np.array([0.80, 0.70, 0.54])
    albedo = base[None, None, :] * v[..., None]
    albedo = albedo * (1 - 0.17 * stain[..., None] * np.array([0.6, 0.85, 1.0])[None, None, :])

    yy, xx = np.mgrid[0:h, 0:w]
    for _ in range(170):  # foxing spots
        cx, cy = rng.random() * w, rng.random() * h
        r = 2 + rng.random() * 7
        x0, x1 = int(max(cx - 3 * r, 0)), int(min(cx + 3 * r, w))
        y0, y1 = int(max(cy - 3 * r, 0)), int(min(cy + 3 * r, h))
        if x1 <= x0 or y1 <= y0:
            continue
        d2 = (xx[y0:y1, x0:x1] - cx) ** 2 + (yy[y0:y1, x0:x1] - cy) ** 2
        a = np.exp(-d2 / (2 * (r * 0.6) ** 2)) * (0.25 + 0.45 * rng.random())
        albedo[y0:y1, x0:x1] = albedo[y0:y1, x0:x1] * (1 - a[..., None]) + np.array([0.42, 0.24, 0.12]) * a[..., None]

    u = np.linspace(0, 1, w)[None, :]
    vv = np.linspace(0, 1, h)[:, None]
    page_u = (u * 2) % 1.0
    left = u < 0.5
    d_outer = np.where(left, page_u, 1 - page_u)
    d_spine = 1 - d_outer
    d_vert = np.minimum(vv, 1 - vv)
    dark = (np.exp(-d_outer / 0.035) * 0.40 + np.exp(-d_spine / 0.09) * 0.45 + np.exp(-d_vert / 0.03) * 0.35)
    dark = np.clip(dark * (0.75 + 0.5 * stain_n), 0, 0.8)
    albedo = albedo * (1 - dark[..., None] * np.array([0.8, 0.9, 1.0])[None, None, :])
    save_png("LoginBook_Paper_Albedo", albedo)


def page_edge_texture(w=64, h=1024):
    """Browned, grubby sheet edges."""
    v = np.zeros(h)
    i = 0
    while i < h:
        step = int(rng.integers(3, 6))
        v[i:i + step] = 0.70 + 0.16 * rng.random()
        v[i:i + 1] = 0.40 + 0.12 * rng.random()  # the thin shadow between two sheets
        i += step
    grime = norm01(blur(rng.standard_normal((h, w)), 5.0))
    shade = np.repeat(v[:, None], w, axis=1) * (0.75 + 0.35 * grime)
    albedo = np.stack([shade * 0.88, shade * 0.70, shade * 0.46], axis=-1)
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


def top_z(x, y):
    """Page top with the age of the book: soft waves in the paper and drooping outer corners."""
    ax = abs(x)
    z = top_height(ax)
    z += 0.0007 * math.sin(x * 37 + y * 23) + 0.0005 * math.sin(x * 91 - y * 57 + 1.3) + 0.0004 * math.sin(y * 71 + x * 13)
    z -= 0.0035 * smoothstep(0.23, PAGE_X1, ax) * smoothstep(0.13, PAGE_H / 2, abs(y))
    return z


def page_block(bm, side, nx=40, ny=12):
    top, bot = {}, {}
    y0 = -PAGE_H / 2
    for i in range(nx + 1):
        ax = PAGE_X0 + (PAGE_X1 - PAGE_X0) * i / nx
        x = side * ax
        for j in range(ny + 1):
            y = y0 + PAGE_H * j / ny
            top[i, j] = bm.verts.new((x, y, top_z(x, y)))
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
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.0026, segments=3, affect="EDGES")


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
                loop[uv].uv = ((x + PAGE_X1) / (2 * PAGE_X1), (y + PAGE_H / 2) / PAGE_H)
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
