using MautAssistent;
using System.Numerics;
public static class TrafficSpeedTests
{
    public static void Run(Action<bool,string> check)
    {
        check(SimulationAcceleration.TryMeasure(1_000_000,1_100_000,10,10.1,out var acceleration)&&Math.Abs(acceleration-1)<.00001,
            "Dynamics are measured in source microseconds rather than callback delivery time");
        check(!SimulationAcceleration.TryMeasure(1_000_000,1_000_000,10,10.1,out _)&&
            !SimulationAcceleration.TryMeasure(1_000_000,900_000,10,10.1,out _)&&
            !SimulationAcceleration.TryMeasure(1_000_000,5_000_000,10,10.1,out _),"Frozen, reset and excessively gapped source clocks are not acceleration evidence");
        var trace=new RouteTrace([Vector3.Zero,new(0,0,-600)]);
        TrafficBody Body(float x,float z,double speed=0,float y=0)=>new(new(x,y,z),Quaternion.Identity,new(2.6f,4,10),speed);
        TrafficSpeedDecision Plan(params TrafficBody[] bodies)=>TrafficSpeedPolicy.Evaluate(trace,new(0,0,-10),25,bodies);
        check(Plan().Clear&&float.IsNaN(Plan().Acceleration),"Fresh empty route imposes no speed/brake limit");
        check(Plan(Body(4.5f,-40)).Clear,"Adjacent same-direction lane does not create a false stop");
        check(Plan(Body(0,-40,0,8)).Clear,"Road above does not create a false stop");
        check(Plan(Body(0,20)).Clear,"Vehicle behind does not create a false stop");
        var stopped=Plan(Body(0,-50));
        check(stopped.Acceleration<0&&stopped.StopDistance>0,"Stationary body in own route requires braking");
        var lead=Plan(Body(0,-120,25));
        check(lead.Acceleration>0&&lead.StopDistance==null,"Same-speed vehicle with sufficient gap allows cruise");
        check(Plan(Body(0,-120,5)).Acceleration<lead.Acceleration,"Closing traffic reduces acceleration bound");
        check(Plan(Body(0,-120,25) with {Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI)}).Acceleration<lead.Acceleration,
            "Oncoming vehicle receives no optimistic forward-speed credit");
        check(!Plan(Body(0,-50) with {Size=Vector3.Zero}).Clear,"Unknown body shape cannot authorize free cruise");
        check(!Plan(Body(0,-50) with {Rotation=new(float.NaN,0,0,1)}).Clear,"Invalid body direction cannot authorize free cruise");
        var own=new LongitudinalController();
        var drive=own.Step(new(1,1,true,false,true,0,0,20/3.6,StopDistance:50));
        check(drive.Throttle>0&&drive.Brake==0,"Planned stopping distance permits a safe approach from rest");
    }
}
