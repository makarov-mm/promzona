using MakarovPhysics.Compute;
namespace MakarovPhysics.UI;
/// <summary>Compact particle count/device controls. The status names the device
/// which completed the calculation, including an explicit CPU recovery.</summary>
public sealed class ParticleSettings
{
    private readonly Func<string,string> _tr;
    private readonly Func<int> _count;
    private readonly Func<ComputeBackend> _resolved;
    private readonly Func<double> _milliseconds;
    private readonly Func<bool> _failed;
    private readonly UiLabel _status;
    private readonly Dropdown _pick;
    private readonly RadioGroup _device;
    private readonly int[] _counts;
    private readonly Func<ComputeBackend> _requested;
    private int _listedCount = -1;
    public ParticleSettings(UiPanel panel,Func<string,string> tr,int[] counts,Func<int> count,Action<int> setCount,
        Func<ComputeBackend> requested,Action<ComputeBackend> setBackend,Func<ComputeBackend> resolved,
        Func<double> milliseconds,Func<bool> failed,bool coupled=false)
    {
        _counts=counts;_requested=requested;_tr=tr;_count=count;_resolved=resolved;_milliseconds=milliseconds;_failed=failed;
        _pick=panel.Add(new Dropdown(tr("ParticleCompute.Count"),counts.Select(n=>n.ToString("N0")).ToArray(),Math.Max(0,Array.IndexOf(counts,count()))));
        _pick.Changed+=i=>{if(i>=0 && i<counts.Length)setCount(counts[i]);};
        _device=ComputeBackendSelector.Add(panel,tr("ParticleCompute.Backend"),tr("ParticleCompute.Auto"),requested(),setBackend,true);
        _device.Advanced=false;
        _status=panel.Add(new UiLabel(""){Wrap=true});
        panel.Add(new UiLabel(tr(coupled?"ParticleCompute.Interacting":"ParticleCompute.Tracers")){Wrap=true,Dim=true});
        Update();
    }
    public void Update()
    {
        int count=_count();
        if(count!=_listedCount)
        {
            int selected=Array.IndexOf(_counts,count);
            _pick.Items=selected>=0 ? _counts.Select(n=>n.ToString("N0")).ToArray()
                : _counts.Select(n=>n.ToString("N0")).Append(count.ToString("N0")).ToArray();
            _pick.Selected=selected>=0 ? selected : _counts.Length;
            _listedCount=count;
        }
        _device.Selected=(int)_requested();
        _device.Items[0]=ComputeBackendSelector.AutoLabel(_tr("ParticleCompute.Auto"),_resolved());
        _status.Text=ComputeBackends.Describe(_resolved())+"\n"+string.Format(_tr("ParticleCompute.Timing"),_count(),_milliseconds())
            +(_failed()?"\n"+_tr("ParticleCompute.Recovery"):"");
    }
}
