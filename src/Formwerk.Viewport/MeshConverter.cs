using System.Numerics;
using System.Windows.Media.Media3D;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>Converts <see cref="MeshData"/> to WPF 3D geometry with crease-angle shading.</summary>
public static class MeshConverter
{
    /// <summary>Corners whose normals point within about 1.8 degrees of each other share a vertex.</summary>
    private const float SameNormal = 0.9995f;

    /// <summary>
    /// A vertex normal averages the neighbouring faces whose angle to the triangle is below <paramref name="creaseAngleDegrees"/>, so
    /// curved surfaces look smooth and edges (box corners) stay sharp (F-VIS-02, 30° default). Corners of one source vertex whose
    /// normals agree share one output vertex: a smooth surface needs about one vertex per source vertex instead of three per triangle
    /// (a fraction of the memory, ADR 0005), a box corner still gets one vertex per face.
    /// </summary>
    public static MeshGeometry3D ToGeometry(MeshData mesh, double creaseAngleDegrees = 30)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        var triangleCount = mesh.TriangleCount;

        var faceNormals = new Vector3[triangleCount];
        var faceWeights = new float[triangleCount];
        for (var t = 0; t < triangleCount; t++)
        {
            var cross = Vector3.Cross(
                vertices[indices[t * 3 + 1]] - vertices[indices[t * 3]],
                vertices[indices[t * 3 + 2]] - vertices[indices[t * 3]]);
            var length = cross.Length();
            faceWeights[t] = length;
            faceNormals[t] = length > 1e-12f ? cross / length : Vector3.UnitZ;
        }

        // Faces around each vertex (counting sort).
        var start = new int[vertices.Length + 1];
        foreach (var i in indices) start[i + 1]++;
        for (var i = 0; i < vertices.Length; i++) start[i + 1] += start[i];
        var cursor = (int[])start.Clone();
        var around = new int[indices.Length];
        for (var t = 0; t < triangleCount; t++)
        {
            for (var c = 0; c < 3; c++) around[cursor[indices[t * 3 + c]]++] = t;
        }

        var cosCrease = (float)Math.Cos(creaseAngleDegrees * Math.PI / 180);
        var positions = new Point3DCollection(vertices.Length);
        var normals = new Vector3DCollection(vertices.Length);
        var triangleIndices = new System.Windows.Media.Int32Collection(triangleCount * 3);

        // Per source vertex a chain of the output vertices made for it so far, each with its normal.
        var head = new int[vertices.Length];
        Array.Fill(head, -1);
        var entryNormal = new Vector3[indices.Length];
        var entryOutput = new int[indices.Length];
        var entryNext = new int[indices.Length];
        var entries = 0;

        for (var t = 0; t < triangleCount; t++)
        {
            for (var c = 0; c < 3; c++)
            {
                var v = indices[t * 3 + c];
                var sum = Vector3.Zero;
                for (var k = start[v]; k < start[v + 1]; k++)
                {
                    var f = around[k];
                    if (Vector3.Dot(faceNormals[f], faceNormals[t]) >= cosCrease) sum += faceNormals[f] * faceWeights[f];
                }

                var n = sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : faceNormals[t];

                var output = -1;
                for (var e = head[v]; e >= 0; e = entryNext[e])
                {
                    if (Vector3.Dot(entryNormal[e], n) <= SameNormal) continue;
                    output = entryOutput[e];
                    break;
                }

                if (output < 0)
                {
                    var p = vertices[v];
                    output = positions.Count;
                    positions.Add(new Point3D(p.X, p.Y, p.Z));
                    normals.Add(new Vector3D(n.X, n.Y, n.Z));
                    entryNormal[entries] = n;
                    entryOutput[entries] = output;
                    entryNext[entries] = head[v];
                    head[v] = entries++;
                }

                triangleIndices.Add(output);
            }
        }

        var geometry = new MeshGeometry3D
        {
            Positions = positions,
            Normals = normals,
            TriangleIndices = triangleIndices,
        };
        geometry.Freeze();
        return geometry;
    }
}
