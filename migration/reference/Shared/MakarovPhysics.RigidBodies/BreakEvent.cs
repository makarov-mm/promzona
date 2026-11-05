using System.Numerics;
using MakarovPhysics.RigidBodies.Materials;

namespace MakarovPhysics.RigidBodies;

public readonly record struct BreakEvent(
    Vector3 Position,
    Vector3 Normal,
    float Speed,
    MaterialId MaterialId,
    Vector3 Color,
    int Pieces);
