using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using MautAssistent;

internal static class RigRoadControlReviewTests
{
    sealed class Fixture:IDisposable
    {
        internal readonly RollingStartIntegration.Fixture Drive;
        internal RigRuntimeObservation Observation=new(null,null,null);
        internal readonly Worker Checks=new();
        ulong sequence=2;
        internal Fixture() {
            Drive=new(withoutAcc:true,writeDiagnostic:(_,_)=>{},readRigObservation:()=>Observation);
            Drive.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false,DetailedRigProtection=false});
            Drive.F8();Drive.Probe();Drive.Set("staticRoadWorker",Checks);Frame(5);
        }
        internal void Frame(double speed) {
            Drive.Fresh(speed);Drive.Set("lastAcceptedPlanAt",Drive.Now);
            var p=Drive.Get<Vector3>("position");var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            var truck=new VehicleBody(p,q,new(-1.25f,.2f,-3),new(1.25f,4,4));
            var trailer=new VehicleBody(p+new Vector3(0,0,-7),q,new(-1.3f,.2f,-5.5f),new(1.3f,4,6.5f));
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var context=new EnvelopeContext("same-fixture-trailer",false,false,[new("trailer","","box","single","plate",trailer.Position){
                Hook=new(0,1,-5),RotationTurns=new(.5f,0,0),Wheels=[new(new(-1,.5f,3),true,false,false,true,0,0),new(new(1,.5f,3),true,false,false,true,0,0)]}]){
                    UtcMs=now,TruckHook=new(0,1,2)};
            Observation=new(context,null,null){MeasuredShape=new(1,7,sequence+=2,now,truck,true,trailer)};
        }
        internal ParkedSceneAdmission.Result[] Evaluate() {
            using var operation=Drive.Get<FaultBrake>("faultBrake").BeginOperation();
            return (ParkedSceneAdmission.Result[])Drive.Call("EvaluateStaticRoadChecks")!;
        }
        internal float Limit() {
            using var operation=Drive.Get<FaultBrake>("faultBrake").BeginOperation();
            return (float)Drive.Call("LimitForParkedScene",float.NaN)!;
        }
        internal void DeliverWithoutDrain(double speed,bool active=true,bool paused=false) {
            var physics=typeof(TollPlugin).GetNestedType("PhysicsObservation",BindingFlags.NonPublic)!;
            var telemetry=typeof(TollPlugin).GetNestedType("TelemetryFrame",BindingFlags.NonPublic)!;
            var reading=Activator.CreateInstance(physics,1000000UL,1000000UL,0f,0f,0f,0f,0f,0f,0f)!;
            var frame=Activator.CreateInstance(telemetry,Drive.Now,1000000UL,active,paused,speed,Drive.Get<Vector3>("position"),.5d,reading)!;
            telemetry.GetProperty("Origin")!.SetValue(frame,Stopwatch.GetTimestamp());
            telemetry.GetProperty("Envelope")!.SetValue(frame,Observation.Context);
            Drive.Set("latestDeliveredTelemetry",frame);
        }
        public void Dispose(){Drive.F9();Drive.Call("StopDiagnostics");}
    }
    // Only the completed process result is substituted. The real production
    // Control/Tick and capture/final-commit paths still execute in this fixture.
    sealed class Worker:IStaticRoadChecks
    {
        internal double Ahead=25;internal Action? DuringFinal;
        public string Status=>"isolated completed trailer-only witness";
        public StaticRoadWorkerClient.Result? Read(RouteTrace route,Vector3 p,double h,long e)=>null;
        public StaticRoadWorkerClient.Result? Read(RouteTrace route,Vector3 p,double h,long e,RigRoadObservation? rig)
            =>rig==null?null:new(1,1,e,Stopwatch.GetTimestamp(),route,"europe",[new(18,route.ProjectForTracking(p).Along+Ahead){Rig=true}],false,null){OriginalRig=rig};
        public bool IsCurrent(StaticRoadWorkerClient.Result r,RouteTrace route,long e) {
            var action=DuringFinal;DuringFinal=null;action?.Invoke();
            return ReferenceEquals(r.Route,route)&&r.Epoch==e&&StaticRoadWorkerProtocol.Fresh(r.OriginalTimestamp,Stopwatch.GetTimestamp());
        }
        public bool IsRigCurrent(StaticRoadWorkerClient.Result r,RigRoadObservation? rig)
            =>rig!=null&&r.OriginalRig is { } old&&RigRoadPrediction.MatchesObserved(old,rig,r.Route,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public void Reset(){}
    }
    internal static void PositiveAndWithdrawal(Action<bool,string> check)
    {
        using var f=new Fixture();var checks=f.Evaluate();
        check(checks is [{Clear:false,AcuteDistance:null,Kind:SceneCheckKind.ConservativeOccupancy}]&&checks[0].StopDistance is >9 and <11,
            "Actual standard-mode Control consumes a trailer-only result as an ordinary planned stop");
        f.Drive.State.DesiredSpeed=20;f.Frame(5);f.Drive.Plugin.Tick();
        var output=f.Drive.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(output.Input.AccelerationSafetyLimit<0&&output.Output.Throttle==0&&output.Output.Brake>0&&!f.Drive.Plugin.FaultBraking,
            "The actual own-controller Tick brakes for a rig hit on an ordinary road without latching emergency braking");
        f.Frame(0);f.Checks.Ahead=15;f.Limit();
        check(f.Drive.Get<float>("scenePedalFloor")>=.35f,"The reached rig boundary holds with the existing ordinary stop controller");
        f.Observation=new(null,null,null);
        check(float.IsNaN(f.Limit())&&f.Drive.Get<float>("scenePedalFloor")==0&&!f.Drive.Get<SceneStopHold>("sceneHold").Holding&&!f.Drive.Plugin.FaultBraking,
            "Missing current body data withdraws this optional hold/floor without creating a new emergency or clearance certificate");
        f.Frame(0);f.Limit();check(f.Drive.Get<float>("scenePedalFloor")>=.35f,"Fresh supported evidence can establish a new ordinary boundary");
        f.Frame(20d/3.6+.01);
        check(float.IsNaN(f.Limit())&&f.Drive.Get<float>("scenePedalFloor")==0&&!f.Drive.Plugin.FaultBraking,
            "Actual speed outside the model withdraws the old rig constraint");
    }
    internal static void CommitRaces(Action<bool,string> check)
    {
        foreach(string change in new[]{"manual","route","delivered-speed","delivered-paused","delivered-inactive"}) {
            using var f=new Fixture();check(f.Evaluate().Length==1,"Rig commit fixture has a positive hit: "+change);f.Frame(5);
            f.Checks.DuringFinal=()=>{
                if(change=="manual")f.Drive.F9();
                else if(change=="route")f.Drive.Set("safetyTrace",new RouteTrace([f.Drive.Point,f.Drive.Point+new Vector3(1,0,100)],[],["replacement"]));
                else f.DeliverWithoutDrain(change=="delivered-speed"?7:5,change!="delivered-inactive",change=="delivered-paused");
            };
            check(f.Evaluate().Length==0&&!f.Drive.Plugin.FaultBraking,"Late provider change cannot commit the old rig hold: "+change);
        }
    }
}
