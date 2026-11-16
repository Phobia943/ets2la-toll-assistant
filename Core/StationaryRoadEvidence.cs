using System.Numerics;
namespace MautAssistent;
// Read-only ground geometry for enclosure calculations. Never a native route,
// sensor heartbeat, start permission or permission to drive a cached path.
public sealed record StationaryRoadEvidence(RouteTrace Trace,Vector3 Origin,double Heading,ulong Navigation,
    object AssetIdentity,uint ProcessId,ulong Session)
{
    public bool InvalidatedBy(Vector3 position,double heading,double speed,bool sdkActive)=>!sdkActive||
        !double.IsFinite(Heading)||!SceneSnapshotProtocol.Finite(Origin)||
        !double.IsFinite(heading)||!double.IsFinite(speed)||!SceneSnapshotProtocol.Finite(position)||
        Math.Abs(speed)>.1||Vector3.Distance(position,Origin)>.25f||
        Vector3.Dot(TrackingGuard.TruckForward(heading),TrackingGuard.TruckForward(Heading))<.995f;
    public bool Matches(Vector3 position,double heading,double speed,ulong navigation,object? assets,VehicleGeometry? shape)=>
        assets!=null&&ReferenceEquals(AssetIdentity,assets)&&shape!=null&&ProcessId!=0&&Session!=0&&
        shape.ProcessId==ProcessId&&shape.Session==Session&&Navigation==navigation&&
        !InvalidatedBy(position,heading,speed,true);
}
