using System.Numerics;
using MautAssistent;
#if INTEGRATION
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;
#endif

public static class ManualPaymentTests
{
#if INTEGRATION
    public static void RouteZone(Action<bool,string> check)
    {
        // Authored map/trigger only, no copied game assets or SDK instances.
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        TruckLib.Models.Ppd.NavCurve Curve(float a,float b,int gate=-1)=>new(){StartPosition=new(0,0,a),EndPosition=new(0,0,b),Length=b-a,
            StartRotation=rotation,EndRotation=rotation,SemaphoreId=gate,AllowedVehicles=TruckLib.Models.Ppd.AllowedVehicles.AllVehicles};
        var curves=new List<TruckLib.Models.Ppd.NavCurve>{Curve(0,20),Curve(20,40,1),Curve(40,80)};
        curves[0].NextLines.Add(1);curves[1].NextLines.Add(2);
        var descriptor=new TruckLib.Models.Ppd.PrefabDescriptor{NavCurves=curves};
        descriptor.Semaphores.Add(new(){SemaphoreId=1,Position=new(2,0,39.6f)});
        var parsed=(ETS2LA.Game.Data.ParsedPrefab)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ETS2LA.Game.Data.ParsedPrefab));
        parsed.Prefab=new(){Uid=910};parsed.Descriptor=descriptor;
        var path=new ETS2LA.Game.Data.PrefabPath(parsed,new(){Uid=911},new(){Uid=912},curves.ToList(),Direction.Forward,
            Matrix4x4.CreateRotationY(MathF.PI/2),new(100,0,200));
        var item=new PlannedItem{item=parsed,prefabPath=path,startNode=path.StartNode,endNode=path.EndNode,direction=Direction.Forward,laneIndex=0};
        var data=new PlannedPathData{HasError=false,PlannedPath=[item]};
        ManualStopZone proof=new(10.6,18.6){Trigger=new(new(0,0,25),Vector3.UnitZ,4,1.75)};
        var router=new TollRouter(new PreferenceStore(Path.Combine(Path.GetTempPath(),"unused-payment-zone-"+Guid.NewGuid()+".json")),
            _=>new Dictionary<int,TollPayment>{{1,TollPayment.Manual}},_=>true,readManualStop:(_,_)=>proof);
        router.Discover(data,path.Interpolate(.02f,0)!.Value.Position,[]);
        var world=router.ManualPaymentZone(8);
        check(world is {GateId:1}&&world.Zone.Valid&&world.Zone.Contains(world.Zone.Center,world.Zone.Forward),
            "The selected directed manual lane exports a current rigidly transformed interaction zone and its exact gate ID");
        check(world!=null&&Vector3.Distance(world.Zone.Center,new(225,0,-100))<.001&&
            Vector3.Distance(world.Zone.Forward,Vector3.UnitX)<.001&&world.Anchor==router.SelectedBarrier!.Anchor,
            "World trigger placement follows the pinned translation-then-rotation host convention, retaining the real gate anchor");
        var first=world!;
        proof=proof with {Trigger=proof.Trigger! with {HalfWidth=1.5}};
        check(router.ManualPaymentZone(8) is { } changed&&changed.Zone.Key.GateIdentity==first.Zone.Key.GateIdentity&&
            changed.Zone.Key.ZoneRevision!=first.Zone.Key.ZoneRevision,"Changing trigger geometry revokes its revision without forgetting the consumed physical booth");
        proof=proof with {MinimumOffset=1,MaximumOffset=4};
        check(router.ManualPaymentZone(8)==null,"A payment trigger closer than the unchanged safe stop target remains a manual fallback");
        proof=proof with {MinimumOffset=10.6,MaximumOffset=18.6,Trigger=null};
        check(router.ManualStopOffset(8)!=null&&router.ManualPaymentZone(8)==null,"A longitudinal-only stop interval never fabricates lateral/heading payment proof");
        proof=proof with {Trigger=new(new(0,0,25),Vector3.UnitZ,4,1.75)};
        curves[0].NextLines.Clear();
        check(router.ManualPaymentZone(8)==null,"Broken canonical directed graph invalidates interaction geometry");
    }
