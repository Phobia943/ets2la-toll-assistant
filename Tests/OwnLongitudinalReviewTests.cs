using MautAssistent;
using System.Numerics;

// Independent policy counterexamples. These do not create SDK, output or plugin instances.
public static class OwnLongitudinalReviewTests
{
    public static void ParkingAndReverseEvidence(Action<bool,string> check)
    {
        var controller=new LongitudinalController();double time=1;
        LongitudinalOutput Step(double speed=0,bool park=true,bool reverse=false,bool rollback=false,
            double? limit=null,bool emergency=false,bool planned=false,bool route=true,float received=0)
        {
            time+=.02;
            return controller.Step(new(time,1,true,false,route,speed,0,25,
                AccelerationSafetyLimit:limit,Emergency:emergency,PlannedHold:planned,
                ActualThrottle:0,ActualBrake:0,ReceivedThrottle:received,
                ParkingBrake:park,ReverseGear:reverse,RollbackDetected:rollback));
        }
        bool standSafe=true;LongitudinalOutput output=new(0,0,0,"initial");
        for(int i=0;i<150;i++) {
            output=Step(i%2==0?-.011614:.0046,limit:i%2==0?-.35:null);
            standSafe&=output.ParkingHold&&!output.Fault&&output.Throttle==0&&output.Brake==0&&!output.DeliveryMissing;
        }
        check(standSafe,"Fresh parked stand remains neutral through three seconds of signed-speed and planning-bound jitter without delivery faults");
        output=Step(.09);
        check(output.ParkingHold&&output.Throttle==0&&output.Brake==0,"An established parking stand survives the exit hysteresis band");
        output=Step(.101);
        check(!output.ParkingHold&&output.Throttle==0&&output.Brake>0,"Moving beyond the parking stand band requests service braking rather than neutralizing motion");
        controller.Reset();output=Step(.09);
        check(!output.ParkingHold&&output.Brake>0,"The parking exit threshold cannot establish a previously unconfirmed stand");
        controller.Reset();Step();output=Step(park:false);
        check(!output.Fault&&!output.ParkingHold&&output.Brake==0&&output.Throttle<=.01f,"Releasing a stationary parking brake resumes with fresh bounded pedals and no learned brake remainder");
        controller.Reset();Step();output=Step(park:false,planned:true);
        check(output.Brake>=.35f&&output.Throttle==0,"Parking release does not erase an independently required planned hold");
        controller.Reset();Step();output=Step(park:false,limit:-2);
        check(output.Brake>0&&output.Throttle==0&&output.TargetAcceleration<=-2,"Parking release does not erase a newly required hazard deceleration");
        controller.Reset();output=Step(emergency:true);
        check(output.Brake==1&&output.Throttle==0&&!output.ParkingHold,"An explicit emergency dominates stationary parking neutrality");
        controller.Reset();output=Step(1);
        check(!output.ParkingHold&&output.Brake>0&&output.Throttle==0,"A parking flag at one metre per second cannot suppress ordinary stopping brakes");
        controller.Reset();output=Step(reverse:true);
        check(output.ManualRequired&&!output.Fault&&output.Throttle==0&&output.Brake==0,"Reverse gear at stand requests manual control without an invented collision emergency");
        controller.Reset();output=Step(-.101);
        check(output.Fault&&output.Throttle==0,"Unambiguous reverse speed is not hidden by parking-brake neutrality");
        controller.Reset();output=Step(-.03,rollback:true);
        check(output.Fault&&output.Throttle==0,"Position-confirmed slow rollback overrides the parking stand band");
        controller.Reset();
        float last=0;
        for(int i=0;i<80;i++) {
            time+=.02;output=controller.Step(new(time,1,true,false,true,15,0,25,ActualThrottle:last,ActualBrake:0,ReceivedThrottle:last));last=output.Throttle;
        }
        for(int i=0;i<30;i++)Step(15,park:false);
        check(Step().ThrottleDeliveryMissing,"A genuine propulsion-delivery latch survives entry into parking stand");
        output=Step(park:false,received:.7f);
        check(output.ThrottleDeliveryMissing&&output.Throttle==0&&!output.Fault,"Parking release cannot acknowledge a pre-existing propulsion suspension");

        var evidence=new ReverseMotionEvidence();
        DirectionSample Sample(int index,Vector3 position,double speed=-.03,long epoch=1,bool valid=true,double heading=0)
            =>new((ulong)(index+1),index*.1,epoch,position,heading,speed,valid);
        bool falseRollback=false;
        for(int i=0;i<300;i++)falseRollback|=evidence.Observe(Sample(i,new(0,0,i%2==0?0:.000977f),i%2==0?.0046:-.011614));
        check(!falseRollback&&!evidence.StationaryDisplaced&&evidence.BackwardMetres<.002,"Float-position oscillation with forward credit cannot accumulate fictitious rollback or parking displacement over thirty seconds");
        void ForwardCreep()
        {
            evidence.Reset();for(int i=0;i<=10;i++)evidence.Observe(Sample(i,new(0,0,-i*.008f),.08));
        }
        ForwardCreep();
        check(evidence.StationaryDisplaced&&!evidence.Confirmed,"Sustained forward creep withdraws stationary authority without fabricating reverse travel");
        controller.Reset();Step();time+=.02;
        output=controller.Step(new(time,1,true,false,true,.08,0,25,ParkingBrake:true,StationaryDisplacement:true));
        check(!output.ParkingHold&&output.Throttle==0&&output.Brake>0,"Position-confirmed forward creep defeats parking hysteresis and receives ordinary stopping brakes");
        var stopped=Sample(11,new(0,0,-.08f),0);evidence.Observe(stopped);
        for(int i=0;i<100;i++)evidence.Observe(stopped);
        check(evidence.StationaryDisplaced,"One repeated stopped simulation frame cannot establish a fresh stable parking rebase");
        for(int i=12;i<20;i++)evidence.Observe(Sample(i,new(0,0,-.08f),0));
        check(!evidence.StationaryDisplaced,"A genuinely stopped sequence of fresh source frames can restore stationary authority after service braking");
        ForwardCreep();bool prematureRebase=false;
        for(int i=11;i<40;i++) {
            evidence.Observe(Sample(i,new(0,0,-.08f-(i-10)*.0009f),.009));
            prematureRebase|=!evidence.StationaryDisplaced;
        }
        check(!prematureRebase,"Small per-frame movements cannot falsely rebase while total displacement exceeds the stable-window bound");
        evidence.Reset();bool found=false;
        for(int i=0;i<30;i++)found|=evidence.Observe(Sample(i,new(0,0,i*.003f)));
        check(found,"Coherent slow reverse displacement is still detected despite lying inside the stand-speed band");
        evidence.Reset();evidence.Observe(Sample(0,Vector3.Zero));
        var duplicate=Sample(1,new(0,0,.01f));evidence.Observe(duplicate);
        double debt=evidence.BackwardMetres;
        for(int i=0;i<100;i++)evidence.Observe(duplicate);
        check(!evidence.Confirmed&&evidence.BackwardMetres==debt,"Repeated reads of one original simulation frame cannot manufacture reverse samples or distance");
        evidence.Reset();evidence.Observe(Sample(0,Vector3.Zero));
        evidence.Observe(Sample(1,new(0,0,.06f)));
        check(!evidence.Confirmed,"One corrected pose frame cannot alone establish sustained rollback");
        evidence.Observe(Sample(2,Vector3.Zero,.02));
        check(evidence.BackwardMetres<=.002&&!evidence.Confirmed,"An actual compensating forward displacement repays all reverse evidence");
        foreach(string reset in new[]{"epoch","invalid","teleport","heading","clock"}) {
            evidence.Reset();for(int i=0;i<6;i++)evidence.Observe(Sample(i,new(0,0,i*.006f)));
            var next=Sample(6,new(0,0,.036f));
            next=reset switch {
                "epoch"=>next with{Epoch=2},"invalid"=>next with{Valid=false},
                "teleport"=>next with{Position=new(0,0,1)},"heading"=>next with{Heading=.3},
                _=>next with{Time=2}
            };
            evidence.Observe(next);
            check(!evidence.Confirmed&&evidence.BackwardMetres==0,$"Rollback evidence is revoked on {reset} discontinuity");
        }
    }
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
