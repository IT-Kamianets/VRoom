"""Builds the wooden book stand (a small lectern) for VRoom and exports it to Unity.

Run it inside Blender (Scripting tab, or `exec(open(path).read())`). The stand rests on the table (z = 0), 0.84 m wide
and 0.50 m deep, with the reading board tilted by 20 degrees: low at the +Y edge (the one facing the reader, which is -Z in
Unity), high at -Y, a front ledge holding the book, two wedge-shaped sides and a back brace. It writes
Assets/VRoom/Models/BookStand.fbx and uses the OldWood textures of the table (written by make_old_table.py).
The point of the board's top surface at its centre is reported at the end, for placing the book.
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = r"D:\work\VRoom"
MODEL_DIR = os.path.join(PROJECT, "Assets", "VRoom", "Models")
os.makedirs(MODEL_DIR, exist_ok=True)

W, L, T = 0.84, 0.50, 0.025  # board width (X), length (Y, along the slope), thickness
ANGLE = math.radians(20)
SIDE_T = 0.025
LEDGE_H, LEDGE_D = 0.030, 0.032
CLEAR = 0.012  # the low end of the board hangs this far above the table

# centre of the board so that its lowest edge is CLEAR above the table
CENTER_Z = CLEAR + (L / 2) * math.sin(ANGLE) + (T / 2) * math.cos(ANGLE)
TOP_CENTER_Z = CENTER_Z + (T / 2) * math.cos(ANGLE)


def box(bm, size, bevel=0.003, grain_x=True):
    """A box centred at the origin with wood UVs (grain along U): along X for boards, along Y for the sides."""
    sx, sy, sz = size
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    faces_before = set(bm.faces)
    v = [bm.verts.new((dx * hx, dy * hy, dz * hz))
         for dx, dy, dz in [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                            (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]]
    faces = [bm.faces.new([v[i] for i in idx])
             for idx in [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]]
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    edges = {e for f in faces for e in f.edges}
    bmesh.ops.bevel(bm, geom=list(edges), offset=bevel, segments=2, affect="EDGES")
    all_faces = [f for f in bm.faces if f not in faces_before]
    uv = bm.loops.layers.uv.verify()
    off = (len(bm.faces) % 7) / 7.0
    for f in all_faces:
        n = f.normal
        for loop in f.loops:
            x, y, z = loop.vert.co
            if grain_x:
                if abs(n.z) > 0.5:
                    loop[uv].uv = (x, y + off)
                elif abs(n.x) > abs(n.y):
                    loop[uv].uv = (y, z + off)
                else:
                    loop[uv].uv = (x, z + off)
            else:
                if abs(n.z) > 0.5:
                    loop[uv].uv = (y, x + off)
                elif abs(n.x) > abs(n.y):
                    loop[uv].uv = (y, z + off)
                else:
                    loop[uv].uv = (z, x + off)
    return {x for f in all_faces for x in f.verts}


def transform(verts, matrix):
    for v in verts:
        v.co = matrix @ v.co


def wedge(bm, x_center):
    """A side support: a trapezoid in Y/Z (flat on the table, rising to the underside of the board), extruded along X."""
    h_near = CENTER_Z - (L / 2) * math.sin(ANGLE) - (T / 2) * math.cos(ANGLE) + 0.004   # underside of the board, low end
    h_far = CENTER_Z + (L / 2) * math.sin(ANGLE) - (T / 2) * math.cos(ANGLE) + 0.004    # underside, high end
    y_near, y_far = L / 2 * math.cos(ANGLE) - 0.01, -L / 2 * math.cos(ANGLE) + 0.01
    profile = [(y_near, 0.0), (y_far, 0.0), (y_far, h_far), (y_near, h_near)]
    x0, x1 = x_center - SIDE_T / 2, x_center + SIDE_T / 2
    left = [bm.verts.new((x0, y, z)) for y, z in profile]
    right = [bm.verts.new((x1, y, z)) for y, z in profile]
    faces = [bm.faces.new(left[::-1]), bm.faces.new(right)]
    for i in range(4):
        j = (i + 1) % 4
        faces.append(bm.faces.new([left[i], left[j], right[j], right[i]]))
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    uv = bm.loops.layers.uv.verify()
    for f in faces:
        n = f.normal
        for loop in f.loops:
            x, y, z = loop.vert.co
            loop[uv].uv = ((y, z) if abs(n.x) > 0.5 else ((y, x) if abs(n.z) > 0.5 else (z, x)))
    return left + right


bm = bmesh.new()

# the reading board, tilted: high edge at -Y
rot = Matrix.Rotation(-ANGLE, 4, "X")
board = box(bm, (W, L, T), bevel=0.003)
transform(board, Matrix.Translation((0, 0, CENTER_Z)) @ rot)

# ledge along the low edge, lying on the board
ledge = box(bm, (W, LEDGE_D, LEDGE_H), bevel=0.003)
transform(ledge, Matrix.Translation((0, 0, CENTER_Z)) @ rot @ Matrix.Translation((0, L / 2 - LEDGE_D / 2, T / 2 + LEDGE_H / 2 - 0.001)))

# the wedge sides
for sx in (-1, 1):
    wedge(bm, sx * (W / 2 - 0.06))

# back brace between the sides, near the high end
brace = box(bm, (W - 0.13, 0.04, 0.07), bevel=0.003)
transform(brace, Matrix.Translation((0, -L / 2 * math.cos(ANGLE) + 0.06, 0.10)))

bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
mesh = bpy.data.meshes.new("BookStand")
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new("BookStand", mesh)
bpy.context.scene.collection.objects.link(obj)
mat = bpy.data.materials.get("OldWood") or bpy.data.materials.new("OldWood")
obj.data.materials.append(mat)
for poly in mesh.polygons:
    poly.use_smooth = False

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
path = os.path.join(MODEL_DIR, "BookStand.fbx")
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
print("Exported", path, "| top surface at the board centre: z =", round(TOP_CENTER_Z, 4), "| tilt", round(math.degrees(ANGLE)), "deg")
