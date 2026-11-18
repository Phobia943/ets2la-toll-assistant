using System.Diagnostics;
using System.Numerics;
using MautAssistent;
using ETS2LA.Game.SDK;
using Semaphore=ETS2LA.Game.SDK.Semaphore;

internal static class MapGateControlReviewTests
{
    static Semaphore Live(MapGateCandidate gate,int state)=>new(){id=0,type=SemaphoreType.GATE,position=gate.Anchor,
        rotation=new(gate.Rotation.W,gate.Rotation.X,gate.Rotation.Y,gate.Rotation.Z),state=state};
    internal static void StatesAndProtocol(Action<bool,string> check)
    {
        var gate=MapGateApproach.Bind(MapGateApproachReviewTests.Area(),MapGateApproachReviewTests.Straight(),0)!;
        foreach(int state in new[]{0,1,2,4})check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[Live(gate,state)]).StopDistance is >34.9 and <35.1,
            "Only state3 opens the bound gate; closed/moving/unknown state creates an ordinary stop: "+state);
        check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[]).StopDistance!=null,"Missing SDK actor cannot clear an explicitly bound gate");
        check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[Live(gate,3)]).StopDistance==null,"Exactly bound open3 removes this gate constraint");
        check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[Live(gate,3),Live(gate,1)]).StopDistance!=null,"Contradictory duplicate actor state cannot certify opening");
        var rotated=Live(gate,3);rotated.rotation=Quaternion.Identity;
        check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[rotated]).StopDistance==null,
            "A correctly anchored open actor does not require an unproven map-to-actor rotation convention");
        rotated.state=1;check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[Live(gate,3),rotated]).StopDistance!=null,
            "A contradictory closed actor at the exact anchor cannot be discarded because its rotation differs");
        var neighbor=Live(gate with{Anchor=gate.Anchor+new Vector3(6,0,0)},3);
        check(TollPlugin.EvaluateStandaloneMapGates([gate],0,[neighbor]).StopDistance!=null,"Another gate with reused ID0 cannot lend its opening");
        string session=Guid.NewGuid().ToString("N");long at=Stopwatch.GetTimestamp();
        var points=MapGateApproachReviewTests.Straight().Select(p=>new StaticRoadWorkerProtocol.Point(p.Position.X,p.Position.Y,p.Position.Z,p.Direction.X,p.Direction.Y,p.Direction.Z,p.Along,0,false)).ToArray();
        var query=new StaticRoadWorkerProtocol.Query(StaticRoadWorkerProtocol.Version,session,1,1,1,1,at,new("europe",true),[Path.GetFullPath("never-opened.scs")],0,0,0,0,["gate-road"],points);
        var reply=new StaticRoadWorkerProtocol.Reply(StaticRoadWorkerProtocol.Version,session,1,1,1,1,at,"europe",[],false,null){Gates=[gate]};
        var roundtrip=StaticRoadWorkerProtocol.Decode<StaticRoadWorkerProtocol.Reply>(StaticRoadWorkerProtocol.Encode(reply,8192),8192);
        check(roundtrip.Gates[0]==gate&&StaticRoadWorkerProtocol.ValidReply(roundtrip,query,at),"Gate pose/progress survives bounded IPC and original request validation");
        check(!StaticRoadWorkerProtocol.ValidReply(reply with{Gates=[gate,gate]},query,at)&&
            !StaticRoadWorkerProtocol.ValidReply(reply with{Gates=[gate with{StopAlong=gate.PlaneAlong}]},query,at)&&
            !StaticRoadWorkerProtocol.ValidReply(reply with{Session=Guid.NewGuid().ToString("N")},query,at)&&
            !StaticRoadWorkerProtocol.ValidReply(reply,query,at+Stopwatch.Frequency),"Duplicate gate IDs, forged stop plane, wrong process session and expired origin are rejected");
        int providers=0;using var client=new StaticRoadWorkerClient(()=>{providers++;return null;},()=>{providers++;return null;},schedule:_=>{});
        void Set(string name,object value)=>typeof(StaticRoadWorkerClient).GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(client,value);
        var originalRoute=new RouteTrace([Vector3.Zero,new(0,0,120)],null,["gate-road"]);
        var local=new StaticRoadWorkerClient.Result(1,1,1,Stopwatch.GetTimestamp(),originalRoute,"europe",[],false,null){Gates=[gate]};
        Set("generation",1L);Set("manualEpoch",1L);Set("completed",local);
        check(client.IsLocallyCurrent(local)&&providers==0,"Final gate check reads existing volatile proof only, without callbacks or scheduling");
        Set("completed",local with{});check(!client.IsLocallyCurrent(local),"Completion replacement revokes the original local gate proof");
        Set("completed",local);Set("manualEpoch",2L);check(!client.IsLocallyCurrent(local),"A changed manual epoch revokes local gate proof");
        Set("manualEpoch",1L);var stale=local with{OriginalTimestamp=Stopwatch.GetTimestamp()-Stopwatch.Frequency};Set("completed",stale);
        check(!client.IsLocallyCurrent(stale),"The local endcheck never renews the original expired response");
        Set("completed",local);client.Reset();check(!client.IsLocallyCurrent(local)&&providers==0,"Reset invalidates the local proof without source callbacks");
    }
    sealed class Worker:IStaticRoadChecks
    {
        internal bool Present=true,NoGate;internal Action? DuringFinal;internal double OldSeconds;
        internal Vector3 AnchorOffset;
        internal ulong Uid=91;internal float ForwardDistance=20;
        internal MapGateCandidate? Gate;
        public string Status=>"synthetic standalone gate";
        public StaticRoadWorkerClient.Result? Read(RouteTrace route,Vector3 position,double horizon,long epoch)
        {
            if(!Present)return null;double at=route.ProjectForTracking(position).Along;
            Gate=new(Uid,position+new Vector3(3,0,ForwardDistance)+AnchorOffset,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),at+ForwardDistance,at+ForwardDistance-5);
            return new(1,1,epoch,Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*OldSeconds),route,"europe",[],false,null){Gates=NoGate?[]:[Gate]};
        }
        public bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace trace,long epoch){var action=DuringFinal;DuringFinal=null;action?.Invoke();return Present&&ReferenceEquals(result.Route,trace)&&result.Epoch==epoch;}
        public bool IsLocallyCurrent(StaticRoadWorkerClient.Result result)=>Present&&StaticRoadWorkerProtocol.Fresh(result.OriginalTimestamp,Stopwatch.GetTimestamp());
        public void Reset(){Present=false;}
    }
    internal static void ActualControl(Action<bool,string> check)
    {
        var approach=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});var far=new Worker{ForwardDistance=105};
        try {
            approach.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});approach.F8();approach.Probe();approach.Set("staticRoadWorker",far);
            approach.Traffic=()=>{object?[] args=[Tuple.Create(Stopwatch.GetTimestamp(),new SemaphoreData{semaphores=far.Gate==null?[]:[Live(far.Gate,1)]}),new List<object>(),0L,null];
                return (TrafficSpeedDecision)approach.Call("EvaluateTrafficLightsFrame",args)!;};
            approach.State.DesiredSpeed=20;approach.Fresh(0);approach.Plugin.Tick();var output=approach.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance is >99.9 and <100.1&&output.Output.Throttle>0&&output.Output.Brake==0,
                "A freshly bound closed gate100m away remains normally approachable from standstill; only missing bindings impose the separate no-gas wait");
        } finally {approach.F9();approach.Call("StopDiagnostics");}
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});var worker=new Worker();int state=1;
        try {
            f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});f.F8();f.Probe();f.Fresh(8);f.Set("staticRoadWorker",worker);
            object? lastLease=null;
            TrafficSpeedDecision Read(){var frame=Tuple.Create(Stopwatch.GetTimestamp(),new SemaphoreData{semaphores=worker.Gate==null?[]:[Live(worker.Gate,state)]});
                object?[] args=[frame,new List<object>(),0L,null];var decision=(TrafficSpeedDecision)f.Call("EvaluateTrafficLightsFrame",args)!;lastLease=args[3];return decision;}
            f.Traffic=Read;f.State.DesiredSpeed=20;f.Plugin.Tick();var output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance is >14.9 and <15.1&&output.Output.Throttle==0&&output.Output.Brake>0&&!f.Plugin.FaultBraking,
                "Actual controller Tick brakes normally for a standalone gate without any toll prefab or emergency latch");
            worker.OldSeconds=1;f.Fresh(8);f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance is >14.9 and <15.1&&output.Output.Throttle==0&&output.Output.TargetAcceleration<=-2.6&&output.Output.Brake>.5&&!f.Plugin.FaultBraking,
                "After helper expiry, current pose/route rederive the static negative stop15m ahead; at8m/s the actual controller retains sufficient braking instead of only -.35");
            worker.OldSeconds=0;state=3;f.Fresh(5);f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance==null&&f.Get<HashSet<TollPlugin.MapGateWatchKey>>("pendingMapGates").Count==0,"Fresh directed binding plus exactly open3 clears the closed-gate pending wait");
            worker.Present=false;f.Set("position",f.Point+new Vector3(0,0,5));f.Fresh(8);f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Output.Throttle==0&&output.Input.StopDistance is >9.9 and <10.1&&output.Output.TargetAcceleration<=-4&&output.Output.Brake>.85,
                "Open3 then missing worker/unknown new closed state10m ahead preserves physically meaningful braking at8m/s without reusing an old distance/opening lease");
            f.Set("position",f.Point);
            worker.Present=true;
            state=4;f.Fresh(8);f.Plugin.Tick();worker.Present=false;f.Fresh(5);f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance is >14.9 and <15.1&&output.Output.Throttle==0&&output.Input.AccelerationSafetyLimit<0,
                "Bound but unknown gate state followed by source loss also preserves the spatial stop and no-gas bound in the combined light/gate policy");
            worker.Present=true;state=3;f.Fresh(5);f.Plugin.Tick();
            check(lastLease!=null&&f.Get<HashSet<TollPlugin.MapGateWatchKey>>("pendingMapGates").Count==0,"A new exact opening resolves the unknown-source pending state");
            worker.DuringFinal=()=>worker.Present=false;
            check(!(bool)f.Call("MapGateLeaseCurrent",lastLease)!,"Source revoked inside the final provider callback rejects the original gate lease");
            worker.Present=true;state=3;f.Fresh(5);f.Plugin.Tick();
            var original=f.Get<RouteTrace>("safetyTrace");
            worker.DuringFinal=()=>f.Set("safetyTrace",new RouteTrace([f.Point,f.Point+new Vector3(6,0,80)],null,["different-lane"]));
            check(!(bool)f.Call("MapGateLeaseCurrent",lastLease)!,"A route changed by the last provider callback cannot retain gate authority");
            f.Set("safetyTrace",original);f.Fresh(5);
            worker.AnchorOffset=new Vector3(6,0,0);state=3;f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Output.Throttle==0&&output.Input.AccelerationSafetyLimit<0,
                "A new source's reused UID with a different anchor cannot release the previously observed gate approach");
            worker.NoGate=true;f.Set("position",f.Point+new Vector3(0,0,22));f.Fresh(5);f.Plugin.Tick();
            check(f.Get<HashSet<TollPlugin.MapGateWatchKey>>("pendingMapGates").Count==0,
                "Fresh current source and route after actually crossing the watched gate plane remove the approach wait without a permanent brake");
            worker.NoGate=false;worker.AnchorOffset=Vector3.Zero;state=1;f.Fresh(8);f.Plugin.Tick();
            worker.Uid=92;worker.ForwardDistance=105;f.Fresh(8);f.Plugin.Tick();output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
            check(output.Input.StopDistance is >14.9 and <15.1&&output.Output.TargetAcceleration<=-2.6&&output.Output.Throttle==0,
                "A newly visible farther closed gate100m away cannot replace the nearer missing approach's rederived15m stop");
            state=1;worker.DuringFinal=()=>f.F9();f.Fresh(5);f.Plugin.Tick();
            check(!f.State.EnableAssists&&!f.Plugin.FaultBraking,"F9 during final gate source validation returns manual authority without a fault brake");
            var old=new RouteTrace([Vector3.Zero,new(0,0,80)],null,["gate-road"]);
            var same=new RouteTrace([Vector3.Zero,new(0,0,80)],null,["gate-road"]);
            check(TollPlugin.MapGatePassed(old,20,same,new(0,0,22))&&!TollPlugin.MapGatePassed(old,20,same,new(0,0,19))&&
                !TollPlugin.MapGatePassed(old,20,new RouteTrace([Vector3.Zero,new(0,0,80)],null,["different-road"]),new(0,0,22)),
                "Approach waits end only after passing the plane on the same current directed corridor; a neighbor/new route does not clear them");
            check(TollPlugin.MapGateRemainingStop(old,20,same,new(0,0,5)) is >9.9 and <10.1&&
                TollPlugin.MapGateRemainingStop(old,20,new RouteTrace([Vector3.Zero,new(6,0,80)],null,["different-road"]),new(0,0,5))==null,
                "Retained plane distance is recomputed from the current position and cannot transfer to another route");
        } finally {f.F9();f.Call("StopDiagnostics");}
    }
}
