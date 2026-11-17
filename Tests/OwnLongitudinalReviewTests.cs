using MautAssistent;
using System.Numerics;

// Independent policy counterexamples. These do not create SDK, output or plugin instances.
public static class OwnLongitudinalReviewTests
{
    public static void TrafficGeometry(Action<bool,string> check)
    {
        var straight=new RouteTrace([Vector3.Zero,new(0,0,-600)]);
        var body=new TrafficBody(new(0,0,-140),Quaternion.Identity,new(2.6f,4,10),25);
        TrafficSpeedDecision Plan(TrafficBody value)=>TrafficSpeedPolicy.Evaluate(straight,new(0,0,-10),25,[value]);
        var following=Plan(body);
        var oncoming=Plan(body with {Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI)});
        check(following.Acceleration>0&&oncoming.Acceleration<0,
            "Oncoming speed cannot receive the positive lead-speed credit of a vehicle travelling away");
        foreach(int component in Enumerable.Range(0,4)) {
            float[] values=[0,0,0,1];values[component]=float.NaN;
            var invalid=Plan(body with {Rotation=new(values[0],values[1],values[2],values[3])});
            check(!invalid.Clear&&float.IsFinite(invalid.Acceleration)&&invalid.Acceleration<=0,
                $"NaN rotation component {component} must not become an unconstrained cruise limit");
        }
        var tall=Plan(body with {Position=new(0,4,-80),Size=new(2.6f,8,10),Speed=0});
        check(!tall.Clear&&tall.Acceleration<0,
            "A tall body touching the road cannot be ignored because its centre exceeds a fixed height threshold");
        var tipped=Plan(body with {Position=new(3.4f,1.3f,-80),Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI/2),
            Size=new(2.6f,4,10),Speed=0});
        check(!tipped.Clear&&tipped.Acceleration<0,
            "Rotated body height contributes to lateral occupation of the own corridor");
        var loop=new RouteTrace([Vector3.Zero,new(0,0,-100),new(100,0,-100),new(100,0,0),
            Vector3.Zero,new(0,0,-100)]);
        var repeated=TrafficSpeedPolicy.Evaluate(loop,new(30,0,0),25,
            [new(new(0,0,-50),Quaternion.Identity,new(2.6f,4,10),0)]);
        check(!repeated.Clear&&repeated.Acceleration<0&&repeated.StopDistance.HasValue,
            "A stopped body on a forthcoming route occurrence cannot be discarded using its earlier occurrence behind the truck");
    }

    public static void DeliveryDuringHold(Action<bool,string> check)
    {
        foreach(bool full in new[]{false,true}) {
            var controller=new LongitudinalController();LongitudinalOutput? result=null;
            for(int i=0;i<80;i++)result=controller.Step(new(1+i*.02,1,true,false,true,full?10:0,0,0,
                AccelerationSafetyLimit:full?-4.5:null,PlannedHold:!full,ActualThrottle:0,ActualBrake:0,
                StopIdentity:"unchanged-physical-stop"));
            check(result?.DeliveryMissing==true&&result.Fault,
                full?"Unapplied full safety braking is observable even through the early full-brake return":
                    "Unapplied unchanged hold braking must not reset or bypass delivery monitoring forever");
        }
    }
}
