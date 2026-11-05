using MakarovPhysics.Compute;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
namespace MakarovPhysics.Experiments;

/// <summary>Opt-in, bounded compute batches for scenes with independent tracers.
/// Particle count changes sample density; the analytic model remains unchanged.</summary>
public abstract class ParticleCosmosModule : CosmosModule
{
    protected readonly AstroParticles Particles = new();
    protected Vec3[] ParticleColors = [];
    protected abstract int DefaultParticleCount { get; }
    protected virtual int[] ParticleCounts => new[]{DefaultParticleCount,100000,250000,500000,1000000}.Distinct().Order().ToArray();
    private int _particleCount;
    private ParticleSettings? _particleSettings;
    protected int ParticleCount => _particleCount==0?DefaultParticleCount:_particleCount;
    protected override void ComputationControls(UiPanel panel)
    {
        _particleSettings=new(panel,Tr,ParticleCounts,()=>ParticleCount,n=>_particleCount=n,
            ()=>Particles.Backend,b=>Particles.Backend=b,()=>Particles.ResolvedBackend,
            ()=>Particles.Milliseconds,()=>Particles.DeviceFailed);
    }
    protected override void OnUiFrame() {base.OnUiFrame();_particleSettings?.Update();}
    protected void SeedParticles(Action<int,float[]> seed)
    {
        base.Scene.FloatingEmission=true;
        if(Particles.Count==ParticleCount)return;
        Particles.Resize(ParticleCount);ParticleColors=new Vec3[ParticleCount];
        for(int i=0;i<ParticleCount;i++)seed(i,Particles.Input);
    }
    protected Vec3 ParticlePosition(int i)
    {int k=i*AstroParticles.Stride;return new(Particles.Output[k],Particles.Output[k+1],Particles.Output[k+2]);}
    // Density-normalized light: a denser sample must not turn the same nebula white.
    protected float ParticleLight => (float)DefaultParticleCount/ParticleCount;
    public override void Dispose() {Particles.Dispose();base.Dispose();}
}
