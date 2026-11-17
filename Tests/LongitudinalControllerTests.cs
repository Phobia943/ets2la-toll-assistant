using MautAssistent;
using System.Text.Json;

public static class LongitudinalControllerTests
{
    static LongitudinalInput Input(double time,double speed=20,double target=100/3.6)=>
        new(time,1,true,false,true,speed,0,target,ActualThrottle:0,ActualBrake:0,ReceivedThrottle:0,ReceivedBrake:0);
    static void Pedals(Action<bool,string> check,LongitudinalOutput output,string reason)=>
        check(float.IsFinite(output.Throttle)&&float.IsFinite(output.Brake)&&output.Throttle is >=0 and <=1&&
            output.Brake is >=0 and <=1&&(output.Throttle==0||output.Brake==0),reason);

    public static void SafetyAndOwnership(Action<bool,string> check)
    {
        var controller=new LongitudinalController();
        var accelerate=controller.Step(Input(1));Pedals(check,accelerate,"First command has bounded exclusive finite pedals");
        var safety=controller.Step(Input(1.05) with{AccelerationSafetyLimit=-1.2,CoastEligible=true});
        check(safety.Throttle==0&&safety.Brake>0&&safety.TargetAcceleration<=-1.2,"Negative safety bound defeats coast and comfort jerk in its first tick");
        var extreme=controller.Step(Input(1.1) with{AccelerationSafetyLimit=-7});
        check(extreme.Throttle==0&&extreme.Brake==1&&extreme.TargetAcceleration<=-7,"Safety bound beyond service model requests full brake without pretending a weaker bound");
        var emergency=controller.Step(Input(1.15) with{Emergency=true});
        check(emergency.Throttle==0&&emergency.Brake==1,"Explicit emergency bypasses smoothing");
        var manual=controller.Step(Input(1.2) with{Enabled=false});
        check(manual.Throttle==0&&manual.Brake==0,"Manual cancellation releases both commands and resets state");
        var paused=controller.Step(Input(1.25) with{Paused=true});
        check(paused.Throttle==0&&paused.Brake==0,"Pause cannot retain either pedal");
        var unknown=controller.Step(Input(1.3) with{RouteKnown=false,CoastEligible=true});
        check(unknown.Throttle==0&&unknown.Brake>0,"Unknown route receives ordinary deceleration and no gas");
        var unavailable=controller.Step(Input(1.35) with{OutputAvailable=false});
        check(unavailable.Throttle==0&&unavailable.Brake==0&&unavailable.Fault,"Unavailable transport resets feedback and does not claim applied braking");
        var fresh=controller.Step(Input(2));
        var repeated=controller.Step(Input(2));var repeatedAgain=controller.Step(Input(2));
        check(repeated.Throttle==0&&repeated.Brake==0&&repeatedAgain.Throttle==0,"Repeated timestamps cannot reacquire accumulated throttle");
        check(controller.Step(Input(1.9)).Fault,"Backward controller time is rejected");
        check(controller.Step(Input(4)).Fault,"Long controller gap has no accumulated integration budget");
        var epoch=controller.Step(Input(4.05) with{Epoch=2});
        check(epoch.Throttle<=.01f&&epoch.Brake==0,"A new epoch starts with a small fresh ramp rather than old pedal state");
        check(controller.Step(Input(4.1) with{Speed=-1}).Fault,"Actual reverse travel is not forward cruise");
        check(controller.Step(Input(4.2) with{AccelerationSafetyLimit=double.NaN}).Fault,"Unknown numeric safety bound cannot become permission");
        check(controller.Step(Input(4.3) with{TargetSpeed=double.NaN}).Fault,"Unknown target in a supposedly known route cannot accelerate");
        check(controller.Step(Input(4.4) with{Speed=double.MaxValue}).Fault,"Finite implausible speed cannot overflow the stopping model");
        check(controller.Step(Input(4.5) with{StopDistance=double.MaxValue}).Fault,"Finite implausible stop distance is not an unbounded control horizon");
        var emergencyUnknown=controller.Step(Input(4.6) with{Emergency=true,Speed=double.NaN,OutputAvailable=false});
        check(emergencyUnknown.Throttle==0&&emergencyUnknown.Brake==1&&emergencyUnknown.Fault,
            "Explicit emergency remains a full brake request despite invalid speed and unavailable transport");
    }