#endif
    static readonly PaymentKey Key=new("plaza:gate:10","directed-route:1","model-zone:1");
    static PaymentZoneProof Zone(PaymentKey? key=null)=>new(key??Key,new(10,2,20),Vector3.UnitZ,2,1.5,.4,.98);
    static PaymentObservation At(double time,PaymentKey? key=null)=>new(key??Key,5,time,time,new(10,2,20),Vector3.UnitZ,0,
        true,true,false,true,true,true,BarrierState.Closed);
    static PaymentDecision Ready(ManualPayment value,PaymentKey? key=null)
    {
        PaymentDecision result=new(PaymentState.Idle,false,"");
        for(int i=0;i<=6;i++)result=value.Step(At(1+i*.1,key),Zone(key));
        return result;
    }
    public static void Run(Action<bool,string> check)
    {
        int persisted=0;
        var value=new ManualPayment(_=>persisted++);
        check(value.Step(At(1),Zone()).State==PaymentState.Stabilizing,"One stopped sample cannot activate payment");
        int activations=0;
        for(int i=1;i<=8;i++)if(value.Step(At(1+i*.1),Zone()).Activate)activations++;
        check(activations==1&&persisted==1,"Exactly one activation intent is emitted and persisted before the caller acts");
        check(value.Step(At(2),Zone()).State==PaymentState.WaitingForGate,"An intent is not a payment acknowledgement");
        check(value.Step(At(12),Zone()).State==PaymentState.TimedOut,"Own gate failing to open times out without a repeat or emergency brake");
        check(!value.Step(At(12.1),Zone()).Activate,"Timeout cannot automatically retry payment");
        value.Cancel();var revision=Key with {RouteRevision="new-route",ZoneRevision="new-zone"};
        for(int i=0;i<8;i++)check(!value.Step(At(13+i*.1,revision) with {Epoch=99},Zone(revision)).Activate,
            "F9 and new epoch/route/zone do not erase the consumed booth identity");
        var restored=new ManualPayment();restored.RestoreConsumed(value.ConsumedGateIds);
        check(!Ready(restored,revision).Activate&&restored.ConsumedGateIds.SequenceEqual(new[]{Key.GateIdentity}),
            "Export/restore retains consumed booth identities across application restart");
        var another=Key with {GateIdentity="plaza:gate:11"};
        check(Ready(restored,another).State==PaymentState.WaitingForGate&&restored.ConsumedGateIds.Length==2,
            "Another independently identified booth has its own one-time attempt");

        var duplicate=new ManualPayment();duplicate.Step(At(1),Zone());
        check(!duplicate.Step(At(1.15) with {OriginalAt=1},Zone()).Activate&&
            !duplicate.Step(At(1.3) with {OriginalAt=1},Zone()).Activate,
            "Growing evaluation time on one original sample cannot establish stopped duration or renew freshness");
        check(duplicate.Step(At(2),Zone()).State==PaymentState.Stabilizing,"A source gap starts a fresh stable-stop window");
        var jump=new ManualPayment();jump.Step(At(1),Zone());jump.Step(At(1.1),Zone());
        check(jump.Step(At(1.05),Zone()).State==PaymentState.Inhibited&&
            jump.Step(At(1.2),Zone()).State==PaymentState.Stabilizing,"Clock rewind revokes the accumulated stopped window");
        var epoch=new ManualPayment();epoch.Step(At(1),Zone());epoch.Step(At(1.1),Zone());
        check(epoch.Step(At(1.4) with {Epoch=6},Zone()).State==PaymentState.Stabilizing,"An epoch change cannot borrow a pre-cancellation stop window");

        var invalids=new PaymentObservation[]{
            At(1) with {Now=double.NaN},At(1) with {OriginalAt=double.NaN},At(1) with {OriginalAt=1.01},
            At(1) with {OriginalAt=.799},At(1) with {Epoch=0},At(1) with {Speed=double.NaN},
            At(1) with {Enabled=false},At(1) with {Active=false},At(1) with {Paused=true},At(1) with {Foreground=false},
            At(1) with {ManualKnown=false},At(1) with {OwnGateBound=false},At(1) with {OwnGateState=(BarrierState)99},
            At(1) with {Speed=.101},At(1) with {Speed=-.101},At(1) with {Position=new(11.501f,2,20)},
            At(1) with {Position=new(10,2,22.001f)},At(1) with {Position=new(10,2.401f,20)},
            At(1) with {Forward=-Vector3.UnitZ},At(1) with {Forward=Vector3.Zero},
            At(1) with {Forward=new(float.NaN,0,1)},At(1) with {OwnGateState=BarrierState.Unknown},
            At(1) with {OwnGateState=BarrierState.Opening},At(1) with {OwnGateState=BarrierState.Closing}
        };
        foreach(var invalid in invalids) {
            var machine=new ManualPayment();machine.Step(At(.9),Zone());
            var result=machine.Step(invalid,Zone());
            check(!result.Activate&&result.State==PaymentState.Inhibited,"Malformed or unconfirmed context never emits activation: "+invalid);
        }
        foreach(var zone in new PaymentZoneProof?[]{null,Zone() with {HalfWidth=0},Zone() with {HalfLength=double.NaN},
            Zone() with {Forward=Vector3.Zero},Zone() with {HeadingCosine=.5},Zone() with {VerticalTolerance=2},
            Zone() with {Key=Key with {ZoneRevision="wrong"}}})
            check(new ManualPayment().Step(At(1),zone).State==PaymentState.Inhibited,"Missing, malformed or wrong-revision zone is not payment proof");

        var open=new ManualPayment();int openIntents=0;PaymentDecision opened=new(PaymentState.Idle,false,"");
        for(int i=0;i<=5;i++) {opened=open.Step(At(1+i*.1) with {OwnGateState=BarrierState.Open},Zone());if(opened.Activate)openIntents++;}
        check(openIntents==0&&opened.State==PaymentState.GateOpen,"Already-open own gate suppresses activation and needs stable fresh open observations");
        check(open.ConsumedGateIds.Length==0,"An open gate for the preceding vehicle cannot consume our own unattempted payment encounter");
        int afterPredecessor=0;
        for(int i=0;i<8;i++)if(open.Step(At(2+i*.1),Zone()).Activate)afterPredecessor++;
        check(afterPredecessor==1&&open.ConsumedGateIds.Length==1,
            "Open for a predecessor then closed at our own stopped payment position permits exactly the first payment intent");
        var after=new ManualPayment();Ready(after);
        after.Step(At(2) with {OwnGateState=BarrierState.Open},Zone());
        check(after.Step(At(2.1) with {OwnGateState=BarrierState.Closed},Zone()).State==PaymentState.WaitingForGate,
            "A brief open sample followed by closed is not completed payment");
        for(int i=0;i<=5;i++)opened=after.Step(At(2.2+i*.1) with {OwnGateState=BarrierState.Open},Zone());
        check(opened.State==PaymentState.GateOpen&&!opened.Activate,"Own stable open observations complete waiting without a second activation");
        var failPersistence=new ManualPayment(_=>throw new IOException("storage unavailable"));
        Ready(failPersistence);
        check(failPersistence.ConsumedGateIds.Length==1&&!Ready(failPersistence).Activate,
            "Persistence failure consumes the identity locally and never emits an unrecorded attempt");
        var rotated=Zone() with {Forward=Vector3.UnitX};
        check(rotated.Contains(rotated.Center+new Vector3(1.9f,0,1.4f),Vector3.UnitX)&&
            !rotated.Contains(rotated.Center+new Vector3(1.4f,0,1.9f),Vector3.UnitX),
            "Payment-zone lateral and longitudinal bounds follow its directed world placement");
        var cancelled=new ManualPayment();Ready(cancelled);
        check(cancelled.Step(At(2) with {Foreground=false},Zone()).State==PaymentState.Inhibited&&
            !Ready(cancelled).Activate,"Focus loss cancels context but cannot retry an already consumed attempt");
        var departedZone=new ManualPayment();Ready(departedZone);
        check(departedZone.Step(At(2) with {Position=new(100,2,20)},Zone()).State==PaymentState.Inhibited&&
            !Ready(departedZone).Activate,"Leaving the payment zone cancels waiting without rearming the attempted booth");
        int departed=0;
        var revisit=new ManualPayment(persistForgottenGate:_=>departed++);Ready(revisit);revisit.Cancel();
        check(revisit.ConsumedGateIds.Length==1,"Cancellation never counts as a physical departure");
        check(revisit.ForgetDepartedGate(Key.GateIdentity)&&departed==1&&revisit.ConsumedGateIds.Length==0,
            "An externally certified continuous departure can explicitly retire and persist its encounter tombstone");
        check(Ready(revisit).State==PaymentState.WaitingForGate,"A later genuinely new encounter can perform one new payment attempt");
        var blockedForget=new ManualPayment(persistForgottenGate:_=>throw new IOException());Ready(blockedForget);
        check(!blockedForget.ForgetDepartedGate(Key.GateIdentity)&&blockedForget.ConsumedGateIds.Length==1,
            "Failed departure persistence cannot rearm a consumed booth");
    }
}
