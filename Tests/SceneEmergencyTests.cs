using System.Numerics;
using MautAssistent;
public static class SceneEmergencyTests
{
    public static void AcuteVersusPrecaution(Action<bool,string> assert)
    {
        SceneCurve[] line=[new(Vector3.Zero,new(0,0,100f/3),new(0,0,200f/3),new(0,0,100))];
        var road=new RouteTrace([Vector3.Zero,new(0,0,100)]);
        var body=new SceneBody(1,2,0,new(0,1,10),Quaternion.Identity,new(1,1,2),1);
        var distance=SceneAcuteHazard.Distance(body,line,4.4f,3.4f,3,road,Vector3.Zero,0);
        assert(distance.HasValue&&SceneStopPolicy.NeedsEmergency(50/3.6,distance),"Stationary measured body 10m ahead at 50km/h cannot use a gentle-only stop");
        var lateral=body with{Center=new(6,1,10)};
        assert(!SceneClearance.OutsideRig(line[0],lateral,15,0),"Adjacent parking lies inside the broad any-articulation enclosure");
        assert(SceneAcuteHazard.Distance(lateral,line,4.4f,3.4f,3,road,Vector3.Zero,0)==null,
            "A lateral parked body inside the broad sphere is not evidence of acute collision on the narrow route");
        assert(!SceneStopPolicy.NeedsEmergency(50/3.6,null),"Unknown evidence alone never triggers the acute-object emergency policy");
        assert(SceneAcuteHazard.Distance(body with{Flags=0},line,4.4f,3.4f,3,road,Vector3.Zero,0)==null,"Unknown motion is precautionary");
        assert(SceneAcuteHazard.Distance(body with{Speed=5},line,4.4f,3.4f,3,road,Vector3.Zero,0)==null,"Scalar speed without direction is not stationary-hazard proof");
        assert(SceneAcuteHazard.Distance(body,line,4.4f,3.4f,3,road,Vector3.Zero,.201)==null,"Stale observations cannot assert an acute object");
        assert(SceneStopPolicy.PlannedLimit(50/3.6,0)==-2.5f,"Unavailable evidence keeps the normal bounded slowdown");
        assert(SceneStopPolicy.PlannedLimit(0,0)==-.6f,"A planned standstill requests a physical hold outside the ACC dead band");
    }
}
