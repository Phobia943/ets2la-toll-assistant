using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;

internal static class StaticRoadControlReviewTests
{
    sealed class Fixture:IDisposable
    {
        internal readonly RollingStartIntegration.Fixture Drive=new(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        internal IFileSystem? Files=new NoReads();
        internal Action? DuringFinalRead;
        internal readonly StaticRoadSceneSource Source;
        int reads;
        internal Fixture()
        {
            Drive.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
            Drive.F8();Drive.Probe();Drive.Fresh(5);
            var p=Drive.Point+new Vector3(0,0,25);
            var triangle=new StaticTriangle(p+new Vector3(-.5f,0,0),p+new Vector3(.5f,0,0),p+new Vector3(0,3,0));
            var geometry=new RigStaticGeometry([triangle]);
            var queue=new Queue<Action>();
            Source=new(()=>{if(++reads==2){var action=DuringFinalRead;DuringFinalRead=null;action?.Invoke();}return Files;},
                ()=>new("europe",true),request=>new(request.Generation,"europe",request.Center,300,
                    [new(77,geometry,p+new Vector3(-.5f,0,0),p+new Vector3(.5f,3,0))],0,null),queue.Enqueue,_=>["isolated-no-reads"]);
            Source.Read(Drive.Point);queue.Dequeue()();Drive.Set("staticRoadWorker",new FixtureWorker(Source));
        }
        internal ParkedSceneAdmission.Result[] Checks()
        {
            reads=0;Drive.Fresh(5);Drive.Set("lastAcceptedPlanAt",Drive.Now);
            using var operation=Drive.Get<FaultBrake>("faultBrake").BeginOperation();
            return (ParkedSceneAdmission.Result[])Drive.Call("EvaluateStaticRoadChecks")!;
        }
        internal float Limit(double speed=5)
        {
            reads=0;Drive.Fresh(speed);Drive.Set("lastAcceptedPlanAt",Drive.Now);
            using var operation=Drive.Get<FaultBrake>("faultBrake").BeginOperation();
            return (float)Drive.Call("LimitForParkedScene",float.NaN)!;
        }
        public void Dispose(){Drive.F9();Drive.Call("StopDiagnostics");Source.Reset();}
    }
    // Only test fixtures perform geometry in-process. Production always uses
    // the separately packaged, pinned helper and has no mesh-query fallback.
    sealed class FixtureWorker(StaticRoadSceneSource source):IStaticRoadChecks
    {
        StaticRoadSceneSource.Snapshot? snapshot;
        Vector3 position;
        public string Status=>source.Status;
        public StaticRoadWorkerClient.Result? Read(RouteTrace trace,Vector3 own,double horizon,long epoch)
        {
            position=own;snapshot=source.Read(own);
            var corridor=StaticRoadCorridor.Create(trace,own,horizon);
            if(snapshot==null||corridor==null)return null;
            var hits=snapshot.Bodies.Select(b=>(Body:b,Check:b.Geometry.CheckNominal(corridor)))
                .Where(x=>!x.Check.Clear&&x.Check.Issue==null&&x.Check.ConflictAlong.HasValue)
                .Select(x=>new StaticRoadWorkerProtocol.Hit(x.Body.Uid,x.Check.ConflictAlong!.Value)).ToArray();
            return new(snapshot.Generation,1,epoch,System.Diagnostics.Stopwatch.GetTimestamp(),trace,snapshot.MapName,hits,false,null);
        }
        public bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace trace,long epoch)=>
            snapshot!=null&&source.IsCurrent(snapshot,position)&&ReferenceEquals(result.Route,trace)&&result.Epoch==epoch;
        public void Reset()=>source.Reset();
    }
    internal static void PositiveAndUnknown(Action<bool,string> check)
    {
        using var f=new Fixture();var checks=f.Checks();
        check(checks is [{Clear:false,AcuteDistance:null,Kind:SceneCheckKind.ConservativeOccupancy}]&&
            checks[0].StopDistance is >=9 and <=10,"A positive static model creates only a nominal planned stop 15m before the face");
        check(f.Drive.Get<PathLib.PlannedPathData>("path").PlannedPath.All(p=>p.prefabPath==null),"The positive fixture contains no toll prefab");
        check(float.IsFinite(f.Limit())&&!f.Drive.Plugin.FaultBraking,
            "Ordinary roads reach static scene limiting without a toll prefab and without a latched emergency");
        f.Drive.State.DesiredSpeed=20;f.Drive.Fresh(5);f.Drive.Plugin.Tick();
        var output=f.Drive.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(output.Input.AccelerationSafetyLimit<0&&output.Output.Throttle==0&&output.Output.Brake>0&&!f.Drive.Plugin.FaultBraking,
            "The actual own-controller Tick applies a positive static wall as ordinary braking without a toll prefab");
        f.Drive.Set("position",f.Drive.Point+new Vector3(0,0,20));
        check(f.Limit(0)<=-.6f&&f.Drive.Get<SceneStopHold>("sceneHold").Holding&&!f.Drive.Plugin.FaultBraking,
            "A reached nominal static boundary uses an ordinary hold rather than emergency braking");
        f.Files=null;
        check(f.Checks().Length==0&&float.IsNaN(f.Limit(0))&&!f.Drive.Plugin.FaultBraking&&
            !f.Drive.Get<SceneStopHold>("sceneHold").Holding&&f.Drive.Get<float>("scenePedalFloor")==0,
            "An unknown source removes the static constraint without manufacturing a brake or clearance certificate");
    }
    internal static void Revocation(Action<bool,string> check)
    {
        foreach(string change in new[]{"source","route","manual"}) {
            using var f=new Fixture();
            check(f.Checks().Length==1,"Revocation fixture begins with a concrete accepted static obstacle: "+change);
            f.DuringFinalRead=()=>{
                if(change=="source")f.Files=new NoReads();
                if(change=="route")f.Drive.Set("safetyTrace",new RouteTrace([f.Drive.Point,f.Drive.Point+new Vector3(5,0,100)],null,["replacement-road"]));
                if(change=="manual")f.Drive.F9();
            };
            var stale=f.Checks();
            check(stale.Length==0,"A change during final source validation cannot return old-route static authority: "+change);
            check(!f.Drive.Plugin.FaultBraking,"Revocation of optional static evidence cannot trigger fault braking: "+change);
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