    public static void CoastStopsAndFeedback(Action<bool,string> check)
    {
        var c=new LongitudinalController();
        var coast=c.Step(Input(1,85/3.6,80/3.6) with{CoastEligible=true,Acceleration=-.05});
        check(coast.Coasting&&coast.Throttle==0&&coast.Brake==0,"Soft85-to80km/h request coasts on level uncritical road");
        c.Reset();var releaseGas=c.Step(Input(1,85/3.6,80/3.6) with{CoastEligible=true,Acceleration=.2});
        check(releaseGas.Coasting&&releaseGas.Throttle==0&&releaseGas.Brake==0,"Residual positive acceleration from the previous gas phase first receives true neutral, never a gas or brake blip");
        for(int i=1;i<=25;i++)releaseGas=c.Step(Input(1+i*.05,85/3.6,80/3.6) with{CoastEligible=true,Acceleration=.2});
        check(!releaseGas.Coasting&&releaseGas.Throttle==0&&releaseGas.Brake>0,"Persistently increasing speed cannot keep renewing the bounded soft-coast reaction interval");
        c.Reset();c.Step(Input(1,85/3.6,80/3.6) with{CoastEligible=true,Acceleration=-.05});
        var curve=c.Step(Input(1.05,85/3.6,80/3.6) with{CoastEligible=true,AccelerationSafetyLimit=-.2});
        check(!curve.Coasting&&curve.Throttle==0&&curve.Brake>0,"The same small speed difference cannot coast through a curve safety bound");
        c.Reset();var downhill=c.Step(Input(2,85/3.6,80/3.6) with{CoastEligible=true,Pitch=-.04,Acceleration=.2});
        check(!downhill.Coasting&&downhill.Brake>0,"Increasing downhill overspeed is actively controlled");
        c.Reset();var hold=c.Step(Input(3,0,0) with{PlannedHold=true,StopIdentity="gate10"});
        check(hold.Holding&&hold.Throttle==0&&hold.Brake>=.35,"Planned stop has an explicit bounded brake hold");
        var released=c.Step(Input(3.05,0,10/3.6) with{StopIdentity=null});
        check(!released.Holding&&released.Brake==0&&released.Throttle<=.03,"Current gate release removes hold immediately with a fresh small throttle ramp");
        c.Reset();LongitudinalOutput? last=null;
        for(int i=0;i<100;i++)last=c.Step(Input(5+i*.05,10,100/3.6));
        check(last!.DeliveryMissing&&last.ThrottleDeliveryMissing&&!last.Fault&&last.Throttle==0&&last.Brake==0,
            "Missing received throttle suspends propulsion without an emergency stop or steering fault");
        var noResume=c.Step(Input(10.05,10,100/3.6) with{ActualThrottle=1});
        check(noResume.DeliveryMissing&&noResume.Throttle==0,"A pedal feedback mismatch requires explicit reset rather than spontaneous full-throttle retry");
        c.Reset();var reset=c.Step(Input(11,0,100/3.6));
        check(reset.Throttle<=.01&&reset.Brake==0,"Explicit reset discards all learned pedal state");
        ThrottleReceptionAndEffect(check);
        RecordedThrottleDrop(check);
        foreach(int branch in new[]{0,1,2}) {
            c.Reset();LongitudinalOutput? failed=null;
            for(int i=0;i<30;i++) {
                var input=Input(12+i*.05,branch==0?0:10,branch==0?0:100/3.6) with{ActualThrottle=0,ActualBrake=0};
                input=branch switch{0=>input with{PlannedHold=true,StopIdentity="held-gate"},1=>input with{Emergency=true},_=>input with{AccelerationSafetyLimit=-7}};
                failed=c.Step(input);
            }
            check(failed!.DeliveryMissing&&failed.Fault&&failed.Throttle==0&&failed.Brake>0,
                "Unapplied brake feedback is diagnosed in every hold/full-brake branch: "+branch);
        }
    }

    public static void RecordedThrottleDrop(Action<bool,string> check)
    {
        using var stream=typeof(LongitudinalControllerTests).Assembly.GetManifestResourceStream("MautAssistent.Tests.Fixtures.ThrottleEffectDrop.json")
            ??throw new InvalidOperationException("Recorded effective-throttle fixture is missing");
        using var fixture=JsonDocument.Parse(stream);
        var inputs=JsonSerializer.Deserialize<LongitudinalInput[]>(fixture.RootElement.GetProperty("inputs").GetRawText())!;
        check(inputs.Length==27&&inputs[0].Time==0&&inputs.All(x=>x.Epoch==1&&x.ReceivedThrottle==null&&x.ReceivedBrake==null),
            "Recorded sampled replay isolates one relative epoch and never invents missing input-pedal acknowledgements");
        var controller=new LongitudinalController();LongitudinalOutput? previous=null;
        bool noFault=true,noBrake=true,sawLimited=false,noEscalation=true;
        foreach(var input in inputs) {
            var output=controller.Step(input);
            noFault&=!output.Fault&&!output.DeliveryMissing&&!output.ThrottleDeliveryMissing;
            noBrake&=output.Brake==0;
            if(output.ThrottleEffectLimited) {
                sawLimited=true;
                noEscalation&=previous!=null&&output.Throttle<=previous.Throttle+.000001f;
            }
            previous=output;
        }
        // Private replay of the installed 2DCE product reproduces DeliveryMissing
        // at 1.702585s; saved fault is 1.764581s. Missing intermediate snapshots
        // prevent claiming an exact complete historical Tick replay.
        check(noFault&&noBrake&&sawLimited,
            "Recorded effective-throttle drop diagnoses limited effect without a false delivery fault or emergency brake");
        check(noEscalation,
            "Recorded simulation-side throttle suppression never trains a higher requested pedal during the limited-effect interval");
    }

