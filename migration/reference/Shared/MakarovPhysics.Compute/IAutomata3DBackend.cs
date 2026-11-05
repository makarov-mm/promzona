namespace MakarovPhysics.Compute;

/// <summary>
/// The contract both GPU backends of the 3D cellular automata fulfil -
/// CUDA (<see cref="CudaAutomata3D"/>) and OpenCL
/// (<see cref="ClAutomata3D"/>): one rule kernel over the 26-neighbourhood
/// with survive/birth bitmasks and decaying states. Never-throw contract.
/// </summary>
public interface IAutomata3DBackend : IDisposable
{
    bool Upload(int cells, byte[]? grid);
    bool Step(int n, bool wrap, uint survive, uint birth, int maxState, int steps);
    bool Download(int cells, byte[]? grid);
}
