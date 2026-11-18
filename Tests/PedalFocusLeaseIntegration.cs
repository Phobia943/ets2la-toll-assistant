using System.Diagnostics;
using System.Reflection;
using ETS2LA.Game.Output;
using MautAssistent;

public static class PedalFocusLeaseIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(monitorSteering:true,withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Call("StopDiagnostics");f.State.EnableAssists=true;
        var commandType=typeof(TollPlugin).GetNestedType("SteeringCommand",BindingFlags.NonPublic)!;
        object Command(double lease,double age) {
            long now=Stopwatch.GetTimestamp(),origin=now-(long)(age*Stopwatch.Frequency);
            var c=Activator.CreateInstance(commandType,[0d,.1f])!;
            foreach(var (name,value) in new (string,object)[]{("Epoch",7L),("IssuedTicks",origin),("ReceivedTicks",origin),("LeaseSeconds",lease)})
                commandType.GetProperty(name)!.SetValue(c,value);
            f.Set("lastSteeringCommand",c);return c;
        }
        Command(.05,.01);
        check((bool)f.Call("FocusLaneLeaseCurrent",7L)!,"Automatic foreground return accepts an actually current shorter50ms lane lease");
        Command(.05,.07);
        check(!(bool)f.Call("FocusLaneLeaseCurrent",7L)!,"Automatic foreground return rejects an expired50ms lane even below the150ms watchdog deadline");
        Command(.1,.147);
        check(!(bool)f.Call("FocusLaneLeaseCurrent",7L)!,"A Healthy-range147ms old command is not a valid100ms lane channel for return");
        foreach(double bad in new[]{0d,-.1,double.NaN,double.PositiveInfinity}) {
            Command(bad,.01);
            check(!(bool)f.Call("FocusLaneLeaseCurrent",7L)!,"Unknown/nonpositive lane lease grants no return authority: "+bad);
        }
        var current=Command(.1,0);
        check(!(bool)f.Call("FocusLaneLeaseCurrent",8L)!,"A fresh lane command from an old assist epoch cannot authorize return");
        foreach(float invalid in new[]{0f,-.1f,float.NaN,float.PositiveInfinity}) {
            f.Call("OnControlOutput",new ControlEvent {ChannelDefinition=new(){Id="LaneAssist.SteeringCorrection",Timeout=invalid},
                Variables=new(){steering=.2f}});
            check(ReferenceEquals(current,f.Get<object>("lastSteeringCommand")),
                "The actual lane callback rejects malformed timeout without replacing existing evidence: "+invalid);
        }
    }
    public static void ActualTick(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        f.Call("StopDiagnostics");f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
        f.F8();f.Probe();f.State.DesiredSpeed=25;
        int foreground=42;
        string image=Path.Combine(Path.GetTempPath(),"maut-authored-focus","eurotrucks2.exe");
        using var context=new GameInputContext([image],pid=>new GameProcessIdentity(pid,"eurotrucks2",image,()=>true),
            ()=>1,_=>foreground,Stopwatch.GetTimestamp);
        check(context.RefreshProcessIdentity(42),"Actual Tick focus fixture verifies one authored process before capture");
        f.Set("inputContext",context);f.Set("inputGamePid",42);
        var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",BindingFlags.NonPublic)!;
        var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",BindingFlags.NonPublic)!;
        ulong simulated=1_000_000;
        void Deliver() {
            simulated+=50_000;
            var reading=Activator.CreateInstance(physics,simulated,simulated,0f,0f,0f,0f,0f,0f,0f)!;
            var frame=Activator.CreateInstance(telemetry,f.Now,simulated,true,false,5d,f.Point,.5d,reading)!;
            telemetry.GetProperty("Origin")!.SetValue(frame,Stopwatch.GetTimestamp());
            f.Set("incomingTelemetry",frame);f.Set("latestDeliveredTelemetry",frame);f.Call("DrainInputFrames");f.Fresh(5);
        }
        Deliver();Thread.Sleep(5);Deliver();f.Plugin.Tick();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{focusFixtureSources=true,
            sourceEpoch=typeof(TollPlugin).GetMethod("CaptureSourceEpoch",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null),
            gameAssembly=typeof(ETS2LA.Game.Output.GameOutput).Assembly.Location,
            contextToken=context.Capture(42).ProcessIdentity!=null,
            focus=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot").Input.Focus}));
        check(f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot").Published,
            "The actual injected Tick begins with advancing physics and a real ReadInputContext witness");
        f.Plugin.BeforeLongitudinalCommit=()=>foreground=99;
        bool neutral=true,noFault=true;int neutralPublications=0;
        for(int i=0;i<28;i++) {
            foreground=42;Thread.Sleep(15);Deliver();int before=f.Outputs.Count;f.Plugin.Tick();
            var snapshot=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            double issuedAge=(Stopwatch.GetTimestamp()-snapshot.IssuedAt)/(double)Stopwatch.Frequency;
            bool originalExpired=issuedAge>.2||snapshot.DynamicsOrigin is long origin&&
                (Stopwatch.GetTimestamp()-origin)/(double)Stopwatch.Frequency>.2;
            bool safeExpiredRejection=!snapshot.Published&&originalExpired&&f.Outputs.Count==before;
            if(!snapshot.Published||snapshot.Output.Throttle!=0)Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{
                focusTick=i,snapshot.Published,snapshot.Output,snapshot.Input.Focus,snapshot.Input.RouteKnown,snapshot.DynamicsOrigin,
                snapshot.InputContext,foreground,issuedAge,originalExpired,safeExpiredRejection}));
            if(snapshot.Published&&snapshot.Output.Throttle==0)neutralPublications++;
            neutral &=snapshot.Published?snapshot.Output.Throttle==0:safeExpiredRejection;
            noFault &=!snapshot.Output.DeliveryMissing&&!f.Plugin.FaultBraking;
        }
        check(neutral&&neutralPublications>0,"A real final focus switch publishes zero gas; an expired original may only be rejected without any event");
        check(noFault,"Repeated actual Tick focus-veto outputs do not become phantom missing positive receipt");
        var controller=f.Get<LongitudinalController>("longitudinal");
        void SeedPriorReturnPending() {
            // Explicit authored prior focus history, not historical game
            // telemetry. The actual next Tick and its final publication are
            // left intact, including current physics, context and route.
            foreground=42;var token=context.Capture(42).ProcessIdentity!;
            long source=(long)typeof(TollPlugin).GetMethod("CaptureSourceEpoch",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
            var recovery=new PedalFocusRecovery();long now=Stopwatch.GetTimestamp();
            PedalFocusSample S(double ago,bool fg,ulong index) {
                long stamp=now-(long)(ago*Stopwatch.Frequency);
                return new(stamp,Stopwatch.Frequency,stamp,stamp,source,42,token,fg,index,index,true);
            }
            for(int i=0;i<8;i++)recovery.Observe(true,true,false,.05,S(.7-i*.05,false,(ulong)i+1));
            for(int i=0;i<5;i++)recovery.BeginStep(S(.25-i*.05,true,(ulong)i+9));
            check(recovery.ReturnPending,"The authored prior focus history reached pending return, not an acknowledged game receipt");
            typeof(LongitudinalController).GetField("focusRecovery",Hidden)!.SetValue(controller,recovery);
        }
        SeedPriorReturnPending();Deliver();f.Plugin.Tick();
        check(controller.FocusSuspended&&!controller.FocusReturnPending&&
            f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot").Output.Throttle==0,
            "The actual final OtherForeground veto revokes pending return immediately");
        f.Plugin.BeforeLongitudinalCommit=null;foreground=42;Thread.Sleep(20);Deliver();f.Plugin.Tick();
        check(controller.FocusSuspended&&f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot").Output.Throttle==0,
            "The following foreground Tick cannot borrow pre-veto return stability");
        for(int i=0;i<12;i++){Thread.Sleep(25);Deliver();f.Plugin.Tick();}
        check(!controller.FocusSuspended&&controller.FocusReturnPending,
            "The DI Tick cannot finish return without a raw host-accepted epoch/issue stamp; the isolated host replay covers acceptance");
        f.Plugin.BeforeLongitudinalCommit=()=>Thread.Sleep(225);
        foreground=42;Deliver();f.Plugin.Tick();
        var rejected=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(!rejected.Published&&!rejected.Output.DeliveryMissing,
            "A completely expired/unpublished actual Tick cannot renew authority or clear a receipt fault");
        check((float)typeof(LongitudinalController).GetField("throttle",Hidden)!.GetValue(controller)! ==0,
            "Completely discarded Tick reconciles internal expected gas to zero without resetting the controller");
        f.Plugin.BeforeLongitudinalCommit=null;f.Call("StopDiagnostics");
    }
}
