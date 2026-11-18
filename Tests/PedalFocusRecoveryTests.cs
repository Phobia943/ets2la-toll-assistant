using MautAssistent;
using System.Text.Json;

public static class PedalFocusRecoveryTests
{
    sealed class Driver {
        public readonly LongitudinalController Controller=new();
        public readonly object Process=new();
        public double Time=1;
        public long Source=1;
        public long Epoch=1;
        public ulong Clock=1;
        public bool Publish=true;
        public LongitudinalOutput? Last;
        public PedalFocusSample Sample(bool foreground,bool ready=true)=>new((long)(Time*1000),1000,(long)(Time*1000),(long)(Time*1000),
            Source,42,Process,foreground,Clock,Clock,ready);
        public LongitudinalOutput Step(bool foreground,bool receipt=false,bool brake=false,bool park=false,PedalFocusSample? sample=null) {
            Time+=.05;Clock++;
            var input=new LongitudinalInput(Time,Epoch,true,false,true,park?0:10,0,25,
                AccelerationSafetyLimit:brake?-1.2:null,ReceivedThrottle:receipt?Last?.Throttle??0:0,
                ReceivedBrake:receipt?Last?.Brake??0:0,ActualThrottle:receipt?Last?.Throttle??0:0,
                ActualBrake:receipt?Last?.Brake??0:0,ParkingBrake:park){Focus=sample??Sample(foreground,!park)};
            Last=Controller.Step(input);
            if(Publish)Controller.AcceptFocusReturn(input.Focus,input.Focus);
            return Last;
        }
        public LongitudinalOutput Background(bool brake=false) {
            for(int i=0;i<40;i++)Step(false,brake:brake);
            return Last!;
        }
    }
    public static void Run(Action<bool,string> check)
    {
        foreach(bool brake in new[]{false,true}) {
            var d=new Driver();var suspended=d.Background(brake);
            check(suspended.FocusSuspended&&!suspended.DeliveryMissing&&!suspended.Fault&&suspended.Throttle==0,
                "Only a completely observed background receipt window becomes expected focus suspension: "+brake);
            if(brake)check(suspended.Brake>0,"Focus interruption preserves the current real negative braking bound");
            for(int i=0;i<8;i++)if(!d.Step(true,receipt:true).FocusSuspended)break;
            check(!d.Last!.FocusSuspended&&!d.Last.DeliveryMissing,
                "Advancing stable foreground resumes a current-plan attempt, not an acknowledgement or fault clear: "+brake);
            var free=d.Step(true,receipt:true);
            check(free.Brake==0&&free.Throttle>0&&free.Throttle<.1,
                "Return discards old pedal history and stale brake, with the ordinary small start ramp: "+brake);
        }
        foreach(bool brake in new[]{false,true}) {
            var d=new Driver();for(int i=0;i<40;i++)d.Step(true,brake:brake);
            check(d.Last!.DeliveryMissing&&!d.Last.FocusSuspended,"A foreground receipt failure remains a strict latch: "+brake);
            d.Background(brake);for(int i=0;i<10;i++)d.Step(true,receipt:true);
            check(d.Last!.DeliveryMissing&&d.Last.Throttle==0,
                "Tab and return cannot clear an existing throttle/brake failure: "+brake);
        }
        {
            var d=new Driver();while(d.Step(true).Throttle<=.15) { }
            d.Step(true);d.Step(true); // Begin genuine foreground mismatch before tab.
            var result=d.Background();
            check(result.DeliveryMissing&&!result.FocusSuspended,
                "A receipt window begun in foreground cannot be retrospectively reclassified by tabbing away");
        }
        {
            var d=new Driver();d.Background();d.Controller.Pause();
            check(d.Step(false).FocusSuspended,"Expected focus suspension survives SDK Pause without requiring an acknowledgement");
            var parked=d.Step(false,park:true);
            check(parked.FocusSuspended&&parked.ParkingHold&&parked.Throttle==0&&parked.Brake==0,
                "Parking-neutral zero pedals cannot erase focus suspension");
            check(d.Step(false).FocusSuspended,"Releasing parking brake while still outside game cannot resume gas");
        }
        {
            var d=new Driver();while(d.Step(true).Throttle<=.15) { }
            d.Step(true);d.Step(true);d.Controller.Pause();
            check(d.Background().DeliveryMissing,
                "Pause and its neutral pedal history cannot erase an unfinished foreground failure window");
        }
        {
            var d=new Driver();d.Background();var duplicate=d.Sample(true);
            for(int i=0;i<9;i++)d.Step(true,sample:duplicate);
            check(d.Last!.FocusSuspended,"Repeated SDK/simulation/context originals earn no stable return time");
            d.Source++;for(int i=0;i<10;i++)d.Step(true,receipt:true);
            check(d.Last!.FocusSuspended,"A source-epoch change revokes automatic return instead of borrowing the previous session");
        }
        {
            var d=new Driver();d.Background();d.Publish=false;
            for(int i=0;i<6;i++)d.Step(true,receipt:true);
            var unready=d.Sample(true,ready:false);
            check(!d.Controller.FocusCommitAllowed(d.Sample(true),unready),"A failed final return cannot borrow an expired lane/source readiness");
            var result=d.Step(true,sample:unready);
            check(result.FocusSuspended&&result.Throttle==0,
                "Without an accepted return commit the following unready Tick stays suspended");
        }
        {
            var d=new Driver();d.Background();
            for(int i=0;i<40;i++)d.Step(true); // Retry does not deliver actual gas.
            check(d.Last!.ThrottleDeliveryMissing&&!d.Last.FocusSuspended&&d.Last.Throttle==0,
                "A failed foreground attempt relatches normally; normal startup does not bypass receipt monitoring");
            d.Background();for(int i=0;i<8;i++)d.Step(true,receipt:true);
            check(d.Last!.ThrottleDeliveryMissing,"Another tab cannot launder the failed foreground attempt");
        }
        {
            var d=new Driver();for(int i=0;i<60;i++) {
                d.Step(true);d.Controller.ReduceUnpublishedPedals(0,0);
            }
            check(!d.Last!.DeliveryMissing,"Repeated vetoed/unpublished gas wishes cannot manufacture missing physical receipt");
            while(d.Step(true).Throttle<=.15) { }
            d.Step(true);d.Step(true);d.Controller.ReduceUnpublishedPedals(0,0);
            for(int i=0;i<8;i++){d.Step(true);d.Controller.ReduceUnpublishedPedals(0,0);}
            for(int i=0;i<30;i++)d.Step(true);
            check(d.Last!.ThrottleDeliveryMissing,"Reduction-only output reconciliation never clears an already genuine receipt window");
        }
        {
            var focus=new PedalFocusRecovery();var token=new object();
            PedalFocusSample S(long n,bool fg=false,object? identity=null)=>new(n,1000,n,n,1,42,identity??token,fg,(ulong)n,(ulong)n,true);
            bool brakeFault=false;
            focus.BeginStep(S(1000,true));focus.Observe(false,true,false,.1,S(1000,true));
            for(int i=1;i<=8;i++) {
                var sample=S(1000+i*50);focus.BeginStep(sample);
                focus.Observe(true,true,false,.05,sample);
                brakeFault|=focus.Observe(false,true,false,.05,sample);
            }
            check(focus.Suspended&&brakeFault,"Focus-qualified gas interruption never heals the independent foreground brake window");
            check(!focus.CanCommit(S(1500,true),S(1501,true,new object())),
                "Reused PID with another actual process identity cannot authorize a final commit");
            check(!focus.CanCommit(S(1500,true),S(1501,false)),"A final foreground-to-background change cannot authorize stale gas");
            var json=JsonSerializer.Serialize(S(1600,true));
            check(!json.Contains("ProcessIdentity",StringComparison.Ordinal),"Opaque process lifetime tokens never enter diagnostics serialization");
        }
        foreach(bool unknown in new[]{false,true}) {
            var focus=new PedalFocusRecovery();var token=new object();
            PedalFocusSample S(long n,bool fg=false)=>new(n,1000,n,n,1,42,token,fg,(ulong)n,(ulong)n,true);
            for(int i=0;i<4;i++)focus.Observe(true,true,false,.05,S(1000+i*50));
            focus.RetainPendingWindows();
            focus.Observe(true,false,false,.05,unknown?null:S(1200,true));
            bool fault=false;
            for(int i=0;i<4;i++)fault|=focus.Observe(true,true,false,.05,S(1250+i*50));
            check(fault&&!focus.Suspended,
                "A retained neutral frame still records disqualifying foreground/unknown provenance: "+unknown);
        }
        {
            var d=new Driver();d.Background();d.Publish=false;
            for(int i=0;i<6;i++)d.Step(true,receipt:true);
            var rolled=d.Sample(true) with{SdkTime=1,SimTime=1};
            check(!d.Controller.FocusCommitAllowed(d.Sample(true),rolled),
                "Fresh arrival clocks cannot authorize return using rolled-back SDK/simulation history");
            check(d.Controller.FocusSuspended,"Rejected rollback withdraws pending return immediately");
            for(int i=0;i<10;i++)d.Step(true,receipt:true);
            check(d.Controller.FocusSuspended,"An end-snapshot clock rollback remains revoked after fresh clocks recover; no implicit reset");
        }
    }
}