    static void ThrottleReceptionAndEffect(Action<bool,string> check)
    {
        var controller=new LongitudinalController();LongitudinalOutput output=new(0,0,0,"");
        double t=1;
        for(int i=0;i<35;i++,t+=.05)output=controller.Step(Input(t,15) with{ReceivedThrottle=output.Throttle,ActualThrottle=output.Throttle});
        float beforeCut=output.Throttle;bool keptSteering=true;float maxDuringCut=0;
        for(int i=0;i<24;i++,t+=.05) {
            output=controller.Step(Input(t,15) with{ReceivedThrottle=output.Throttle,ActualThrottle=0,Acceleration=.3});
            keptSteering&=!output.Fault&&!output.DeliveryMissing&&output.Brake==0;
            maxDuringCut=Math.Max(maxDuringCut,output.Throttle);
        }
        check(keptSteering&&maxDuringCut<=beforeCut+1e-6&&output.Throttle>0,
            "Received gas with a 1.2-second simulated effective cut keeps steering and never learns a higher pedal or invents an emergency");
        var resumed=controller.Step(Input(t,15) with{ReceivedThrottle=output.Throttle,ActualThrottle=output.Throttle});t+=.05;
        check(!resumed.Fault&&resumed.Throttle<=beforeCut+.023,
            "Effective pedal recovery uses the normal fresh ramp, not an accumulated full-throttle jump");
        for(int i=0;i<15;i++,t+=.05)output=controller.Step(Input(t,15) with{ReceivedThrottle=0,ActualThrottle=0});
        check(output.ThrottleDeliveryMissing&&!output.Fault&&output.Throttle==0&&output.Brake==0,
            "Actual absent input becomes a latched propulsion-only warning even after a successful drive phase");
        var constrained=controller.Step(Input(t,15) with{ReceivedThrottle=0,ActualThrottle=0,AccelerationSafetyLimit=-1.2});t+=.05;
        check(constrained.ThrottleDeliveryMissing&&constrained.Throttle==0&&constrained.Brake>0&&constrained.TargetAcceleration<=-1.2,
            "Missing propulsion never suppresses a new traffic, curve or gate deceleration bound");
        var emergency=controller.Step(Input(t,15) with{Emergency=true,ReceivedThrottle=0,ActualThrottle=0});t+=.05;
        check(emergency.Brake==1&&emergency.Throttle==0,"An explicit emergency still overrides the propulsion-only warning");
        controller.Step(Input(t,15) with{Enabled=false});t+=.05;
        check(!controller.Step(Input(t,15)).ThrottleDeliveryMissing,"Manual cancellation clears the propulsion latch before a fresh start");
        controller.Reset();output=new(0,0,0,"");
        for(int i=0;i<30;i++)output=controller.Step(Input(20+i*.05,15) with{ReceivedThrottle=null,ActualThrottle=0});
        check(!output.Fault&&!output.DeliveryMissing&&output.Throttle<.2,
            "An unavailable input field cannot prove failed transport; ineffective gas still prevents upward adaptation");
    }

