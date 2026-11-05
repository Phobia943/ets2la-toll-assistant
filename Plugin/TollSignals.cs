using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using TruckLib.Models.Ppd;

namespace MautAssistent;

internal static class TollSignals
{
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;

    internal static bool SameBooth(NavCurve a,NavCurve b)
    {
        var da=a.EndPosition-a.StartPosition;var db=b.EndPosition-b.StartPosition;
        return da.LengthSquared()>0 && db.LengthSquared()>0 &&
            Vector3.Dot(Vector3.Normalize(da),Vector3.Normalize(db))>.95 &&
            Vector3.Distance(a.StartPosition,b.StartPosition)<=1 && Vector3.Distance(a.EndPosition,b.EndPosition)<=1;
    }

    internal static IEnumerable<ParsedSemaphore> ForPath(PrefabPath input)
    {
        var path=PrefabRefinement.OriginalFor(input);
        var descriptor=path.Prefab.Descriptor;
        if(descriptor==null)yield break;
        foreach(var curve in path.Curves.Where(c=>c.SemaphoreId>=0))
        {
            int count=Math.Clamp((int)Math.Ceiling(curve.Length*2),4,600);
            var points=Enumerable.Range(0,count+1).Select(i=>PrefabUtils.InterpolateNavCurveOriented(curve,(float)i/count).Position).ToArray();
            var candidates=descriptor.Semaphores.Where(s=>s.SemaphoreId==(uint)curve.SemaphoreId)
                .Select(s=>(Signal:s,Distance:points.Min(p=>Vector3.Distance(p,s.Position))))
                .Where(s=>s.Distance<=4).OrderBy(s=>s.Distance).ToArray();
            if(candidates.Length==0)continue;
            var chosen=candidates[0];
            if(candidates.Any(s=>s.Distance-chosen.Distance<.25 && Vector3.Distance(s.Signal.Position,chosen.Signal.Position)>1))continue;
            // Detached measurement containers preserve the native transform.
            // Neither the game descriptor nor the host steering path is changed.
            var localDescriptor=(PrefabDescriptor)clone.Invoke(descriptor,null)!;
            localDescriptor.Semaphores=[chosen.Signal];
            var localPrefab=(ParsedPrefab)clone.Invoke(path.Prefab,null)!;localPrefab.Descriptor=localDescriptor;
            var localPath=(PrefabPath)clone.Invoke(path,null)!;localPath.Prefab=localPrefab;localPath.Curves=[curve];
            foreach(var signal in localPath.GetSemaphores())yield return signal;
        }
    }

    internal static ETS2LA.Game.SDK.Semaphore? ReadLive(ETS2LA.Game.SDK.Semaphore[] samples,int id,Vector3 anchor)
    {
        var matches=samples.Where(s=>s.type==ETS2LA.Game.SDK.SemaphoreType.GATE && s.id==id &&
            Vector3.Distance(s.GetWorldCoordinates(),anchor)<=1).ToArray();
        if(matches.Length==0)return null;
        int state=matches[0].state;
        if(state is <0 or >3 || matches.Any(s=>s.state!=state))state=-1;
        return new() {id=id,position=anchor,type=ETS2LA.Game.SDK.SemaphoreType.GATE,state=state};
    }
}
