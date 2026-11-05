using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ETS2LA.Game.Data;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using PathLib;
using MautAssistent;

public static class RouteIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        // Authored geometry: four parallel booths with one common entry and exit.
        // No extracted game map or prefab data is included.
        var curves=new List<NavCurve>();
        NavCurve Curve(Vector3 a,Vector3 b,int gate=-1)=>new() {
            StartPosition=a,EndPosition=b,StartRotation=Quaternion.Identity,EndRotation=Quaternion.Identity,
            Length=Vector3.Distance(a,b),SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles
        };
        curves.Add(Curve(new(0,0,0),new(0,0,20)));
        foreach(var lane in new[]{(id:4,x:0f),(id:12,x:-6f),(id:14,x:6f),(id:15,x:-12f)}) {
            int i=curves.Count;
            curves[0].NextLines.Add(i);
            curves.Add(Curve(new(0,0,20),new(lane.x,0,60)));
            curves.Add(Curve(new(lane.x,0,60),new(lane.x,0,80),lane.id));
            curves.Add(Curve(new(lane.x,0,80),new(0,0,120)));
            curves[i].NextLines.Add(i+1);curves[i+1].NextLines.Add(i+2);curves[i+2].NextLines.Add(13);
        }
        curves.Add(Curve(new(0,0,120),new(0,0,140)));
        var descriptor=new PrefabDescriptor{NavCurves=curves};
        foreach(var id in new[]{4,12,14,15}) {
            var c=curves.First(c=>c.SemaphoreId==id);
            descriptor.Semaphores.Add(new TruckLib.Models.Ppd.Semaphore{SemaphoreId=(uint)id,Position=c.EndPosition+new Vector3(2.5f,0,-.4f)});
        }
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        parsed.Descriptor=descriptor;parsed.Prefab=new Prefab{Uid=100};
        var start=new Node{Uid=101};var end=new Node{Uid=102};
        var source=new PrefabPath(parsed,start,end,new[]{0,1,2,3,13}.Select(i=>curves[i]).ToList(),Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        var fixedPath=TollRouter.ViaGate(source,12)!;
        assert(fixedPath!=null,"Confirmed path missing");
        assert(source.Curves.Any(c=>c.SemaphoreId==4),"Source path must remain unchanged");
        assert(fixedPath!.GetSemaphores().Single().Semaphore.SemaphoreId==12,"Correct map semaphore");
        assert(Vector3.Distance(source.Interpolate(.99999f,0)!.Value.Position,fixedPath.Interpolate(.99999f,0)!.Value.Position)<.01,
            "Toll lane replacement must preserve the original road exit");
        var item=new PlannedItem{item=parsed,prefabPath=source,startNode=start,endNode=end,direction=Direction.Forward,laneIndex=0};
        var data=new PlannedPathData{PlannedPath=[item],HasError=false};
        var truck=source.Interpolate(.03f,0)!.Value.Position;
        var sdk=descriptor.Semaphores.Select(s=>new ETS2LA.Game.SDK.Semaphore{position=s.Position,id=(int)s.SemaphoreId,
            type=ETS2LA.Game.SDK.SemaphoreType.GATE,state=1}).ToArray();
        var filename=Path.Combine(AppContext.BaseDirectory,"route-integration-settings.json");
        if(File.Exists(filename))File.Delete(filename);
        var prefs=new PreferenceStore(filename);var router=new TollRouter(prefs);
        router.Discover(data,truck,sdk);
        assert(router.Choices.Single(c=>c.GateId==12).Label.StartsWith("2."),"Gate 12 must be second from right");
        assert(router.Apply(data,truck,out _) && router.StopBoundary.HasValue,"Unknown lane must request a planned stop, not a payment clearance");
        assert(item.prefabPath.GetSemaphores().Single().Semaphore.SemaphoreId==4,"Unknown lane must not rewrite host route into another booth");
        assert(router.UpdateStopDistance(data,truck,0)<5,"Stop must precede the first branch, not the gate");
        router.Confirm(12,TollPayment.Automatic);
        assert(router.StopBoundary==null,"Explicit automatic truck lane releases the planned stop");
        assert(router.Apply(data,truck,out var error)&&error==null,"Route must apply");
        assert(item.prefabPath.GetSemaphores().Single().Semaphore.SemaphoreId==12,"Host route not updated");
        var gate=RouteMatcher.Select(data,truck,sdk,null);
        assert(gate?.Key.StartsWith("12:")==true,"Route matcher chose another gate");
        var trace=RouteMatcher.Sample(data);
        for(int n=1;n<19;n++) {
            var point=item.prefabPath.Interpolate(n/20f,0)!.Value.Position;
            assert(trace.Project(point).Offset<.08,"Sampled route deviates inside a curve");
        }
        var reloaded=new PreferenceStore(filename);reloaded.Load();
        assert(reloaded.Gates[router.CurrentKey!]==12,"Selected lane not saved");
        // A new plaza must not silently inherit the known plaza's height clearance.
        parsed.Prefab.Uid=999;var unknown=new TollRouter(reloaded);
        unknown.Discover(data,truck,sdk);
        assert(unknown.Apply(data,truck,out error)&&error==null&&unknown.StopBoundary.HasValue,"Unknown plaza must stop before entry, not inherit clearance");
        unknown.Confirm(12,TollPayment.Manual);
        assert(unknown.Apply(data,truck,out _)&&unknown.StopBoundary.HasValue,"Manual booth must retain approach stop, not use automatic payment offset");
        unknown.Confirm(12,TollPayment.Automatic);
        assert(unknown.Apply(data,truck,out error)&&error==null,"Explicit confirmation must enable selected route");
        reloaded.Save(new TollOptions{ApproachSpeedKph=15},reloaded.Gates);
        var again=new PreferenceStore(filename);again.Load();assert(again.Options.ApproachSpeedKph==15,"Settings not restored");
        assert(again.Payments[PaymentPolicy.Key(unknown.CurrentKey!,12)]==TollPayment.Automatic,"Payment type not restored");
        File.Delete(filename);

        // Pay-point markers are positioned before the semaphore in some models.
        var marks = TollModelEvidence.Match(descriptor,
            [(new(-6,0,69),TollPayment.Automatic), (new(6,0,69),TollPayment.Manual),
             (new(-3,0,69),TollPayment.Automatic)]);
        assert(marks.Count==2 && marks[12]==TollPayment.Automatic && marks[14]==TollPayment.Manual,
            "Resolve point to its own lane and reject a marker between lanes");
        var byModel = new TollRouter(new PreferenceStore(filename), _ => marks);
        item.prefabPath=source;
        byModel.Discover(data,truck,sdk);
        assert(byModel.SelectedGate==12 && byModel.Apply(data,truck,out _) && !byModel.StopBoundary.HasValue,
            "Choose explicit AutoPASS without a plaza confirmation");
        var lateRouter = new TollRouter(new PreferenceStore(filename), _ => marks);
        item.prefabPath=source;
        lateRouter.Discover(data,truck,sdk);
        var pastSplit = source.Interpolate(.82f,0)!.Value.Position;
        assert(!lateRouter.Apply(data,pastSplit,out _) && ReferenceEquals(item.prefabPath,source),
            "A truck already committed to a different booth must never get a late route rewrite");
        curves[5].AllowedVehicles=AllowedVehicles.SmallVehicles;
        var truckOnly = new TollRouter(new PreferenceStore(filename), _ => marks);
        item.prefabPath=source;
        truckOnly.Discover(data,truck,sdk);
        assert(truckOnly.SelectedGate==14 && truckOnly.Apply(data,truck,out _),
            "A manual truck lane beats an automatic lane with a car-only approach");
        curves[5].AllowedVehicles=AllowedVehicles.AllVehicles;
        item.prefabPath=source;

        // Full plugin path: an unknown toll at motorway speed requests ACC
        // deceleration while retaining valid steering, without the fault channel.
        curves[0].StartPosition=new(0,0,-400);curves[0].Length=420;
        source.Length=source.Curves.Sum(c=>c.Length);item.prefabPath=source;
        var state=(ETS2LA.State.ApplicationState)RuntimeHelpers.GetUninitializedObject(typeof(ETS2LA.State.ApplicationState));
        state.DrivingMode=ETS2LA.State.DrivingMode.FullSelfDriving;
        var output=new List<ETS2LA.Game.Output.ControlEvent>();var notices=new List<string>();
        var plugin=new TollPlugin(()=>state,(title,_)=>notices.Add(title),output.Add);
        plugin._IsRunning=true;
        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        void Set(string name,object value)=>typeof(TollPlugin).GetField(name,hidden)!.SetValue(plugin,value);
        object? Call(string name,params object?[] values)=>typeof(TollPlugin).GetMethod(name,hidden)!.Invoke(plugin,values);
        var telemetry=new ETS2LA.Game.Telemetry.GameTelemetryData {sdkActive=true,time=1};
        telemetry.truckFloat.speed=25;telemetry.truckPlacement.coordinate=new(0,0,-250);
        telemetry.truckPlacement.rotation=new(.5,0,0);
        Call("OnTelemetry",telemetry);
        Call("OnGates",new ETS2LA.Game.SDK.SemaphoreData {semaphores=sdk});
        Set("active",true);state.EnableAssists=true;
        ((DriveSupervisor)typeof(TollPlugin).GetField("supervisor",hidden)!.GetValue(plugin)!).Step(
            new(0,25,true,false,true,RouteFeed.Valid,0,true,true));
        Call("OnPath",data);
        assert(plugin.Active&&state.EnableAssists&&!plugin.FaultBraking&&output.Count==0,"Unknown toll must retain steering and avoid emergency output");
        var acceleration=(float)Call("Evaluate")!;
        assert(acceleration<-.5f&&acceleration>-2.5f,"ACC must receive a bounded planned deceleration");
        Call("OnPath",data);
        assert(notices.Count==1,"One takeover warning, not a warning every frame");
        Call("OnPath",new PlannedPathData {HasError=true,ErrorMessage="No suitable road",PlannedPath=[]});
        assert(plugin.FaultBraking&&!state.EnableAssists&&output[^1].Variables.abackward==-1,"Real tracking loss during planned halt must still brake immediately");
        Call("OnCancel",null,new ETS2LA.Controls.ControlChangeEventArgs(false,null!));
        assert(float.IsNaN((float)Call("Evaluate")!),"F9 releases planned braking immediately");
    }
}
