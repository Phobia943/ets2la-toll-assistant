using System.Numerics;
using ETS2LA.Game.Utils;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;

namespace MautAssistent;

internal static class ManualTriggerEvidence
{
    public static IReadOnlyDictionary<int,ManualStopZone> Read(PrefabDescriptor descriptor,IEnumerable<Part> parts,
        byte[] pmg,IReadOnlyDictionary<string,Unit> definitions,bool specialJob)
    {
        var candidates=new Dictionary<int,List<ManualStopZone?>>();
        foreach(var locator in parts.SelectMany(p=>p.Locators)) {
            string? hook=TollModelEvidence.ReadHookup(pmg,locator.HookupOffset);
            if(hook==null||!definitions.TryGetValue(hook,out var unit)||TollDefinition.Read(unit,specialJob).Payment!=TollPayment.Manual)continue;
            var association=TollModelEvidence.Match(descriptor,[(locator.Position,TollPayment.Manual)]);
            if(association.Count!=1)continue;
            int gate=association.Keys.Single();
            if(!candidates.TryGetValue(gate,out var list))candidates[gate]=list=[];
            var curves=descriptor.NavCurves.Where(c=>c.SemaphoreId==gate).ToArray();
            var signals=descriptor.Semaphores.Where(s=>s.SemaphoreId==gate).Select(s=>s.Position).ToArray();
            list.Add(curves.Length==1?Inspect(curves[0],locator,unit,signals):null);
        }
        var result=new Dictionary<int,ManualStopZone>();
        foreach(var (gate,zones) in candidates) {
            if(zones.Any(z=>z==null))continue;
            double minimum=zones.Max(z=>z!.MinimumOffset),maximum=zones.Min(z=>z!.MaximumOffset);
            if(minimum<=maximum)result[gate]=new(minimum,maximum);
        }
        return result;
    }
    static ManualStopZone? Inspect(NavCurve curve,Locator marker,Unit unit,Vector3[] signals)
    {
        double? Number(string name)=>unit.Attributes.TryGetValue(name,out var value)?(object?)value switch {
            int n=>n,float n=>n,double n=>n,_=>null}:null;
        var length=Number("trigger_length");var width=Number("trigger_width");
        if(length is not >2 or >50||width is not >=3 or >20||unit.Attributes.ContainsKey("trigger_distance")||
            (unit.Attributes.ContainsKey("trigger_offset")&&Number("trigger_offset")!=0)||Math.Abs(marker.Scale-1)>.0001f)return null;
        var delta=curve.EndPosition-curve.StartPosition;
        if(delta.LengthSquared()<1||Math.Abs(delta.Y)>.25f)return null;
        var forward=Vector3.Normalize(delta);
        if(signals.Length==0||signals.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||
            Math.Abs(Vector3.Dot(p-curve.EndPosition,forward))>1||Math.Abs(p.Y-curve.EndPosition.Y)>1))return null;
        var offsets=signals.Select(p=>(double)Vector3.Dot(p-marker.Position,forward)).ToArray();
        if(offsets.Max()-offsets.Min()>.5)return null;
        if(!float.IsFinite(marker.Rotation.LengthSquared())||Math.Abs(marker.Rotation.LengthSquared()-1)>.001f||
            Math.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitZ,marker.Rotation),forward))<.995f)return null;
        // No curved or disconnected extrapolation from a trigger rectangle.
        for(int i=0;i<=8;i++) {
            var sample=PrefabUtils.InterpolateNavCurveOriented(curve,i/8f).Position;
            if(Vector3.Distance(sample,Vector3.Lerp(curve.StartPosition,curve.EndPosition,i/8f))>.2f)return null;
        }
        double fromStart=Vector3.Dot(marker.Position-curve.StartPosition,forward);
        if(fromStart<length/2||delta.Length()-fromStart<length/2)return null;
        var projected=curve.StartPosition+(float)fromStart*forward;
        if(Vector3.Distance(projected,marker.Position)>.5f)return null;
        // One metre inside the model trigger. Runtime still refuses any offset
        // closer to the barrier than the user's existing safe stop offset.
        return new(offsets.Max()-length.Value/2+1,offsets.Min()+length.Value/2-1);
    }
}
