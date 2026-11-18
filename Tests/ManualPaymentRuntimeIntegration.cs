using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Controls;
using ETS2LA.Game.Data;
using ETS2LA.State;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using SdkGate=ETS2LA.Game.SDK.Semaphore;
using SdkGateType=ETS2LA.Game.SDK.SemaphoreType;

// Product RunManualPayment/PaymentCommitAllowed, authored map/DTOs and a blocked
// injected storage worker only. No Init/OnEnable, SDK/MMF or output singleton.
public static class ManualPaymentRuntimeIntegration
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    internal sealed class Fixture:IDisposable
    {
        public readonly ApplicationState State=(ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationState));
        public readonly TollPlugin Plugin;
        public readonly PaymentJournal Journal;
        public readonly PlannedPathData Path;
        public readonly ManualPaymentRouteZone Zone;
        public readonly List<(long Attempt,long Epoch,long SourceEpoch,long IssuedAt,long SourceAt)> Leases=[];
        public readonly List<long> Cancels=[];
        public readonly ManualResetEventSlim WriterEntered=new(),WriterRelease=new();
        public Action<long>? DuringLease;
        public bool Foreground=true;
        public bool BlockWriter;
        public long Epoch=7,SourceEpoch=19;
        public byte[]? Bytes;
        ulong simulation=1_000_000;
        public double Now=>Get<Stopwatch>("clock").Elapsed.TotalSeconds;
        public Vector3 Point=>Zone.Zone.Center;
        public Fixture(bool blockWriter)
        {
            BlockWriter=blockWriter;
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            NavCurve Line(float a,float b,int gate=-1)=>new(){StartPosition=new(0,0,a),EndPosition=new(0,0,b),Length=b-a,
                StartRotation=rotation,EndRotation=rotation,SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
            var curves=new List<NavCurve>{Line(0,20),Line(20,40,1),Line(40,80)};
            curves[0].NextLines.Add(1);curves[1].NextLines.Add(2);
            var descriptor=new PrefabDescriptor{NavCurves=curves};
            descriptor.Semaphores.Add(new(){SemaphoreId=1,Position=new(2,0,39.6f)});
            var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
            prefab.Prefab=new(){Uid=982011};prefab.Descriptor=descriptor;
            var route=new PrefabPath(prefab,new(){Uid=982012},new(){Uid=982013},curves.ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            var item=new PlannedItem{item=prefab,prefabPath=route,startNode=route.StartNode,endNode=route.EndNode,direction=Direction.Forward,laneIndex=0};
            Path=new(){HasError=false,PlannedPath=[item]};
            ManualStopZone stop=new(10.6,18.6){Trigger=new(new(0,0,25),Vector3.UnitZ,4,1.75)};
            Plugin=new(()=>State,(_,_)=>{},_=>{},readAcc:()=>null,readPlanner:()=>null,readLaneAssist:()=>null,
                readPayments:_=>new Dictionary<int,TollPayment>{{1,TollPayment.Manual}},readClearance:_=>true,
                readManualStop:(_,_)=>stop,writeDiagnostic:(_,_)=>{});
            State.DrivingMode=DrivingMode.FullSelfDriving;State.EnableAssists=true;Plugin._IsRunning=true;
            Set("options",new TollOptions());Set("active",true);Set("path",Path);
            Set("paymentPathGeneration",1L);Set("paymentAppliedPathGeneration",1L);
            var router=Get<TollRouter>("router");router.Discover(Path,new(0,0,5),[]);
            Zone=router.ManualPaymentZone(8)??throw new InvalidOperationException("Authored runtime payment zone did not validate");
            Journal=new(()=>Bytes,data=> {
                WriterEntered.Set();
                if(BlockWriter&&!WriterRelease.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException("Blocked test writer");
                Bytes=data.ToArray();
            });
            var load=Journal.LoadAsync();
            if(!load.Wait(TimeSpan.FromSeconds(2))||!load.Result.Available)throw new InvalidOperationException("Isolated journal failed to load");
            Set("paymentJournal",Journal);Set("paymentJournalLoad",load);
            Set("paymentLeaseSink",(Action<long,long,long,long,long>)((attempt,epoch,source,issued,origin)=> {
                Leases.Add((attempt,epoch,source,issued,origin));DuringLease?.Invoke(attempt);
            }));
            Set("paymentCancelSink",(Action<long>)(epoch=>Cancels.Add(epoch)));
            Set("paymentContextReader",(Func<GameInputContextSnapshot>)(()=>new(Stopwatch.GetTimestamp(),Foreground?42:99,42,
                Foreground?GameInputContextStatus.GameForeground:GameInputContextStatus.OtherForeground,"Authored test input context")));
            Fresh();
        }
        public void Set(string field,object? value)=>typeof(TollPlugin).GetField(field,Hidden)!.SetValue(Plugin,value);
        public T Get<T>(string field)=>(T)typeof(TollPlugin).GetField(field,Hidden)!.GetValue(Plugin)!;
        public object? Call(string method,params object?[] args)=>typeof(TollPlugin).GetMethod(method,Hidden)!.Invoke(Plugin,args);
        public bool Commit(long attempt)=>(bool)Call("PaymentCommitAllowed",attempt)!;
        public SdkGate Gate(int id=1,int state=1,Vector3? anchor=null)=>new(){id=id,state=state,type=SdkGateType.GATE,position=anchor??Zone.Anchor};
        public void Fresh(Vector3? position=null,double speed=0,bool active=true,bool paused=false,long? telemetryOrigin=null,long? gateOrigin=null,SdkGate[]? samples=null)
        {
            long qpc=Stopwatch.GetTimestamp();double now=Now;simulation+=50_000;
            var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",BindingFlags.NonPublic)!;
            var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",BindingFlags.NonPublic)!;
            var gateType=typeof(TollPlugin).GetNestedType("GateFrame",BindingFlags.NonPublic)!;
            var reading=Activator.CreateInstance(physics,simulation,simulation,0f,0f,0f,0f,0f,0f,0f)!;
            var frame=Activator.CreateInstance(telemetry,now,simulation,active,paused,speed,position??Point,.5d,reading)!;
            telemetry.GetProperty("Origin")!.SetValue(frame,telemetryOrigin??qpc);
            var gates=samples??new[]{Gate()};
            var gateFrame=Activator.CreateInstance(gateType,now,gates)!;
            gateType.GetProperty("Origin")!.SetValue(gateFrame,gateOrigin??qpc);gateType.GetProperty("Epoch")!.SetValue(gateFrame,SourceEpoch);
            Set("appliedTelemetry",frame);Set("latestDeliveredTelemetry",frame);Set("latestDeliveredGates",gateFrame);
            Set("gates",gates);Set("gatesOrigin",gateOrigin??qpc);Set("gatesSourceEpoch",SourceEpoch);
            Set("sdk",active);Set("paused",paused);Set("speed",speed);Set("position",position??Point);Set("headingTurns",.5d);
            foreach(string field in new[]{"telemetryAt","gatesAt","pathAt","gameTimeChangedAt"})Set(field,now);
            var live=Gate();
            Set("barrierBinding",new BarrierBindingResult(true,true,true,new(RouteMatcher.Key(live),14.6,BarrierState.Closed),live,14.6,null,TollDecision.Idle("Fixture live own gate"),false));
        }
        public void Run()=>Call("RunManualPayment",Epoch,Stopwatch.GetTimestamp());
        public Task<PaymentJournalAck> Reservation()
        {
            var request=Get<object>("paymentReservation");
            return (Task<PaymentJournalAck>)request.GetType().GetProperty("Work")!.GetValue(request)!;
        }
        public void AwaitDurable()
        {
            if(!Reservation().Wait(TimeSpan.FromSeconds(2))||!Reservation().Result.Applied)
                throw new InvalidOperationException("Fixture reservation did not become durable");
        }
        public void UntilFirstLease()
        {
            var watch=Stopwatch.StartNew();
            while(Leases.Count==0&&watch.Elapsed.TotalSeconds<2) {Fresh();Run();Thread.Sleep(25);}
            if(Leases.Count==0)throw new InvalidOperationException("Actual runtime did not emit a payment lease: "+Plugin.PaymentStatus);
        }
        public void Dispose()
        {
            WriterRelease.Set();Journal.Dispose();Call("StopDiagnostics");
            // Test cleanup may await its injected worker OUTSIDE product locks;
            // production Stop/Dispose itself remains nonblocking.
            if(!Journal.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)))throw new TimeoutException("Isolated journal cleanup");
            WriterEntered.Dispose();WriterRelease.Dispose();
        }
    }

    public static void Run(Action<bool,string> check)
    {
        BlockedAndCommit(check);
        CancelledBeforeAck(check);
        UnappliedRoute(check);
    }
    static void BlockedAndCommit(Action<bool,string> check)
    {
        using var f=new Fixture(true);
        var watch=Stopwatch.StartNew();f.Run();watch.Stop();
        check(watch.ElapsedMilliseconds<200&&f.WriterEntered.Wait(TimeSpan.FromSeconds(2))&&!f.Reservation().IsCompleted&&f.Leases.Count==0,
            "Actual RunManualPayment enqueues storage without blocking the driving thread or activating before durable ACK");
        Thread.Sleep(275);f.Fresh();watch.Restart();f.Run();watch.Stop();
        check(watch.ElapsedMilliseconds<200&&f.Leases.Count==0&&!f.Reservation().IsCompleted,
            "A storage writer blocked beyond250ms cannot block another real payment update or manufacture a lease");
        f.WriterRelease.Set();f.AwaitDurable();f.Fresh();f.Run();
        check(f.Leases.Count==0,"A late durable ACK alone is not a new activation decision or a completed stopped window");
        bool checkedCommit=false;
        f.DuringLease=attempt=> {
            if(checkedCommit)return;checkedCommit=true;
            CommitVetoes(f,attempt,check);
        };
        f.UntilFirstLease();
        var attempts=f.Leases.Select(l=>l.Attempt).Distinct().ToArray();
        check(checkedCommit&&attempts.Length==1&&f.Leases.All(l=>l.Epoch==7&&l.SourceEpoch==19&&l.SourceAt>0&&l.SourceAt<=l.IssuedAt),
            "Durable ACK plus a NEW advancing fresh stopped window emits one opaque attempt with original source time");
        for(int i=0;i<8;i++){Thread.Sleep(25);f.Fresh();f.Run();}
        check(f.Leases.Select(l=>l.Attempt).Distinct().Count()==1&&f.Get<ManualPayment>("manualPayment").ConsumedGateIds.Length==1,
            "Repeated actual runtime updates cannot turn the consumed gate into multiple attempts");
    }
    static void CommitVetoes(Fixture f,long attempt,Action<bool,string> check)
    {
        check(f.Commit(attempt),"The actual payment commit predicate accepts the exact current stopped, focused own-gate attempt");
        check(!f.Commit(attempt+1),"An unrelated opaque attempt cannot borrow the current payment guard");
        f.Foreground=false;check(!f.Commit(attempt),"Window focus loss vetoes the actual final payment commit");f.Foreground=true;
        f.Fresh(position:f.Point+new Vector3(3,0,0));check(!f.Commit(attempt),"Lateral drift outside the exact asset zone vetoes final commit");
        f.Fresh(paused:true);check(!f.Commit(attempt),"A newly delivered SDK pause vetoes final commit");
        f.Fresh(active:false);check(!f.Commit(attempt),"A newly inactive SDK frame vetoes final commit");
        f.Fresh(speed:.2);check(!f.Commit(attempt),"Actual movement after the stopped decision vetoes final commit");
        long now=Stopwatch.GetTimestamp();
        f.Fresh(telemetryOrigin:now-Stopwatch.Frequency);check(!f.Commit(attempt),"A fresh arrival cannot renew stale original telemetry at final commit");
        f.Fresh(gateOrigin:now-Stopwatch.Frequency);check(!f.Commit(attempt),"A fresh telemetry sample cannot renew a stale original own-gate observation");
        f.Fresh(telemetryOrigin:Stopwatch.GetTimestamp()+Stopwatch.Frequency);check(!f.Commit(attempt),"Future original telemetry cannot authorize interaction");
        f.Fresh(samples:[f.Gate(id:2,state:3)]);check(!f.Commit(attempt),"An adjacent open gate cannot stand in for the selected own gate");
        f.Fresh(samples:[f.Gate(anchor:f.Zone.Anchor+new Vector3(3,0,0))]);check(!f.Commit(attempt),"A spatially different gate with the same numeric ID cannot supply the own component");
        f.Fresh(samples:[f.Gate(state:3)]);check(!f.Commit(attempt),"An already open own gate suppresses the semantic activation pulse");
        f.Fresh(samples:[f.Gate(),f.Gate(id:2,state:3,anchor:f.Zone.Anchor+new Vector3(3,0,0))]);
        check(f.Commit(attempt),"An unrelated neighbor's state does not invalidate the exact current own closed-gate interaction");
        f.Set("paymentPathGeneration",2L);check(!f.Commit(attempt),"Arrival of a new but unapplied route invalidates the previous payment guard immediately");
        f.Set("paymentPathGeneration",1L);f.Set("paymentAppliedPathGeneration",2L);
        check(!f.Commit(attempt),"A guard cannot borrow a different applied-route generation");
        f.Set("paymentAppliedPathGeneration",1L);f.State.EnableAssists=false;
        check(!f.Commit(attempt),"F9/manual assist cancellation vetoes the final interaction independently of source freshness");
        f.State.EnableAssists=true;f.Fresh();
    }
    static void CancelledBeforeAck(Action<bool,string> check)
    {
        using var f=new Fixture(true);f.Run();
        check(f.WriterEntered.Wait(TimeSpan.FromSeconds(2)),"Cancellation regression reaches the actual asynchronous reservation writer");
        var old=f.Reservation();
        f.Call("OnCancel",null,new ControlChangeEventArgs(false,null!));f.Fresh();f.Run();
        check(!f.State.EnableAssists&&f.Leases.Count==0,"Actual F9 handling revokes the pending payment without waiting on disk");
        f.Epoch++;f.State.EnableAssists=true;f.Set("active",true);
        f.WriterRelease.Set();check(old.Wait(TimeSpan.FromSeconds(2))&&old.Result.Durable,
            "The cancelled epoch's journal reservation may finish durably without becoming activation authority");
        for(int i=0;i<24;i++){f.Fresh();f.Run();Thread.Sleep(25);}
        check(f.Leases.Count==0&&f.Get<ManualPayment>("manualPayment").ConsumedGateIds.Contains(f.Zone.Zone.Key.GateIdentity),
            "F8/new epoch after F9 cannot activate from a late old ACK or retry the persisted consumed booth");
        check(f.Journal.LoadAsync().GetAwaiter().GetResult().Entries.Count==1,
            "The late old reservation remains in durable storage even though no semantic activation was emitted");
    }
    static void UnappliedRoute(Action<bool,string> check)
    {
        using var f=new Fixture(false);f.Set("paymentPathGeneration",2L);f.Fresh();f.Run();
        check(f.Get<object?>("paymentReservation")==null&&f.Leases.Count==0,
            "A newly arrived unapplied route cannot reserve or prepare payment under the older selected-zone decision");
        f.Set("paymentAppliedPathGeneration",2L);f.Fresh();f.Run();
        check(f.Get<object?>("paymentReservation")!=null,"Matching actual applied-route generation permits a new independently validated reservation");
    }
}
