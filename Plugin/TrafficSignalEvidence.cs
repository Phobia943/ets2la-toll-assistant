using System.Reflection;
using System.Security.Cryptography;
using TruckLib.ScsMap;
using System.Numerics;
using PathLib;

namespace MautAssistent;

internal static class TrafficSignalEvidence
{
    // This supported TruckLib build reads the flag, but predates the public
    // Prefab.Semaphores property. Bit 19 controls spawned semaphore models;
    // a PPD profile alone does not mean that an instance has traffic lights.
    // See TruckLib Prefab.Semaphores and upstream PR 22. Never infer "off"
    // from an absent reflection member or an unsupported library version.
    static readonly Lazy<Task<bool>> supported=new(()=>Task.Run(()=> {
        try {return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Prefab).Assembly.Location)))==
            "728BB116B6161498A1C4D1F6095BC5CA3B0CC680091BA83DE06CDB33769E3697";}
        catch{return false;}
    }));
    internal static Task<bool> Prepare()=>supported.Value;
    static readonly PropertyInfo? kdop=typeof(MapItem).GetProperty("Kdop",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);

    internal static bool? SpawnEnabled(Prefab? prefab)
    {
        if(prefab==null||!supported.IsValueCreated||!supported.Value.IsCompletedSuccessfully||!supported.Value.Result)return null;
        try {
            var bounds=kdop?.GetValue(prefab);
            var flags=bounds?.GetType().GetField("Flags",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(bounds);
            return flags?.GetType().GetProperty("Item",[typeof(int)])?.GetValue(flags,[19]) as bool?;
        } catch {return null;}
    }
    internal static bool OmitUnspawned(bool? spawnEnabled,bool observedActor)=>spawnEnabled==false&&!observedActor;
    internal static bool OwnsBarrier(PlannedItem item,TollBarrierPlan? barrier)
    {
        if(barrier==null||item.prefabPath==null||barrier.RouteIdentity==null||
            !float.IsFinite(barrier.Anchor.LengthSquared())||!float.IsFinite(barrier.Forward.LengthSquared())||
            Math.Abs(barrier.Forward.LengthSquared()-1)>.05f||
            item.prefabPath.Prefab.Prefab?.Uid!=barrier.PrefabUid||RouteMatcher.ItemIdentity(item)!=barrier.RouteIdentity)return false;
        var canonical=TollSignals.ForPath(item.prefabPath).Where(s=>s.Semaphore.SemaphoreId==barrier.GateId).ToArray();
        if(canonical.Length!=1||Vector3.Distance(canonical[0].GetWorldOrientedPoint().Position,barrier.Anchor)>1)return false;
        var direction=canonical[0].GetCurveEndWorldCoordinates().Position-canonical[0].GetCurveStartWorldCoordinates().Position;
        return direction.LengthSquared()>.01f&&float.IsFinite(direction.LengthSquared())&&
            Vector3.Dot(Vector3.Normalize(direction),barrier.Forward)>.95f;
    }
}
