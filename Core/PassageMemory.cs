using System.Numerics;
namespace MautAssistent;

// Only measures the tail of an already released passage. This is not a steering
// route and cannot replace a missing/invalid live navigation route.
public sealed class PassageMemory
{
    RouteTrace? trace;
    Vector3 gatePosition;
    string? key;
    double gateAlong;
    public void Reset() {trace=null;key=null;}
    public Vector3? PositionFor(string? gateKey)=>key!=null&&key==gateKey?gatePosition:null;
    public void Capture(string gateKey,Vector3 position,RouteTrace route)
    {
        if(key==gateKey)return;
        var projection=route.Project(position);
        if(projection.Offset>4)return;
        key=gateKey;gatePosition=position;trace=route;gateAlong=projection.Along;
    }
    public Gate? AfterCrossing(string? tracked,Vector3 truck,Vector3 liveGatePosition,
        BarrierState state,bool freshNavigation,bool passageReleased)
    {
        if(!freshNavigation||!passageReleased||trace==null||tracked==null||key!=tracked||
            Vector3.Distance(liveGatePosition,gatePosition)>1)return null;
        var projection=trace.ProjectForTracking(truck);
        double distance=gateAlong-projection.Along;
        if(projection.Offset>3||distance>=-1)return null;
        return new(tracked,distance,state);
    }
}
