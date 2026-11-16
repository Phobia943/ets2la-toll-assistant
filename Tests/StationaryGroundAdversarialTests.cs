using System.Numerics;
using MautAssistent;

public static class StationaryGroundAdversarialTests
{
    public static void Matching(Action<bool,string> assert)
    {
        var assets=new object();var road=new RouteTrace([new(0,0,-30),new(0,0,30)]);
        var truck=new VehicleBody(Vector3.Zero,Quaternion.Identity,new(-1,0,-3),new(1,4,3));
        var shape=new VehicleGeometry(321,44,2,10000,truck,true,null);
        var ground=new StationaryRoadEvidence(road,Vector3.Zero,0,9,assets,321,44);
        bool Match(StationaryRoadEvidence? value=null,Vector3? pose=null,double heading=0,double speed=0,
            ulong navigation=9,object? files=null,VehicleGeometry? rig=null)=>(value??ground).Matches(pose??Vector3.Zero,heading,speed,navigation,files??assets,rig??shape);
        assert(Match(),"A stationary matching asset/navigation/process/session may reuse only its real ground geometry");
        assert(Match(pose:new(.01f,0,0),speed:.005),"Small real standstill jitter does not make cold prewarming impossible");
        assert(!Match(pose:new(.251f,0,0)),"Ground matching is bound to the preview origin tolerance");
        assert(!Match(heading:.02),"A materially changed heading cannot reuse the preview ground basis");
        assert(!Match(speed:.1001)&&!Match(speed:-.1001),"Both forward and reverse motion withdraw stationary geometry reuse");
        assert(!Match(navigation:10),"A new navigation plan cannot inherit the old preview basis");
        assert(!Match(files:new object()),"Even an equal-looking new asset source cannot inherit old geometry evidence");
        assert(!Match(rig:shape with{ProcessId=322})&&!Match(rig:shape with{Session=45}),"Source process and source session changes are distinct revocations");
        assert(!ground.Matches(Vector3.Zero,0,0,9,assets,null),"Missing native identity cannot borrow a cached road basis");
        assert(!ground.Matches(Vector3.Zero,0,0,9,null,shape),"Missing live asset identity cannot borrow a cached road basis");
        assert(ground.InvalidatedBy(Vector3.Zero,0,0,false),"SDK inactivity explicitly invalidates the standing geometry context");
        assert(ground.InvalidatedBy(new(float.NaN,0,0),0,0,true)&&ground.InvalidatedBy(Vector3.Zero,double.NaN,0,true)&&
            ground.InvalidatedBy(Vector3.Zero,0,double.NaN,true),"Invalid current pose, heading or speed cannot become a matching standstill");
        assert(!Match(ground with{Origin=new(float.NaN,0,0)})&&!Match(ground with{Heading=double.NaN}),
            "Malformed stored origin or heading cannot exploit unordered floating-point comparisons");
        assert(ReferenceEquals(ground.Trace,road)&&shape.UtcMs==10000,
            "Matching does not replace the actual trace or relabel the native observation timestamp");
    }
}
