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

    public static void ThrottleReceiptAndEffect(Action<bool,string> check)
    {
        var controller=new LongitudinalController();double time=1;LongitudinalOutput prior=new(0,0,0,"initial");
        LongitudinalOutput Step(float? received,float effective,double? limit=null,bool emergency=false,bool enabled=true,bool route=true,long epoch=1,bool paused=false)
        {
            time+=.02;
            return controller.Step(new(time,epoch,enabled,paused,route,15,.2,25,
                AccelerationSafetyLimit:limit,Emergency:emergency,ActualThrottle:effective,ActualBrake:prior.Brake,
                ReceivedThrottle:received,ReceivedBrake:prior.Brake));
        }
        for(int i=0;i<80;i++)prior=Step(prior.Throttle,prior.Throttle);
        float confirmed=prior.Throttle;bool cutSafe=true;float cutMaximum=0;
        for(int i=0;i<60;i++) {
            prior=Step(prior.Throttle,0);cutMaximum=Math.Max(cutMaximum,prior.Throttle);
            cutSafe&=!prior.Fault&&!prior.ThrottleDeliveryMissing&&prior.Brake==0;
        }
        check(confirmed>.15f&&confirmed<.95f&&cutSafe&&prior.ThrottleEffectLimited,
            "A1.2s effective gas cut with continuing input receipts cannot become a transport fault or unsolicited service brake");
        check(cutMaximum<=confirmed+.00001f,
            "A suppressed effective gas pedal cannot train or slew a higher pending throttle request");
        float beforeRecovery=prior.Throttle;prior=Step(prior.Throttle,prior.Throttle);
        check(!prior.Fault&&!prior.ThrottleDeliveryMissing&&prior.Throttle<=beforeRecovery+.0091f,
            "Effective-gas recovery resumes only the bounded normal slew, without an accumulated full-throttle jump");
        bool noForcedBrake=true;
        for(int i=0;i<30;i++) {prior=Step(0,0);noForcedBrake&=!prior.Fault&&prior.Brake==0;}
        check(prior.ThrottleDeliveryMissing&&prior.Throttle==0&&noForcedBrake,
            "Actually missing received throttle suspends propulsion without fabricating a collision or global emergency");
        prior=Step(0,0,paused:true);
        check(prior.Throttle==0&&prior.Brake==0&&prior.ThrottleDeliveryMissing,
            "A genuine game pause releases pedals but does not acknowledge the suspended-propulsion latch");
        time+=2;prior=Step(.6f,.6f);
        check(prior.Throttle==0&&prior.Brake==0&&!prior.Fault&&prior.ThrottleDeliveryMissing,
            "Resuming after a long pause cannot silently restart propulsion even if input receipt has returned");
        controller.Pause();time+=1;prior=Step(.6f,.6f);
        check(prior.Throttle==0&&prior.Brake==0&&prior.ThrottleDeliveryMissing,
            "The plugin's direct Pause API also preserves the propulsion latch until explicit acknowledgement");
        prior=Step(0,0,-2);
        check(prior.Throttle==0&&prior.Brake>0&&prior.TargetAcceleration<=-2,
            "A genuine negative safety bound remains effective after propulsion suspension");
        prior=Step(0,0,emergency:true);
        check(prior.Throttle==0&&prior.Brake==1,
            "Explicit emergency braking has priority over the throttle-delivery warning state");
        prior=Step(0,0,route:false);
        check(prior.Throttle==0&&prior.Brake>0,
            "Losing the tracked route still receives the normal protective deceleration during throttle suspension");
        prior=Step(0,0,enabled:false);
        check(prior.Throttle==0&&prior.Brake==0&&!prior.ThrottleDeliveryMissing,
            "Explicit manual cancellation clears the propulsion latch without retaining a brake");

        foreach(float? unavailable in new float?[]{null,float.NaN}) {
            controller.Reset();prior=new(0,0,0,"initial");
            for(int i=0;i<80;i++)prior=Step(prior.Throttle,prior.Throttle);
            bool unknownSafe=true;
            for(int i=0;i<50;i++) {prior=Step(unavailable,0);unknownSafe&=!prior.Fault&&!prior.ThrottleDeliveryMissing&&prior.Brake==0;}
            check(unknownSafe&&prior.ThrottleEffectLimited,
                "An unavailable input acknowledgement is unknown evidence, not a measured zero-throttle transport failure");
        }
    }
}
