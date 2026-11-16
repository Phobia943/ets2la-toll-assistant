using System.Numerics;
namespace MautAssistent;
internal static class WheelAxleTests
{
    internal static void Evidence(Action<bool,string> assert)
    {
        WheelSample W(float x,float z,bool lifted=false)=>new(new(x,.5f,z),true,false,true,!lifted,lifted?1:0,0);
        var hook=new Vector3(0,1,-5);
        var wheels=new[]{W(-1,3),W(1,3),W(-1,4.3f),W(1,4.3f),W(-1,5.6f),W(1,5.6f)};
        var proof=WheelAxle.Trailer(hook,wheels);
        assert(proof.Supported&&proof.ActiveAxles==3&&Math.Abs(proof.HookDistance-9.3)<.0001,
            "A fixed three-axle trailer uses its measured bogie centre and actual hook, not body length");
        var lifted=wheels.Select((w,i)=>i<2?w with{OnGround=false,Lift=1}:w).ToArray();
        var liftProof=WheelAxle.Trailer(hook,lifted);
        assert(liftProof.Supported&&liftProof.ActiveAxles==2&&Math.Abs(liftProof.Axle.Z-4.95f)<.0001,
            "An actually lifted complete axle is excluded from the effective contact model");
        assert(!WheelAxle.Trailer(hook,wheels[..^1]).Supported,"A missing wheel cannot invent a symmetric axle");
        assert(!WheelAxle.Trailer(hook,wheels.Concat([wheels[1]]).ToArray()).Supported,"Duplicated candidate wheels make a pair ambiguous");
        assert(!WheelAxle.Trailer(hook,wheels.Select(w=>w with{Steerable=true}).ToArray()).Supported,"A steerable axle remains unsupported at zero angle");
        var airborne=WheelAxle.Trailer(hook,wheels.Select(w=>w with{OnGround=false}).ToArray());
        assert(airborne.Supported&&!airborne.ContactStable&&airborne.Axle==proof.Axle,"Airborne reports withdraw contact confidence without inventing or shifting axle geometry");
        assert(!WheelAxle.Trailer(new(0,1,10),wheels).Supported,"A hook behind the axle cannot enter the forward trailer model");
        assert(!WheelAxle.Trailer(hook,wheels.Select(w=>w with{Position=Vector3.Zero}).ToArray()).Supported,"Default-filled arrays are not real wheel evidence");
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.1f,1.1f})
            assert(!WheelAxle.Trailer(hook,[wheels[0] with{Lift=invalid},..wheels.Skip(1)]).Supported,"Nonfinite or invalid lift observations cannot silently select an axle");
        var decoration=wheels.Concat([new WheelSample(new(0,2,2),false,false,false,false,0,0)]).ToArray();
        assert(WheelAxle.Trailer(hook,decoration).Supported,"An explicitly nonsimulated decorative wheel does not move the contact model");
        var twins=new[]{3f,4.3f,5.6f}.SelectMany(z=>new[]{W(-.85f,z),W(.85f,z),W(-1.15f,z),W(1.15f,z)}).ToArray();
        var twinsProof=WheelAxle.Trailer(hook,twins);
        assert(twinsProof.Supported&&twinsProof.ActiveAxles==3&&Math.Abs(twinsProof.HookDistance-9.3)<.0001,
            "Twin tyres do not double the effective axle count or shift the bogie centre");
        var asymmetric=new[]{W(-1,3),W(-.85f,3),W(.9f,3),W(1.15f,3)};
        foreach(var order in new[]{asymmetric,asymmetric.Reverse().ToArray(),new[]{asymmetric[1],asymmetric[3],asymmetric[0],asymmetric[2]}}) {
            var p=WheelAxle.Trailer(hook,order);
            assert(p.Supported&&p.ActiveAxles==1&&Math.Abs(p.HookDistance-8)<.0001,"A globally valid twin-tyre match is independent of input order");
        }
        var flicker=WheelAxle.Trailer(hook,[wheels[0] with{OnGround=false},..wheels.Skip(1)]);
        assert(flicker.Supported&&!flicker.ContactStable&&flicker.Axle==proof.Axle,"One momentary wheel-contact change cannot redefine the bogie centre");
        var transition=WheelAxle.Trailer(hook,[wheels[0] with{Lift=.5f},..wheels.Skip(1)]);
        assert(transition.Supported&&transition.LiftTransition&&!transition.ContactStable&&transition.Axle==proof.Axle,"A partially moving lift axle lowers confidence until a complete raised axle is observed");
    }
}
