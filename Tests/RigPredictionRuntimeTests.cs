using System.Numerics;
using MautAssistent;
using TruckLib;
using System.Text;
public static class RigPredictionRuntimeTests
{
    static IReadOnlyList<WheelSample> Wheels(bool contact=true,bool steerable=false)=>Array.AsReadOnly(new[]{
        new WheelSample(new(-1,.5f,2.5f),true,steerable,false,contact,0,0),
        new WheelSample(new(1,.5f,2.5f),true,steerable,false,contact,0,0),
        new WheelSample(new(-1,.5f,3.5f),true,steerable,false,contact,0,0),
        new WheelSample(new(1,.5f,3.5f),true,steerable,false,contact,0,0)});
    static VehicleGeometry Shape(long now=10000)=>new(11,22,2,now,
        new(Vector3.Zero,Quaternion.Identity,new(-1.2f,0,-4),new(1.2f,3,3)),true,
        new(new(0,0,7),Quaternion.Identity,new(-1.25f,0,-5),new(1.25f,4,8)));
    static EnvelopeContext Context(long now=10000)=>new("actual truck/trailer/wheels/cargo",false,false,
        [new("tank","","tank","single","plate",new(0,0,7)){Hook=new(0,0,-5),Wheels=Wheels(),RotationTurns=Vector3.Zero}])
        {UtcMs=now,TruckHook=new(0,0,2)};
    static PredictionEnvelope Envelope(VehicleGeometry shape,EnvelopeContext context)=>new(shape,4.5f,2.8f,context.Identity,new object(),context.UtcMs);
    static RouteTrace Road()=>new(new[]{new Vector3(0,0,30),new Vector3(0,0,-200)},[],new[]{"actual directed road"});
    public static void Factory(Action<bool,string> check)
    {
        var shape=Shape();var context=Context();var envelope=Envelope(shape,context);var road=Road();
        RigRuntimePrediction Build(RouteTrace? r=null,PredictionEnvelope? e=null,EnvelopeContext? c=null,double speed=2,long now=10000,double along=30,double ahead=50)=>
            RigPredictionRuntime.Build(r??road,along,e??envelope,c??context,Vector3.Zero,0,speed,now,ahead);
        var good=Build();check(good.Supported,"Measured single fixed-axle rig builds an actual final-route prediction");
        check(good.Input!.Path[0].Position==new Vector3(0,0,2)&&good.Input.Path[0].TruckYaw==0,"First world hook/yaw is the actual native observation");
        check(good.StartAlong==30&&good.Input.Path[0].Along==30&&good.Input.Path[^1].Along==80,"Absolute directed tracking anchor survives the factory");
        check(good.Shape!.UtcMs==10000&&good.Shape.Session==22&&good.ContextUtcMs==10000,"Factory does not refresh source time/session");
        check(good.Axle is{ContactStable:true}&&good.RequiredAhead>=50,"Typed wheel proof and full requested horizon survive");
        check(Build(speed:25).SpeedOutsideModelScope,"Fast approach is explicitly speed-limited, not absent geometry");
        check(!Build(now:10151).Supported&&!Build(e:envelope with{Shape=shape with{Session=0}}).Supported,"Old or invalid native identity cannot predict");
        check(!Build(e:envelope with{ContextIdentity="old cargo"}).Supported,"A proof from another config cannot be borrowed");
        check(!Build(c:context with{Trailers=[context.Trailers[0],context.Trailers[0]]}).Supported,"Multiple trailers are unsupported");
        check(!Build(e:envelope with{Shape=shape with{TrailerKnown=false}}).Supported,"Missing positive native trailer-state proof is unsupported");
        check(!Build(c:context with{Trailers=[context.Trailers[0] with{RotationTurns=new(.01f,0,0)}]}).Supported,
            "Trailer native and SCS headings must match independently of the tractor");
        var steer=context with{Trailers=[context.Trailers[0] with{Wheels=Wheels(steerable:true)}]};
        check(!Build(c:steer).Supported,"Configured steering trailer axle cannot be treated as fixed at zero angle");
        var flicker=context with{Trailers=[context.Trailers[0] with{Wheels=Wheels(contact:false)}]};
        var contact=Build(c:flicker);check(contact.Supported&&contact.Axle is{ContactStable:false},"Contact confidence is distinct from configured axle geometry");
        check(!Build(ahead:250).Supported,"Finite route horizon is never extrapolated");
        var noIdentity=new RouteTrace(new[]{new Vector3(0,0,30),new Vector3(0,0,-200)});
        check(!Build(r:noIdentity).Supported,"Anonymous route geometry is not a final directed path proof");
        var seam=new RouteTrace(new[]{new Vector3(0,0,30),new Vector3(0,0,-10),new Vector3(0,0,-11),new Vector3(0,0,-200)},[2],new[]{"road","join","plaza"});
        check(Build(r:seam).Supported,"An exactly straight directed native item seam does not cause an always-stop");
        var wrongSeam=new RouteTrace(new[]{new Vector3(0,0,30),new Vector3(0,0,-10),new Vector3(1,0,-10),new Vector3(1,0,-200)},[2],new[]{"road","join","plaza"});
        check(!Build(r:wrongSeam).Supported,"A geometric side-jump seam is not promoted to driving evidence");
        var slope=new RouteTrace(new[]{new Vector3(0,0,30),new Vector3(0,20,-200)},[],new[]{"steep road"});
        check(!Build(r:slope).Supported,"Steep road exceeds the explicit near-flat runtime model");
        var shifted=shape with{Truck=shape.Truck with{Position=new(.1f,0,0)},Trailer=shape.Trailer! with{Position=new(.1f,0,7)}};
        var shiftedContext=context with{Trailers=[context.Trailers[0] with{Position=new(.1f,0,7)}]};
        var shiftedEnvelope=Envelope(shifted,shiftedContext);
        var shiftedPrediction=RigPredictionRuntime.Build(road,30,shiftedEnvelope,shiftedContext,new(.1f,0,0),0,2,10000,50);
        check(shiftedPrediction.Supported&&shiftedPrediction.Input!.Path[0].Position==new Vector3(.1f,0,2)&&
            Math.Abs(shiftedPrediction.Input.TrackingError-.15)<.00001&&Math.Abs(shiftedPrediction.AnchorShift.X-.1)<.00001,
            "Known lateral start is part of the actual model path, separate from the full future tracking budget");
        foreach(var offset in new[]{.1f,.15f})foreach(var degrees in new[]{.5,1.0}) {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,(float)(degrees*Math.PI/180));
            var truck=shape.Truck with{Position=new(offset,0,0),Rotation=q};
            var hitch=truck.Position+Vector3.Transform(context.TruckHook!.Value,q);
            var measured=shape with{Truck=truck,Trailer=shape.Trailer! with{Position=hitch-new Vector3(0,0,-5)}};
            var observed=context with{Trailers=[context.Trailers[0] with{Position=measured.Trailer.Position}]};
            var points=Enumerable.Range(-120,521).Select(i=>{
                double arc=i*.25,angle=arc/200;
                return new Vector3((float)(-200*(1-Math.Cos(angle))),0,(float)(-200*Math.Sin(angle)));}).ToArray();
            var curved=new RouteTrace(points,[],Enumerable.Repeat("actual curved directed road",points.Length-1));
            double start=curved.ProjectForTracking(Vector3.Zero).Along;
            var result=RigPredictionRuntime.Build(curved,start,Envelope(measured,observed),observed,truck.Position,
                degrees/360,2,10000,40);
            check(result.Supported&&Vector3.Distance(result.Input!.Path[0].Position,hitch)<.00001&&
                Math.Abs(result.Input.Path[0].TruckYaw-degrees*Math.PI/180)<.00001,
                $"Realistic {offset:F2}m tracking / {degrees:F1}deg yaw in curved approach preserves measured start without unsupported stop ({result.Issue})");
        }
        var world=new Vector3(-33543.87f,23.19f,15893.19f);
        var far=shape with{Truck=shape.Truck with{Position=world,Minimum=new(-1.54f,0,-4),Maximum=new(1.54f,3,3)},
            Trailer=shape.Trailer! with{Position=shape.Trailer.Position+world,Minimum=new(-1.54f,0,-5),Maximum=new(1.54f,4,8)}};
        var farContext=context with{Trailers=[context.Trailers[0] with{Position=far.Trailer.Position}]};
        var farRoad=new RouteTrace(new[]{world+new Vector3(0,0,30),world+new Vector3(0,0,-200)},[],new[]{"FR world directed road"});
        var farResult=RigPredictionRuntime.Build(farRoad,30,Envelope(far,farContext),farContext,world,0,2,10000,50,.05);
        check(farResult.Supported,"Large FR world coordinates keep the explicit narrow tracking-budget model supported");
        foreach(float side in new[]{-1f,1f})check(farResult.Prediction!.ClearOf(new SceneBody(1,2,0,
            world+new Vector3(side*2.2f,2,-10),Quaternion.Identity,new(.5f,3,100),1)),
            "At large FR coordinates a straight 3.08m body plus explicit 5cm model reserve fits 3.4m walls, without a roadworthiness claim");
        bool allPhasesClear=true,allTimestampsPreserved=true;
        for(int tick=0;tick<21;tick++) {
            int scsMs=tick*16+(tick%17),nativeMs=Math.Max(0,(scsMs-1)/16*16);
            double v=20d/3.6;var nativeMove=new Vector3(0,0,(float)(-v*nativeMs/1000));
            var currentMove=new Vector3(0,0,(float)(-v*scsMs/1000));
            var native=far with{UtcMs=10000+nativeMs,Sequence=(ulong)(2+nativeMs/16*2),
                Truck=far.Truck with{Position=world+nativeMove},Trailer=far.Trailer! with{Position=world+new Vector3(0,0,7)+nativeMove}};
            var observed=farContext with{UtcMs=10000+scsMs,Trailers=[farContext.Trailers[0] with{Position=world+new Vector3(0,0,7)+currentMove}]};
            var predicted=RigPredictionRuntime.Build(farRoad,30+v*scsMs/1000,Envelope(native,observed),observed,
                world+currentMove,0,v,10000+scsMs,0,.08);
            allTimestampsPreserved&=predicted.Shape?.UtcMs==native.UtcMs&&predicted.ContextUtcMs==observed.UtcMs;
            allPhasesClear&=predicted.Supported&&new[]{-1f,1f}.All(side=>predicted.Prediction!.ClearOf(new SceneBody(1,2,0,
                world+new Vector3(side*2.2f,2,-10),Quaternion.Identity,new(.5f,3,100),1)));
        }
        check(allPhasesClear,"Product 8cm tracking budget at 20km/h remains clear in 3.4m FR walls across jittered 16ms native/SCS moving phases");
        check(allTimestampsPreserved,"Moving phase reserves retain both actual source capture times");
        var sideObservation=farContext with{UtcMs=10016,Trailers=[farContext.Trailers[0] with{Position=far.Trailer!.Position+new Vector3(.1f,0,-.089f)}]};
        var sideResult=RigPredictionRuntime.Build(farRoad,30+.089,Envelope(far,sideObservation),sideObservation,
            world+new Vector3(.1f,0,-.089f),0,20d/3.6,10016,0,.08);
        check(sideResult.Supported&&!sideResult.Prediction!.ClearOf(new SceneBody(1,2,0,world+new Vector3(2.2f,2,-10),
            Quaternion.Identity,new(.5f,3,100),1)),"A real sideways source residual keeps its full directional support and cannot become a false narrow-lane clear");
    }
    sealed class Files:IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>false;public bool DirectoryExists(string p)=>true;
        public IList<string> GetFiles(string p)=>[];public byte[] ReadAllBytes(string p)=>throw new FileNotFoundException();
        public string ReadAllText(string p)=>throw new FileNotFoundException();public string ReadAllText(string p,Encoding e)=>ReadAllText(p);
        public Stream Open(string p)=>throw new FileNotFoundException();public string GetParent(string p)=>"/";
    }
    public static void ExpandedCache(Action<bool,string> check)
    {
        long now=10000;IFileSystem files=new Files();var shape=Shape();var context=Context();
        var evidence=new VehicleEnvelopeEvidence(()=>files,()=>now,(f,c,s)=>new(4.8f,3.1f,"independent auxiliary fixture",null)
            {TrailerLow=new(-1.4f,0,-5),TrailerHigh=new(1.4f,4.3f,8)});
        check(evidence.ReadForPrediction(context,shape,Vector3.Zero,0,0)==null,"No assumed cold-start model proof");
        evidence.Read(context,shape,Vector3.Zero,0,0,Road());now+=800;context=Context(now);shape=Shape(now);
        evidence.Read(context,shape,Vector3.Zero,0,0,Road());
        PredictionEnvelope? current=null;
        check(SpinWait.SpinUntil(()=>{current=evidence.ReadForPrediction(context,shape,Vector3.Zero,0,0);return current!=null;},3000),"Independent cargo worker result exposes current coherent measured bounds");
        check(current!.Shape.Trailer!.Maximum.Y==4.3f&&current.PlanningWidth==3.1f&&current.Shape.UtcMs==now,
            "Expanded cargo geometry and immutable local planning width survive without pose restamping");
        var yawed=shape with{Trailer=shape.Trailer! with{Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.13f)}};
        check(evidence.ReadForPrediction(context,yawed,Vector3.Zero,0,0)?.PlanningWidth==3.1f,"Articulation does not globally widen an immutable local planning profile");
        now+=301;check(evidence.ReadForPrediction(context,shape,Vector3.Zero,0,0)==null,"Cached geometry never supplies fresh measurement authority");
        now-=301;files=new Files();check(evidence.ReadForPrediction(context,shape,Vector3.Zero,0,0)==null,"Asset replacement revokes independent model proof");
    }
}
