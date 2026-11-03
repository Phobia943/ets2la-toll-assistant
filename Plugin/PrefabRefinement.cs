using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
using TruckLib.Models.Ppd;

namespace MautAssistent;

/// <summary>
/// The host projects onto each nav curve's endpoint chord. Shorter pieces keep
/// that projection close to the actual curved lane, without changing its entry,
/// exit or topology. Only our selected route is refined; map data stays intact.
/// </summary>
public static class PrefabRefinement
{
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;
    static readonly ConditionalWeakTable<PrefabPath,PrefabPath> originals=new();
    public static PrefabPath OriginalFor(PrefabPath path)=>originals.TryGetValue(path,out var original)?original:path;

    public static PrefabPath Refine(PrefabPath source)
    {
        if(originals.TryGetValue(source,out _))return source;
        var pieces=new List<NavCurve>();
        int lengthUnits=0;
        foreach(var curve in source.Curves) {
            if(!float.IsFinite(curve.Length)||curve.Length<=0)
                throw new InvalidOperationException("Ungültige Mautkurvenlänge");
            int count=(int)Math.Ceiling(Math.Max(curve.Length,Vector3.Distance(curve.StartPosition,curve.EndPosition)));
            if(count<1 || count>4096 || pieces.Count+count>16384)
                throw new InvalidOperationException("Mautkurve überschreitet die unterstützte Geometriegröße");
            for(int i=0;i<count;i++) {
                var start=PrefabUtils.InterpolateNavCurveOriented(curve,(float)i/count);
                var end=PrefabUtils.InterpolateNavCurveOriented(curve,(float)(i+1)/count);
                float length=Vector3.Distance(start.Position,end.Position);
                if(!float.IsFinite(length)||length<.00001f)
                    throw new InvalidOperationException("Entartetes Mautkurvensegment");
                var piece=(NavCurve)clone.Invoke(curve,null)!;
                piece.StartPosition=start.Position;piece.EndPosition=end.Position;
                piece.StartRotation=start.Rotation;piece.EndRotation=end.Rotation;
                // Binary-exact millimetre-scale lengths avoid accumulation gaps
                // at t=0/1 in the host's forward/backward interpolation loops.
                int units=Math.Max(1,(int)MathF.Round(length*1024));
                lengthUnits=checked(lengthUnits+units);
                if(lengthUnits>16_777_216)throw new InvalidOperationException("Mautroute zu lang");
                piece.Length=units/1024f;
                // Semaphores belong to the original curve end, not every piece.
                piece.SemaphoreId=i==count-1?curve.SemaphoreId:-1;
                pieces.Add(piece);
            }
        }
        var result=(PrefabPath)clone.Invoke(source,null)!;
        result.Curves=pieces;result.Length=lengthUnits/1024f;
        originals.Add(result,source);
        return result;
    }
}
