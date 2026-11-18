using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text;
using MautAssistent;
using TruckLib;

internal static class StaticRoadWorkerClientReviewTests
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Fixture:IDisposable
    {
        internal IFileSystem? Files=new NoReads();
        internal readonly StaticRoadWorkerClient Client;
        internal readonly Vector3 Position;
        internal readonly Queue<Action> Scheduled=[];
        internal Fixture(Vector3? position=null) {
            Position=position??new(0,0,30);
            Client=new(()=>Files,()=>new("europe",true),
                paths:_=>[Path.GetFullPath("never-opened.scs")],schedule:Scheduled.Enqueue);
        }
        internal T ReadField<T>(string name)=>(T)typeof(StaticRoadWorkerClient).GetField(name,Hidden)!.GetValue(Client)!;
        internal StaticRoadWorkerClient.Result Seed(RouteTrace trace,long epoch=1,double horizon=40)
        {
            Client.Read(trace,Position,horizon,epoch);
            var job=ReadField<object>("pending");
            T Field<T>(string name)=>(T)job.GetType().GetProperty(name)!.GetValue(job)!;
            var result=new StaticRoadWorkerClient.Result(Field<long>("Generation"),Field<long>("RouteId"),epoch,
                Field<long>("Timestamp"),trace,"europe",[new(77,65)],false,null) {
                    QueryHorizon=horizon,RouteEpoch=ReadField<long>("routeEpoch")
                };
            typeof(StaticRoadWorkerClient).GetField("completed",Hidden)!.SetValue(Client,result);
            return result;
        }
        public void Dispose()=>Client.Dispose();
    }
    static RouteTrace Route(float offset=0,string identity="road42:lane0",bool prune=false)
    {
        Vector3[] points=prune?[new(offset,0,20),new(offset,0,60),new(offset,0,100)]:
            [new(offset,0,0),new(offset,0,20),new(offset,0,60),new(offset,0,100)];
        return new(points,null,Enumerable.Repeat(identity,points.Length-1));
    }

    internal static void OriginalHitProgress(Action<bool,string> check)
    {
        var drive=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        drive.Set("options",new TollOptions {CurvePreviewEnabled=false,BriskOpenRoads=false});
        drive.F8();drive.Probe();drive.Fresh(5);
        using var f=new Fixture(drive.Point);
        var original=Route(offset:drive.Point.X);drive.Set("safetyTrace",original);
        long epoch=drive.Get<FaultBrake>("faultBrake").ControlEpoch;
        var response=f.Seed(original,epoch);drive.Set("staticRoadWorker",f.Client);
        ParkedSceneAdmission.Result[] Checks() {
            drive.Fresh(5);drive.Set("lastAcceptedPlanAt",drive.Now);
            using var operation=drive.Get<FaultBrake>("faultBrake").BeginOperation();
            return (ParkedSceneAdmission.Result[])drive.Call("EvaluateStaticRoadChecks")!;
        }
        try {
            var first=Checks();
            check(first.Length==1&&Math.Abs(first[0].StopDistance-10)<.01,
                $"An original response places the static stop15m before its absolute collision progress: {string.Join(',',first.Select(x=>x.StopDistance))}; pose={drive.Point}; {drive.Plugin.StaticRoadStatus}");
            drive.Set("safetyTrace",Route(offset:drive.Point.X,prune:true));
            var pruned=Checks();
            check(pruned.Length==1&&Math.Abs(pruned[0].StopDistance-10)<.01,"The actual plugin uses the response's original progress after the current trace prunes20m behind the truck");
            drive.Set("position",drive.Point+new Vector3(0,0,30));
            check(Checks().Length==0,"The actual plugin drops a still-fresh cached collision that the reference point has already passed");
            drive.F9();
            check(Checks().Length==0&&!drive.Plugin.FaultBraking,"F9 revokes the same cached response without a technical fault brake");
        } finally {drive.Call("StopDiagnostics");}
    }

    internal static void EquivalentRefresh(Action<bool,string> check)
    {
        using var f=new Fixture();var original=Route();var response=f.Seed(original);
        var identical=Route();
        check(!ReferenceEquals(original,identical),"The native route-republication fixture must create a different trace object");
        var read=f.Client.Read(identical,f.Position,40,1);
        check(ReferenceEquals(read,response)&&read.OriginalTimestamp==response.OriginalTimestamp&&
            ReferenceEquals(read.Route,original)&&f.Client.IsCurrent(read,identical,1),
            "A ready original worker response survives a new publication of the same directed corridor without restamping");
        var pruned=Route(prune:true);
        read=f.Client.Read(pruned,f.Position,40,1);
        check(ReferenceEquals(read,response)&&f.Client.IsCurrent(response,pruned,1)&&
            original.ProjectForTracking(f.Position).Along==30&&pruned.ProjectForTracking(f.Position).Along==10,
            "Pruning behind the current segment keeps the original response and its different absolute progress coordinates");
        check(f.Scheduled.Count==1,"Repeated route deliveries do not start multiple workers or a live process in the fixture");
    }

    internal static void InFlightLongerHorizon(Action<bool,string> check)
    {
        using var f=new Fixture();
        RouteTrace Sample(bool change)=>new(Enumerable.Range(0,201).Select(i=>new Vector3(change&&i>90?(i-90)*.02f:0,0,i)),
            null,Enumerable.Repeat("road42:lane0",200));
        var original=Sample(false);var response=f.Seed(original,horizon:100);
        // Simulate the actual asynchronous boundary: the long original query
        // is running and has not published yet when the planner changes.
        typeof(StaticRoadWorkerClient).GetField("completed",Hidden)!.SetValue(f.Client,null);
        typeof(StaticRoadWorkerClient).GetField("activeHorizon",Hidden)!.SetValue(f.Client,100d);
        f.Client.Read(Sample(true),f.Position,40,1);
        f.Client.Read(Sample(false),f.Position,40,1);
        typeof(StaticRoadWorkerClient).GetField("completed",Hidden)!.SetValue(f.Client,response);
        check(!f.Client.IsCurrent(response,original,1),
            "Shrinking a new request horizon cannot hide an intervening change inside an older in-flight query's longer corridor");
    }

    internal static void RevocationAndLifecycle(Action<bool,string> check)
    {
        foreach(string change in new[]{"identity","geometry","seam","epoch","source","expiry","reset"}) {
            using var f=new Fixture();var original=Route();var response=f.Seed(original);
            check(f.Client.IsCurrent(response,original,1),"A valid worker response starts current: "+change);
            RouteTrace current=original;long epoch=1;
            switch(change) {
                case "identity": current=Route(identity:"road42:lane1");break;
                case "geometry": current=Route(offset:1);break;
                case "seam":
                    // Put a short directed seam ahead of the current pose.
                    current=new([new(0,0,0),new(0,0,20),new(0,0,40),new(0,0,41),new(0,0,100)],
                        [3],Enumerable.Repeat("road42:lane0",4));break;
                case "epoch":epoch=2;break;
                case "source":f.Files=new NoReads();break;
                case "expiry":Thread.Sleep(220);break;
                case "reset":f.Client.Reset();break;
            }
            check(f.Client.Read(current,f.Position,40,epoch)==null&&!f.Client.IsCurrent(response,current,epoch),
                "Changed or expired worker evidence cannot create a static brake: "+change);
            if(change is "identity" or "geometry" or "seam") {
                // A still-pending response must not regain authority merely
                // because the planner later selects an earlier path again.
                f.Client.Read(Route(),f.Position,40,1);
                typeof(StaticRoadWorkerClient).GetField("completed",Hidden)!.SetValue(f.Client,response);
                check(!f.Client.IsCurrent(response,original,1),"Returning to an old route cannot revive a pre-change response: "+change);
            }
            if(change=="reset") {
                var fresh=f.Seed(original);
                check(fresh.Generation!=response.Generation&&f.Client.IsCurrent(fresh,original,1),
                    "A reset helper can acquire a fresh generation after the plugin is enabled again");
            }
        }
    }
    sealed class NoReads:IFileSystem
    {
        public char DirectorySeparator=>'/';
        public bool FileExists(string p)=>throw new InvalidOperationException();
        public bool DirectoryExists(string p)=>throw new InvalidOperationException();
        public IList<string> GetFiles(string p)=>throw new InvalidOperationException();
        public byte[] ReadAllBytes(string p)=>throw new InvalidOperationException();
        public string ReadAllText(string p)=>throw new InvalidOperationException();
        public string ReadAllText(string p,Encoding e)=>throw new InvalidOperationException();
        public Stream Open(string p)=>throw new InvalidOperationException();
        public string GetParent(string p)=>throw new InvalidOperationException();
    }
}
