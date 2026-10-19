using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using ManifoldSharp;
using FwBooleanOperation = Formwerk.Core.Model.BooleanOperation;

namespace Formwerk.Kernel;

/// <summary>
/// <see cref="IGeometryKernel"/> on top of ManifoldSharp (managed port of Manifold, see ADR 0003).
/// No ManifoldSharp type leaves this class.
/// </summary>
public sealed class ManifoldKernel : IGeometryKernel
{
    /// <param name="parallel">
    /// Use all CPU cores inside one boolean (N-GPU-05). This is a process-wide switch of the library;
    /// results are bit-identical to the sequential path.
    /// </param>
    public ManifoldKernel(bool parallel = true)
    {
        ManifoldParallel.Enabled = parallel;
    }

    public string Name => "Manifold (managed)";

    public MeshData Boolean(
        FwBooleanOperation operation, IReadOnlyList<MeshData> operands, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operands);
        cancellationToken.ThrowIfCancellationRequested();

        if (operands.Count == 0) return MeshData.Empty;
        if (operands.Count == 1) return operands[0];

        var token = new CancelToken(cancellationToken);
        try
        {
            var manifolds = new List<Manifold>(operands.Count);
            for (var i = 0; i < operands.Count; i++) manifolds.Add(ToManifold(operands[i], i));

            var result = operation == FwBooleanOperation.Union
                ? Manifold.BatchBoolean(manifolds, OpType.Add)
                : Fold(manifolds, operation == FwBooleanOperation.Difference ? OpType.Subtract : OpType.Intersect, token);

            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status() != ManifoldSharp.Error.NoError)
                throw new GeometryException($"The operation failed ({result.Status().ToStr()}). Try repairing the objects involved.");

            return ToMeshData(result);
        }
        catch (GeometryException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new GeometryException("The operation could not be completed. Try repairing the objects involved.", ex);
        }
    }

    private static Manifold Fold(List<Manifold> operands, OpType op, CancelToken token)
    {
        var result = operands[0];
        for (var i = 1; i < operands.Count; i++)
        {
            result = result.BooleanWithToken(operands[i], op, token);
            if (token.IsCancelled) throw new OperationCanceledException();
        }

        return result;
    }

    private static Manifold ToManifold(MeshData mesh, int operandIndex)
    {
        var raw = new MeshGL { NumProp = 3 };
        var vertices = mesh.Vertices;
        raw.VertProperties.Capacity = vertices.Length * 3;
        foreach (var v in vertices)
        {
            raw.VertProperties.Add(v.X);
            raw.VertProperties.Add(v.Y);
            raw.VertProperties.Add(v.Z);
        }

        var indices = mesh.Indices;
        raw.TriVerts.Capacity = indices.Length;
        foreach (var i in indices) raw.TriVerts.Add((uint)i);

        var manifold = Manifold.FromMeshGL(raw);
        if (manifold.Status() != ManifoldSharp.Error.NoError)
            throw new GeometryException(
                $"Object {operandIndex + 1} is not a closed, watertight solid ({manifold.Status().ToStr()}). Try repairing it first.");
        return manifold;
    }

    private static MeshData ToMeshData(Manifold manifold)
    {
        var mesh = manifold.GetMeshGL(-1);
        var stride = (int)mesh.NumProp;
        var vertexCount = mesh.VertProperties.Count / stride;
        var vertices = new Vector3[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var o = i * stride;
            vertices[i] = new Vector3(mesh.VertProperties[o], mesh.VertProperties[o + 1], mesh.VertProperties[o + 2]);
        }

        var indices = new int[mesh.TriVerts.Count];
        for (var i = 0; i < indices.Length; i++) indices[i] = (int)mesh.TriVerts[i];
        return new MeshData(vertices, indices);
    }
}
