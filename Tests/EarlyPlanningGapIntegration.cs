using System.Numerics;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;

public static class EarlyPlanningGapIntegration
{
    static PlannedItem Item(float from,float to,ulong uid,int gate=-1)
    {
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        var curve=new NavCurve{StartPosition=new(0,0,from),EndPosition=new(0,0,to),Length=to-from,
            StartRotation=rotation,EndRotation=rotation,AllowedVehicles=AllowedVehicles.AllVehicles,SemaphoreId=gate};
        var descriptor=new PrefabDescriptor{NavCurves=[curve]};
        if(gate>=0)descriptor.Semaphores.Add(new(){SemaphoreId=(uint)gate,Position=new(2.5f,0,to-.4f)});
        var prefab=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
        prefab.Prefab=new(){Uid=uid};prefab.Descriptor=descriptor;
        var path=new PrefabPath(prefab,new(){Uid=uid*10+1},new(){Uid=uid*10+2},[curve],Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
        return new(){item=prefab,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,direction=Direction.Forward,laneIndex=0};
    }
    public static void RemoteUnknownPlaza(Action<bool,string> assert)
    {
        TollRouter Router(Func<bool>? proof=null)=>new(new PreferenceStore(Path.Combine(Path.GetTempPath(),"unused-gap-"+Guid.NewGuid()+".json")),
            p=>p.Descriptor!.NavCurves.Any(c=>c.SemaphoreId==7)?new Dictionary<int,TollPayment>{{7,TollPayment.Automatic}}:new Dictionary<int,TollPayment>(),_=>proof?.Invoke()??false);
        var data=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,10),Item(800,1000,55,7)]};
        var truck=Vector3.Zero;var router=Router();router.Discover(data,truck,[],10);
        bool applied=router.Apply(data,truck,out var issue,10);double? distance=null;Exception? failure=null;
        try {distance=router.UpdateStopDistance(data,truck,10);}catch(Exception ex){failure=ex;}
        assert(applied&&issue==null&&failure==null,
            $"A far typed-payment plaza behind a safely distant accepted-prefix gap cannot invent an untrackable branch fault (applied={applied}, issue={issue}, stop={router.StopBoundary}, exception={failure?.Message})");
        assert(distance==null||distance>200,
            "An unavailable remote plaza must defer to the existing prefix-gap approach instead of adding an immediate stop");

        bool known=true;var revoked=Router(()=>known);revoked.Discover(data,truck,[],10);known=false;
        Exception? revokedFailure=null;double? revokedDistance=null;
        bool revokedApplied=revoked.Apply(data,truck,out var revokedIssue,10);
        try {revokedDistance=revoked.UpdateStopDistance(data,truck,10);}catch(Exception ex){revokedFailure=ex;}
        assert(revokedApplied&&revokedIssue==null&&revokedFailure==null&&(revokedDistance==null||revokedDistance>200),
            "Proof expiring after early static selection of a disconnected remote plaza cannot turn that future item into a current branch emergency");

        var connected=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,20),Item(300,500,66,7)]};
        var near=new Vector3(0,0,250);var active=Router();active.Discover(connected,near,[],10);
        assert(active.Apply(connected,near,out var nearIssue,10)&&nearIssue==null,
            "Deferring a remote unknown plaza must retain the real connected uncertain-lane stop");
        var nearDistance=active.UpdateStopDistance(connected,near,10);
        assert(active.StopBoundary.HasValue&&nearDistance is >0 and <100,
            "The actually connected nearby unknown branch still gets its finite normal stop distance");

        (TollRouter Router,PlannedPathData Frame,string Owner) HeldA()
        {
            bool clear=true;var current=Router(()=>clear);
            var frame=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,40),Item(300,500,88,7)]};
            current.Discover(frame,near,[],10);
            assert(current.Apply(frame,near,out _,10),"Ownership fixture first genuinely applies a valid selected A topology");
            clear=false;
            assert(current.Apply(frame,near,out _,10)&&current.ClearanceHoldRequested&&current.ClearanceHoldKey!=null,
                "Ownership fixture establishes a genuine current-proof hold for A");
            return(current,frame,current.ClearanceHoldKey!);
        }
        var absent=HeldA();
        var bOnly=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,41),Item(800,1000,55,7)]};
        absent.Router.Discover(bOnly,truck,[],10);absent.Router.Apply(bOnly,truck,out _,10);
        assert(absent.Router.PlanningDeferred&&!absent.Router.ClearanceHoldRequested&&absent.Router.ClearanceHoldKey==null,
            "Valid route replacement removing A revokes its old proof hold while remote B remains deferred");
        var alive=HeldA();
        var withA=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,42),Item(800,1000,56,7),alive.Frame.PlannedPath[1]]};
        alive.Router.Discover(withA,truck,[],10);alive.Router.Apply(withA,truck,out _,10);
        assert(alive.Router.PlanningDeferred&&alive.Router.ClearanceHoldRequested&&alive.Router.ClearanceHoldKey==alive.Owner,
            "Deferring distinct B cannot clear the proof owner A while that exact owner still belongs to the valid route");

        var unknownFuture=Item(800,1000,77,7);var old=unknownFuture.prefabPath!.Curves[0];
        unknownFuture.prefabPath.Curves=[new(){StartPosition=old.StartPosition,EndPosition=old.EndPosition,StartRotation=old.StartRotation,
            EndRotation=old.EndRotation,Length=old.Length,AllowedVehicles=old.AllowedVehicles,SemaphoreId=old.SemaphoreId}];
        var unknownFrame=new PlannedPathData{HasError=false,PlannedPath=[Item(-100,300,43),unknownFuture]};
        var accepted=RouteMatcher.SampleForTracking(unknownFrame,truck,10);
        assert(accepted.EndsAtGap&&accepted.IdentityKnown,
            "An unaccepted unknown future item cannot contaminate the verified identity of the connected prefix before its gap");
    }
}
