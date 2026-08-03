#!/usr/bin/env python
"""Convert a VTK PolyData (.vtp) file to Wavefront OBJ (.obj), including normals.

Usage:
    python vtp_to_obj.py input.vtp output.obj
"""

import argparse

import numpy as np
import vtk
from vtk.util.numpy_support import vtk_to_numpy


def convert(input_path: str, output_path: str):
    reader = vtk.vtkXMLPolyDataReader()
    reader.SetFileName(input_path)
    reader.Update()

    polydata = reader.GetOutput()

    # Merge coincident points (common in meshes assembled from many
    # independent unstructured cells that were never welded) and drop
    # degenerate cells. Without this, geometrically-identical triangles can
    # end up at different vertex indices, which reads as duplicate/
    # overlapping geometry and causes z-fighting.
    clean_filter = vtk.vtkCleanPolyData()
    clean_filter.SetInputData(polydata)
    clean_filter.Update()
    polydata = clean_filter.GetOutput()

    # Triangulate so every cell in Polys is a plain 3-vertex triangle,
    # collapsing triangle strips/quads/polygons into that one shape.
    triangle_filter = vtk.vtkTriangleFilter()
    triangle_filter.SetInputData(polydata)
    triangle_filter.Update()
    polydata = triangle_filter.GetOutput()

    # Always recompute normals fresh from the cleaned/welded topology, rather
    # than trusting whatever normals existed before cleaning. Coincident
    # points merged by vtkCleanPolyData above may have carried different
    # normals each (from separate unwelded source cells); merging doesn't
    # properly re-average/renormalize those, producing an inconsistent,
    # noisy-looking normal per merged point.
    normals_filter = vtk.vtkPolyDataNormals()
    normals_filter.SetInputData(polydata)
    normals_filter.ComputePointNormalsOn()
    normals_filter.ComputeCellNormalsOff()
    normals_filter.SplittingOff()
    normals_filter.ConsistencyOn()
    normals_filter.AutoOrientNormalsOn()
    normals_filter.Update()
    polydata = normals_filter.GetOutput()

    points = vtk_to_numpy(polydata.GetPoints().GetData())
    normals = vtk_to_numpy(polydata.GetPointData().GetNormals())

    # Pull triangle connectivity out as a flat index array and reshape to
    # (n_triangles, 3); safe because vtkTriangleFilter guarantees 3 points
    # per cell.
    polys = polydata.GetPolys()
    conn = vtk_to_numpy(polys.GetConnectivityArray())
    faces = conn.reshape(-1, 3) + 1  # OBJ indices are 1-based

    print(f"Writing {len(points)} vertices, {len(faces)} faces to {output_path}")

    with open(output_path, "w") as f:
        for x, y, z in points:
            f.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")
        for x, y, z in normals:
            f.write(f"vn {x:.6f} {y:.6f} {z:.6f}\n")
        for a, b, c in faces:
            f.write(f"f {a}//{a} {b}//{b} {c}//{c}\n")

    print(f"Wrote {output_path}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", help="Input .vtp file")
    parser.add_argument("output", help="Output .obj file")
    args = parser.parse_args()

    convert(args.input, args.output)


if __name__ == "__main__":
    main()
