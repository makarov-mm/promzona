using System.Runtime.CompilerServices;

namespace MakarovPhysics.Renderer;

/// <summary>Sixteen floats stored inline, without a heap array.</summary>
[InlineArray(16)]
public struct Mat4Elements
{
    private float _e0;
}