    // Independent longitudinal plant: bounded engine traction falls with speed,
    // brake effectiveness differs between loaded/empty, actuators lag150ms,
    // drag and actual gravity remain outside the controller. No equation calls
    // back into controller internals or assumes requested acceleration occurs.
    sealed class Plant(double drive,double braking,double pitch,double initial=0)
    {
        public double Speed=initial,Acceleration,Distance;
        public float Throttle,Brake;
        public void Step(LongitudinalOutput output,double dt) {
            Throttle+=(output.Throttle-Throttle)*(float)(1-Math.Exp(-dt/.15));
            Brake+=(output.Brake-Brake)*(float)(1-Math.Exp(-dt/.15));
            double engine=drive*Throttle*Math.Clamp((45-Speed)/30,.25,1);
            double drag=.06+.00018*Speed*Speed;
            double force=engine-braking*Brake-drag-9.81*Math.Sin(pitch);
            double next=Math.Max(0,Speed+force*dt);Acceleration=(next-Speed)/dt;
            Distance+=(Speed+next)*.5*dt;Speed=next;
        }
        public LongitudinalInput Input(double time,double target)=>new(time,1,true,false,true,Speed,Acceleration,target,
            Pitch:pitch,ActualThrottle:Throttle,ActualBrake:Brake,ReceivedThrottle:Throttle,ReceivedBrake:Brake);
    }
    public static void Dynamics(Action<bool,string> check)
    {
        foreach(var setting in new[]{(Drive:2.0,Brake:5.0,Pitch:0d,Name:"empty flat"),
            (Drive:1.0,Brake:3.2,Pitch:0d,Name:"loaded flat"),
            (Drive:1.6,Brake:3.2,Pitch:.025,Name:"loaded uphill")}) {
            var c=new LongitudinalController();var p=new Plant(setting.Drive,setting.Brake,setting.Pitch);
            bool exclusive=true;double max=0;int brakeTicks=0;
            for(int i=0;i<7000;i++) {
                var o=c.Step(p.Input((i+1)*.05,100/3.6));
                exclusive&=float.IsFinite(o.Throttle)&&float.IsFinite(o.Brake)&&(o.Throttle==0||o.Brake==0);
                if(i>6000&&o.Brake>.05)brakeTicks++;p.Step(o,.05);max=Math.Max(max,p.Speed);
            }
            check(exclusive,"Independent "+setting.Name+" dynamics always have exclusive finite pedals");
            check(p.Speed*3.6 is >97 and <103&&max*3.6<106,"Independent "+setting.Name+" truck reaches and controls100km/h: "+p.Speed*3.6);
            check(brakeTicks<60,"Independent "+setting.Name+" cruise avoids repeated substantial brake applications");
        }
        foreach(double grade in new[]{0d,-.04}) {
            var c=new LongitudinalController();var p=new Plant(1,3.2,grade,20/3.6);bool stopped=false;double maximumOverrun=0;
            for(int i=0;i<1600;i++) {
                double remaining=Math.Max(0,60-p.Distance);
                var o=c.Step(p.Input((i+1)*.05,20/3.6) with{StopDistance=remaining,StopIdentity="closed-gate"});
                p.Step(o,.05);maximumOverrun=Math.Max(maximumOverrun,p.Distance-60);if(p.Speed<.1&&p.Distance>50)stopped=true;
            }
            check(stopped&&p.Speed<.15&&maximumOverrun<1,"Loaded stop with actuator lag, grade"+grade+" reaches standstill without large overrun: distance="+p.Distance);
            var release=c.Step(p.Input(80.05,10/3.6));
            check(release.Brake==0&&!release.Holding&&release.Throttle<.05,"Open gate releases loaded stop without lingering brake or learned full throttle");
        }
        {
            var c=new LongitudinalController();var p=new Plant(1,3.2,0);double maximum=0;
            var launch=c.Step(p.Input(.05,20/3.6) with{StopDistance=100,StopIdentity="closed-gate"});
            check(launch.Throttle>0&&launch.Brake==0,"A closed gate100m away still permits a fresh controlled launch from standstill");
            p.Step(launch,.05);
            for(int i=1;i<2000;i++) {
                var o=c.Step(p.Input((i+1)*.05,20/3.6) with{StopDistance=Math.Max(0,100-p.Distance),StopIdentity="closed-gate"});
                p.Step(o,.05);maximum=Math.Max(maximum,p.Speed);
            }
            check(maximum*3.6>15&&p.Distance is >98 and <101&&p.Speed<.15,
                "Independent loaded plant approaches a distant closed gate, then stops instead of holding at its launch point: "+p.Distance);
            var release=c.Step(p.Input(100.05,10/3.6));
            check(release.Brake==0&&!release.Holding&&release.Throttle<.05,"Opening the approached gate immediately releases its own stop context");
        }
        {
            var c=new LongitudinalController();var p=new Plant(1,3.2,0,85/3.6){Throttle=.6f,Acceleration=.2};
            int brakeTicks=0;double maximum=p.Speed;
            for(int i=0;i<400;i++) {
                var o=c.Step(p.Input((i+1)*.05,80/3.6) with{CoastEligible=true});
                if(o.Brake>.01)brakeTicks++;p.Step(o,.05);maximum=Math.Max(maximum,p.Speed);
            }
            check(brakeTicks==0&&maximum*3.6<86&&p.Speed*3.6 is >79 and <82,
                "Independent loaded plant releases prior positive throttle and rolls85-to80 without a brake series: brakes="+brakeTicks+", speed="+p.Speed*3.6);
        }
        {
            var c=new LongitudinalController();var p=new Plant(1,3.2,-.04,80/3.6);double max=p.Speed;
            for(int i=0;i<2000;i++){var o=c.Step(p.Input((i+1)*.05,80/3.6) with{CoastEligible=true});p.Step(o,.05);max=Math.Max(max,p.Speed);}
            check(p.Speed*3.6 is >77 and <83&&max*3.6<86,"Loaded downhill cruise controls gravity instead of coasting into growing overspeed: "+p.Speed*3.6);
        }
    }
}
