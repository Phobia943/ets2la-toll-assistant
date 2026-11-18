using System.Diagnostics;
using System.Numerics;
using MautAssistent;
using TruckLib.Models;
using TruckLib.ScsMap;
using Model=TruckLib.Models.Model;

internal static class StaticRoadSignControlReviewTests
{
    // The actual Sign+PMC parser and nominal geometry query feed the same
    // result contract as the isolated process. Only IPC is substituted here.
    sealed class Worker(RigStaticGeometry mesh):IStaticRoadChecks
    {
        internal bool Present=true;internal Action? DuringFinal;
        public string Status=>"synthetic static sign collision";
        public StaticRoadWorkerClient.Result? Read(RouteTrace route,Vector3 position,double horizon,long epoch)
        {
            if(!Present)return null;var corridor=StaticRoadCorridor.Create(route,position,horizon);if(corridor==null)return null;
            var found=mesh.CheckNominal(corridor);if(found.Issue!=null)return null;
            var hits=found.ConflictAlong is { } along?new[]{new StaticRoadWorkerProtocol.Hit(91,along)}:[];
            return new(1,1,epoch,Stopwatch.GetTimestamp(),route,"europe",hits,false,null);
        }
        public bool IsCurrent(StaticRoadWorkerClient.Result result,RouteTrace route,long epoch){var action=DuringFinal;DuringFinal=null;action?.Invoke();return Present&&ReferenceEquals(result.Route,route)&&result.Epoch==epoch;}
        public void Reset(){Present=false;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var f=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
        try {
        f.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});f.F8();f.Probe();f.Fresh(5);
        var map=new Map();var sign=Sign.Add(map,f.Point+new Vector3(0,0,25),"pole");sign.Node.Rotation=Quaternion.Identity;sign.Variant="default";
        var model=new Model{Parts=[new Part("vis")],Variants=[new("default"){Attributes=[new(){Type=0,Tag="visible",Value=1}]}]};
        var definition=StaticRoadSignRegistry.Parse("SiiNunit { sign_model: sign.pole {model_desc: \"/pole.pmd\" model_coll: \"/pole.pmc\" dynamic: false} }")["pole"]!;
        var captured=StaticRoadSceneSource.CaptureSign(sign,definition,model,StaticRoadSignReviewTests.Pmc(("default",[new(1,new(0,2,0),Quaternion.Identity,new(.2f,4,.4f))])));
        check(captured.Geometry!=null,"The control fixture uses an actual eligible Sign instance and parsed PMC box");
        var worker=new Worker(captured.Geometry!);f.Set("staticRoadWorker",worker);
        ParkedSceneAdmission.Result[] Evaluate(){f.Fresh(5);f.Set("lastAcceptedPlanAt",f.Now);using var operation=f.Get<FaultBrake>("faultBrake").BeginOperation();return (ParkedSceneAdmission.Result[])f.Call("EvaluateStaticRoadChecks")!;}
        var checks=Evaluate();check(checks is [{Clear:false,AcuteDistance:null,Kind:SceneCheckKind.ConservativeOccupancy}]&&checks[0].StopDistance is >8 and <11,
            "Sign collision in the standard road corridor reaches an ordinary planned stop, not an emergency");
        f.State.DesiredSpeed=20;f.Fresh(5);f.Plugin.Tick();var output=f.Get<TollPlugin.LongitudinalSnapshot>("longitudinalSnapshot");
        check(output.Output.Throttle==0&&output.Output.Brake>0&&!f.Plugin.FaultBraking,"The actual controller Tick brakes for the parsed static sign without latching fault braking");
        worker.Present=false;check(Evaluate().Length==0&&!f.Plugin.FaultBraking,"Unknown/revoked sign evidence does not retain a stop or invent a scene clearance");
        worker.Present=true;worker.DuringFinal=()=>f.F9();check(Evaluate().Length==0&&!f.Plugin.FaultBraking,"F9 during final sign-result validation revokes that old stop");
        } finally {f.F9();f.Call("StopDiagnostics");}
    }
}
