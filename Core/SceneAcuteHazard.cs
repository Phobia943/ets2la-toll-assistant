using System.Numerics;
namespace MautAssistent;
public static class SceneAcuteHazard
{
    // Emergency authority uses actual unexpanded measured truck boxes at
    // concrete selected-route poses. Tracking/motion/clearance margins belong
    // to ordinary caution; an overlap of those margins is not an intrusion.
    public static double? MeasuredDistance(SceneBody body,VehicleBody truck,RouteTrace road,Vector3 own,double speed,double age)
    {
        return MeasuredTruckCorridor.Create(truck,road,own,speed)?.Distance(body,age);
    }
    public static double? Distance(SceneBody body,IReadOnlyList<SceneCurve> curves,float height,float width,float frontReach,
        RouteTrace road,Vector3 own,double age,(double Start,double End)? itemRange=null,double? currentAlong=null)
    {
        // A conservative all-articulation sphere alone cannot establish an
        // acute collision. Require a currently almost stationary measured body
        // actually intruding into the narrow, selected driving corridor.
        if(!float.IsFinite(height)||height is <3 or >8||!float.IsFinite(width)||width is <2 or >5||
            (body.Flags&2)==0||body.Speed is <0 or >.1f||!double.IsFinite(age)||age is <0 or >.2||
            !float.IsFinite(frontReach)||frontReach is <0 or >20)return null;
        float reserve=(float)(body.Speed*age+.2);
        var observed=body with{HalfExtents=body.HalfExtents+new Vector3(reserve)};
        if(curves.All(c=>SceneClearance.Outside(c,observed,height,width)))return null;
        double origin=currentAlong??road.ProjectForTracking(own).Along;
        TraceProjection obstacle;
        if(itemRange is { } range) {
            if(!double.IsFinite(range.Start)||!double.IsFinite(range.End)||range.Start<0||range.End>road.Length||range.End<=range.Start)return null;
            obstacle=road.ProjectNearForTracking(body.Center,(range.Start+range.End)/2,(range.End-range.Start)/2);
        } else obstacle=road.Project(body.Center); // Pure legacy/offline caller.
        if(!double.IsFinite(origin)||origin<0||origin>road.Length||obstacle.Along<origin)return null;
        return obstacle.Along-origin-frontReach-body.HalfExtents.Length()-reserve-1;
    }
}
