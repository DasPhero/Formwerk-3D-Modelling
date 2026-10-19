using Formwerk.Core.Model;

namespace Formwerk.Geometry;

/// <summary>
/// Boundary to the boolean kernel (Manifold now, OpenCascade later, see ADR 0001).
/// Implementations must not leak native types and must return watertight meshes for watertight inputs.
/// </summary>
public interface IGeometryKernel
{
    string Name { get; }

    /// <summary>Combines <paramref name="operands"/> left to right (first operand is the minuend of a difference).</summary>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    /// <exception cref="GeometryException">The operation failed (F-BOO-05); the caller keeps the last valid result.</exception>
    MeshData Boolean(BooleanOperation operation, IReadOnlyList<MeshData> operands, CancellationToken cancellationToken);
}

public sealed class GeometryException(string message, Exception? inner = null) : Exception(message, inner);
