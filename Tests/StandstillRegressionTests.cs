using System.Numerics;
using System.Text.Json;
using MautAssistent;

public static class StandstillRegressionTests
{
    sealed record Sample(double T,double RawSpeedKph,float X,float Z,double Heading,ulong SimStamp,int Gear)
    {
        public double Speed=>RawSpeedKph/3.6;
        public Vector3 Position=>new(X,0,Z);
    }
    public static void Run(Action<bool,string> check)
    {
        using var stream=typeof(StandstillRegressionTests).Assembly.GetManifestResourceStream("MautAssistent.Tests.Fixtures.StandstillJitter.json")
            ??throw new InvalidOperationException("Recorded stand-jitter fixture is missing");
        using var fixture=JsonDocument.Parse(stream);
        var samples=JsonSerializer.Deserialize<Sample[]>(fixture.RootElement.GetProperty("samples").GetRawText(),
            new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        check(samples.Length==240&&samples[0].T==0&&samples.Min(s=>s.Speed)<-.01&&samples.Max(s=>s.Speed)>0,
            "Stand fixture preserves both measured speed signs and the original 0.01m/s boundary crossing");
        check(!fixture.RootElement.GetProperty("historicalParkingBrakeAtFaultKnown").GetBoolean(),
            "Historical parking continuity is unknown; fresh parking confirmation below is explicitly counterfactual");

        // Existing authored curve geometry, independently completed once. It is
        // rotated to the recorded initial heading and placed at relative zero.
        // Historical zero targets/CurvePending are deliberately NOT replayed as
        // a new geometric plan, nor is this synthetic road called the real road.
        var rotation=Matrix4x4.CreateRotationY((float)(samples[0].Heading*2*Math.PI+Math.PI));
        var anchor=CurveSpeedTests.Point(90);
        var points=Enumerable.Range(0,525).Select(i=>Vector3.Transform(CurveSpeedTests.Point(i)-anchor,rotation)).ToArray();
        var route=new RouteTrace(points,null,Enumerable.Repeat("synthetic-stand-curve",points.Length-1));
        var profile=CurveSpeedProfile.Build(route,Vector3.Zero);
        check(profile.Count>0&&profile.HasFullCoverage(route,Vector3.Zero,50),
            "A completed authored curve with explicit route identity exists before recorded jitter is evaluated");

        var evidence=new ReverseMotionEvidence();
        var parked=new LongitudinalController();var unparked=new LongitudinalController();
        bool allReady=true,noRollback=true,parkNeutral=true,unparkedForward=true;
        double largestDebt=0;LongitudinalOutput? lastUnparked=null;
        for(int i=0;i<samples.Length;i++) {
            var sample=samples[i];
            bool rollback=evidence.Observe(new(sample.SimStamp,sample.T,1,sample.Position,sample.Heading,sample.Speed,true));
            noRollback&=!rollback;largestDebt=Math.Max(largestDebt,evidence.BackwardMetres);
            var curve=profile.Evaluate(sample.Position,sample.Speed,1.2);
            allReady&=curve!=null&&profile.HasFullCoverage(route,sample.Position,50);
            if(curve==null)continue;
            double acceleration=0;
            if(i>0)SimulationAcceleration.TryMeasure(samples[i-1].SimStamp,sample.SimStamp,samples[i-1].Speed,sample.Speed,out acceleration);
            double? limit=float.IsFinite(curve.MaxAcceleration)?curve.MaxAcceleration:null;
            // Only speed/pose/stamp/gear come from the recording. The route,
            // positive 20 km/h desired speed, and fresh parking state are test
            // premises; no unrecorded pedal acknowledgement is manufactured.
            var input=new LongitudinalInput(sample.T,1,true,false,true,sample.Speed,acceleration,
                Math.Min(20/3.6,curve.AllowedSpeed),limit,ReverseGear:sample.Gear<0,RollbackDetected:rollback);
            var held=parked.Step(input with{ParkingBrake=true});
            parkNeutral&=held.ParkingHold&&!held.Fault&&!held.DeliveryMissing&&held.Throttle==0&&held.Brake==0;
            lastUnparked=unparked.Step(input with{ParkingBrake=false});
            unparkedForward&=!lastUnparked.ParkingHold&&!lastUnparked.Fault&&!lastUnparked.ManualRequired&&lastUnparked.Brake==0;
        }
        check(allReady,"Every original signed-speed sample retains the completed synthetic curve; none fabricates CurvePending");
        check(noRollback&&!evidence.Confirmed&&largestDebt<.002,
            "Original alternating quantized poses do not accumulate fictional rollback from signed-speed jitter");
        check(parkNeutral,"Counterfactually fresh confirmed parking brake keeps all recorded jitter inputs pedal-neutral without a reverse fault");
        check(unparkedForward&&lastUnparked is {Throttle:>0},
            "Separate parking-false counterfactual keeps the completed curve and forward control without a fake pending/reverse brake pulse");
    }
}
