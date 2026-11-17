using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
namespace MautAssistent;

internal static class RigControlIntegration
{
    sealed class Fixture
    {
        internal readonly Vector3 Origin=new(33544,0,12345);
        internal readonly RollingStartIntegration.Fixture Driver;
        internal readonly PlannedPathData Plan;
        internal readonly PlannedItem Item;
        internal readonly TollModelEvidence Evidence;
        internal RigRuntimeObservation Observation=null!;
        internal bool Dynamic=true;
        internal SceneBody[] Bodies=[];
        internal Action<int>? DuringRead;
        internal int Reads;
        long sequence=2,sceneSequence=2,lastUtc;
        readonly object assets=new();
        readonly IReadOnlyList<WheelSample> wheels=Array.AsReadOnly(new[]{
            new WheelSample(new(-1,.5f,3),true,false,false,true,0,0),new WheelSample(new(1,.5f,3),true,false,false,true,0,0),
            new WheelSample(new(-1,.5f,4.3f),true,false,false,true,0,0),new WheelSample(new(1,.5f,4.3f),true,false,false,true,0,0)});
        internal readonly RouteTrace Trace;
        internal Fixture(bool detailed=true)
        {
            var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            var curve=new NavCurve {StartPosition=Vector3.Zero,EndPosition=new(0,0,200),Length=200,
                StartRotation=rotation,EndRotation=rotation,SemaphoreId=1,AllowedVehicles=AllowedVehicles.AllVehicles};
            var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
            prefab.Prefab=new(){Uid=55,Model="fixture",Variant="default"};prefab.Descriptor=new(){NavCurves=[curve]};
            var path=new PrefabPath(prefab,new Node{Uid=551},new Node{Uid=552},[curve],Direction.Forward,
                Matrix4x4.CreateTranslation(Origin),Vector3.Zero);
            Item=new(){item=prefab,startNode=path.StartNode,endNode=path.EndNode,prefabPath=path,direction=Direction.Forward,laneIndex=0,targetLaneIndex=0};
            Plan=new(){HasError=false,PlannedPath=[Item]};
            string identity=RouteMatcher.ItemIdentity(Item);
            Trace=new([Origin+new Vector3(0,0,-100),Origin,Origin+new Vector3(0,0,200),Origin+new Vector3(0,0,400)],[],["approach",identity,"exit"]);
            var faces=new List<StaticTriangle>();
            foreach(float x in new[]{-1.7f,1.7f}) {
                faces.Add(new(new(x,.3f,-50),new(x,5,-50),new(x,5,250)));
                faces.Add(new(new(x,.3f,-50),new(x,5,250),new(x,.3f,250)));
            }
            var geometry=new RigStaticGeometry(faces);var files=new ModelClearanceIntegration.StubFiles();
            Evidence=new(()=>files,null,_=>new(new Dictionary<int,TollPayment>{{1,TollPayment.Automatic}},new bool[1]) {
                HasDynamicParked=Dynamic,StaticGeometry=geometry},()=> (4.5f,3.4f));
            Evidence.Prefetch(prefab);
            if(!SpinWait.SpinUntil(()=>Evidence.Diagnose(prefab).Completed,TimeSpan.FromSeconds(2)))throw new Exception("Fixture model worker did not complete");
            Driver=new(readLaneAssist:()=>null,readRigObservation:()=>{DuringRead?.Invoke(++Reads);return Observation;},runtimeModels:Evidence);
            Driver.Set("options",new TollOptions {DetailedRigProtection=detailed});
            Driver.State.EnableAssists=true;Driver.Set("active",true);Driver.Set("path",Plan);Driver.Set("safetyTrace",Trace);
            Driver.Set("driveAction",DriveAction.Drive);Driver.Set("takeoverUntil",-100d);
            Frame(40,0);
        }
        internal void Frame(float nativeZ,double speed,double phase=.016,bool sameNative=false,bool contact=true)
        {
            SpinWait.SpinUntil(()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>lastUtc,TimeSpan.FromMilliseconds(30));
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();lastUtc=now;
            long nativeAt=sameNative?Observation.Envelope!.Shape.UtcMs:now-(long)(phase*1000);
            if(!sameNative)sequence+=2;
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            var truck=new VehicleBody(Origin+new Vector3(0,0,nativeZ),q,new(-1.54f,.2f,-4),new(1.54f,4,4));
            var trailer=new VehicleBody(truck.Position+new Vector3(0,0,-7),q,new(-1.3f,.2f,-5.5f),new(1.3f,4,8));
            var shape=sameNative?Observation.Envelope!.Shape:new VehicleGeometry(321,44,(ulong)sequence,nativeAt,truck,true,trailer);
            var delta=new Vector3(0,0,(float)(speed*(now-nativeAt)/1000d));
            var context=new EnvelopeContext("one reported single chain, actual fixed wheels, cargo proof",false,false,[new("fixture","","tank","single","fixture",shape.Trailer!.Position+delta) {
                Hook=new(0,1,-5),RotationTurns=new(.5f,0,0),Wheels=Array.AsReadOnly(wheels.Select(w=>w with{OnGround=contact}).ToArray())}]) {
                UtcMs=now,TruckHook=new(0,1,2)};
            var envelope=new PredictionEnvelope(shape,4.5f,3.4f,context.Identity,assets,now);
            sceneSequence+=2;ulong clock=(ulong)(1000000+sceneSequence*10000);
            var scene=new SceneSnapshot(321,44,(uint)sceneSequence,now,clock,clock,1,clock,shape.Truck.Position+delta,59,
                (uint)Bodies.Length,1,double.PositiveInfinity,Bodies,[new(55,3)]);
            Observation=new(context,envelope,scene){MeasuredShape=shape};Reads=0;
            Driver.Set("position",shape.Truck.Position+delta);Driver.Set("headingTurns",.5d);Driver.Fresh(speed);
            var physics=typeof(TollPlugin).GetField("physicsObservation",BindingFlags.NonPublic|BindingFlags.Instance)!;
            physics.SetValue(Driver.Plugin,Activator.CreateInstance(physics.FieldType,clock,clock,0f,0f,0f,0f,0f,0f,0f));
            Driver.Set("safetyTrace",Trace);
        }
        internal float Limit()=> (float)Driver.Call("LimitForParkedScene",.35f)!;
        internal SceneBody Body(float x,float z,bool known=true)=>new(901,known?2u:0u,0,Origin+new Vector3(x,2,z),Quaternion.Identity,new(1,2,2),901);
    }
    internal static void MovingAndRecovery(Action<bool,string> assert)
    {
        var f=new Fixture();f.Limit();f.Frame(40,20/3.6);
        assert(f.Limit()>.3f&&!f.Driver.Plugin.FaultBraking,"The actual nonempty toll Control path passes a moving 3.08m rig through 3.4m walls at FR coordinates with 16ms sources");
        f.Frame(40,20/3.6,sameNative:true);
        assert(f.Limit()>.3f,"The same native capture with a newer SCS phase retains correct old model parameters");
        f.Bodies=[f.Body(3,50)];f.Frame(40.05f,20/3.6);
        assert(f.Limit()>.3f&&!f.Driver.Plugin.FaultBraking,"A physically free neighbouring car at 3m cannot become an emergency merely through planning/motion reserves");
        // A different newly observed actor, not the previous car teleporting
        // one metre sideways in one frame (which is real motion evidence).
        f.Bodies=[f.Body(4,50,false) with{Id=902,GroupId=902}];f.Frame(40.1f,20/3.6);
        assert(f.Limit()>.3f,"A current unknown-motion car in the neighbouring lane does not globally stop the actual Control path");
        f.Frame(40.2f,20/3.6,contact:false);
        assert(f.Limit()>.3f,"A brief contact-confidence bounce retains the unchanged monitored axle geometry");
        f.Driver.Call("ResetRigEvaluation");f.Bodies=[f.Body(0,45)];f.Frame(40,0,phase:0);
        assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f&&!f.Driver.Plugin.FaultBraking,
            "A real current lane blocker produces an ordinary physical standstill hold without an emergency latch");
        f.Frame(40,0,phase:0);assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f,"The ordinary hold remains physically applied while the body remains");
        f.Bodies=[];f.Frame(40,0,phase:0);
        assert(f.Limit()>.3f&&f.Driver.Get<float>("scenePedalFloor")==0&&f.Driver.State.EnableAssists,
            "Renewed empty scoped evidence releases the actual hold without F9 or F8");
        f.Frame(40,0,phase:0);f.Observation=f.Observation with{Scene=null};
        assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f,"Locally missing source produces a normal hold inside the toll rather than false clearance");
        f.Frame(40,0,phase:0);
        assert(f.Limit()>.3f&&f.Driver.Get<float>("scenePedalFloor")==0,"Restoring the local source automatically releases that normal hold");
        var acute=new Fixture();acute.Limit();acute.Bodies=[acute.Body(0,50)];acute.Frame(40,20/3.6);
        assert(acute.Limit()==-5&&acute.Driver.Plugin.FaultBraking,"An actual measured box on the imminent truck path retains the independent emergency stop");
    }
    internal static void CancellationAndSpeed(Action<bool,string> assert)
    {
        var f=new Fixture();f.Limit();f.Frame(40,0,phase:0);
        var brake=f.Driver.Get<FaultBrake>("faultBrake");
        using(var operation=brake.BeginOperation()) {
            f.DuringRead=n=>{if(n==3)f.Driver.F9();};
            f.Limit();
            assert(!f.Driver.State.EnableAssists&&!f.Driver.Plugin.Active&&f.Driver.Get<float>("scenePedalFloor")==0&&!brake.OperationCurrent,
                "F9 during the real runtime source/geometry transaction invalidates its output generation and physical floor");
        }
        var fast=new Fixture();fast.Frame(-60,100/3.6);
        var checks=(ParkedSceneAdmission.Result[])fast.Driver.Call("EvaluateRigChecks",(object)new[]{fast.Item})!;
        assert(checks.Any(c=>!c.RequiresPhysicalHold&&c.AccelerationLimit is >=-2 and <0)&&!checks.Any(c=>c.HoldKey.StartsWith("scene-evidence")&&c.StopDistance==0),
            "A fast early approach requests ordinary 20km/h deceleration instead of a zero-distance missing-model hold");
    }
    internal static void StandardPolicy(Action<bool,string> assert)
    {
        var f=new Fixture(detailed:false);
        void UnknownCargo(float z=40,double speed=0) {
            f.Frame(z,speed,phase:0);
            // This is the reported Yacht failure class: no accepted accessory
            // envelope and no axle proof, but a fresh raw truck and local scene.
            f.Observation=f.Observation with{Context=null,Envelope=null};
        }
        UnknownCargo();
        assert(f.Limit()>.3f&&!f.Driver.Plugin.FaultBraking&&f.Driver.Get<float>("scenePedalFloor")==0,
            "Default standard runtime permits a scoped empty toll despite absent cargo and axle evidence");
        var profile=((float Height,float Width))f.Driver.Call("ReadPlanningProfile")!;
        assert(profile==(TollOptions.StandardLaneHeight,TollOptions.StandardLaneWidth)&&!f.Observation.MeasuredShape!.CargoCoverageKnown,
            "Standard routing declares nominal dimensions without claiming measured cargo clearance");
        f.Bodies=[f.Body(4,50,false)];UnknownCargo(40,20/3.6);
        assert(f.Limit()>.3f&&!f.Driver.Plugin.FaultBraking,"Unknown-motion neighbouring car does not block standard runtime");
        f.Bodies=[f.Body(0,45)];UnknownCargo();
        assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f&&!f.Driver.Plugin.FaultBraking,
            "Real lane obstruction still applies an ordinary physical hold in standard mode");
        f.Bodies=[];UnknownCargo();
        assert(f.Limit()>.3f&&f.Driver.Get<float>("scenePedalFloor")==0,
            "A removed obstruction releases the standard hold automatically");
        UnknownCargo();f.Observation=f.Observation with{Scene=null};
        assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f,
            "Missing actual scene remains a normal stop, independently of missing cargo");
        UnknownCargo();assert(f.Limit()>.3f&&f.Driver.Get<float>("scenePedalFloor")==0,"Fresh scene recovery releases hold without restart");
        var gate=new Gate("standard-closed",8,BarrierState.Closed);
        var controller=f.Driver.Get<TollController>("controller");
        f.Driver.Set("candidate",gate);
        f.Driver.Set("decision",controller.Step(new(0,0,true,true,gate)));
        UnknownCargo();
        assert(f.Limit()<0&&f.Driver.Get<float>("scenePedalFloor")>=.35f&&!f.Driver.Plugin.FaultBraking,
            "Closed live gate keeps its ordinary hold even when unknown cargo no longer blocks the scene");
        controller.Reset();f.Driver.Set("candidate",null!);UnknownCargo();
        assert(f.Limit()>.3f&&f.Driver.Get<float>("scenePedalFloor")==0,"Removing the independent gate constraint permits the otherwise clear standard scene");
        UnknownCargo();f.Observation=f.Observation with{MeasuredShape=null};
        assert(f.Limit()<0,"Standard source cannot silently reuse an expanded cargo envelope as raw measurement");
        UnknownCargo();f.Observation=f.Observation with{Scene=f.Observation.Scene! with{ProcessId=999}};
        assert(f.Limit()<0,"A foreign scene process cannot authorize the standard output");
        UnknownCargo();f.Observation=f.Observation with{Scene=f.Observation.Scene! with{Session=999}};
        assert(f.Limit()>.3f,"Independent geometry and scene producer sessions are not incorrectly equated");
        // The prior epoch-change adversary intentionally retired session 44.
        // A new independent source is required for the positive hazard case.
        var acute=new Fixture(detailed:false);acute.Bodies=[acute.Body(0,50)];acute.Frame(40,20/3.6,phase:0);
        acute.Observation=acute.Observation with{Context=null,Envelope=null};
        assert(acute.Limit()==-5&&acute.Driver.Plugin.FaultBraking,"Measured imminent obstacle still stops standard driving");
        acute.Driver.F9();
        assert(!acute.Driver.Plugin.FaultBraking&&!acute.Driver.State.EnableAssists&&acute.Driver.Get<float>("scenePedalFloor")==0,
            "F9 clears standard emergency/hold outputs and returns manual control");
        var cancel=new Fixture(detailed:false);cancel.Observation=cancel.Observation with{Context=null,Envelope=null};
        using(var operation=cancel.Driver.Get<FaultBrake>("faultBrake").BeginOperation()) {
            cancel.DuringRead=n=>{if(n==2)cancel.Driver.F9();};
            cancel.Limit();
            assert(!cancel.Driver.State.EnableAssists&&cancel.Driver.Get<float>("scenePedalFloor")==0,
                "F9 during the standard raw-source transaction cannot publish a new physical hold");
        }
    }
    internal static void StandardSettings(Action<bool,string> assert)
    {
        string file=Path.Combine(Path.GetTempPath(),"maut-standard-options-"+Guid.NewGuid().ToString("N"),"settings.json");
        var f=new Fixture();f.Driver.Set("preferences",new PreferenceStore(file));
        try {
            f.Observation=f.Observation with{Envelope=null};f.Frame(40,0,phase:0);
            f.Observation=f.Observation with{Envelope=null};
            assert(f.Limit()<0,"Explicit detailed policy still holds for missing detailed evidence");
            f.Driver.State.EnableAssists=false;
            var result=f.Driver.Plugin.Configure(new TollOptions {DetailedRigProtection=false});
            assert(result.StartsWith("Einstellungen dauerhaft")&&f.Driver.Get<float>("scenePedalFloor")==0,
                "Stopped mode change clears the obsolete detailed hold and requires fresh preparation");
            var store=new PreferenceStore(file);store.Load();
            assert(!store.Options.DetailedRigProtection,"Standard policy is persisted");
            f.Driver.State.EnableAssists=true;f.Driver.Set("active",true);f.Frame(40,0,phase:0);
            f.Observation=f.Observation with{Context=null,Envelope=null};
            assert(f.Limit()>.3f&&!f.Driver.Get<bool>("sceneStopping"),"A previous missing-rig hold cannot retain its scene/coasting veto in standard mode");
            assert(f.Driver.Plugin.Configure(new TollOptions {DetailedRigProtection=true}).StartsWith("Bitte im Stand"),
                "Mode cannot change with active assists");
        } finally {if(File.Exists(file))File.Delete(file);if(Directory.Exists(Path.GetDirectoryName(file)))Directory.Delete(Path.GetDirectoryName(file)!);}
        var old=System.Text.Json.JsonSerializer.Deserialize<TollOptions>("{\"RoadCruiseSpeedKph\":100}")!;
        assert(!old.DetailedRigProtection,"Old saved settings migrate to standard policy without a cargo gate");
    }
}
