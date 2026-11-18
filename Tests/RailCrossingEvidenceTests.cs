using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;
using TruckLib.Sii;
using LiveSignal=ETS2LA.Game.SDK.Semaphore;
using LiveType=ETS2LA.Game.SDK.SemaphoreType;
using MapSignal=TruckLib.Models.Ppd.Semaphore;
using MapType=TruckLib.Models.Ppd.SemaphoreType;

// Independent synthetic map/SDK fixtures. The two distinct post locators share
// PPD id1 and remap to SDK/controller id0, as in the captured crossing. These
// tests do not reproduce native warning-light animation or train behaviour.
public static class RailCrossingEvidenceTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    sealed record Fixture(PrefabDescriptor Descriptor,PrefabPath Path,PlannedPathData Plan,
        RouteTrace Trace,MapSignal Controller,Vector3 Truck,Vector3 Center,Vector3[] Posts);
    static RailSignalProfile Definition()=>new(["barrier_distance","model_only"],["","crossing_r"],[0]);
    static Dictionary<string,RailSignalProfile> Profiles()=>new(){{"crossing",Definition()}};
    static Fixture Build(Vector3? offset=null,Matrix4x4? orientation=null)
    {
        var origin=offset??Vector3.Zero;var matrix=orientation??Matrix4x4.Identity;
        Vector3 World(Vector3 local)=>Vector3.Transform(local+origin,matrix);
        var heading=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        NavCurve Line(float a,float b,int signal)=>new(){StartPosition=new(0,0,a),EndPosition=new(0,0,b),
            StartRotation=heading,EndRotation=heading,Length=b-a,SemaphoreId=signal,AllowedVehicles=AllowedVehicles.AllVehicles};
        var curves=new List<NavCurve>{Line(0,20,-1),Line(20,35,0),Line(35,60,0),Line(60,200,-1)};
        for(int i=0;i<curves.Count-1;i++){curves[i].NextLines.Add(i+1);curves[i+1].PreviousLines.Add(i);}
        var controller=new MapSignal{SemaphoreId=0,Type=MapType.UseProfile,Profile="crossing",Position=new(0,.5f,35)};
        Vector3[] localPosts=[new(6.35f,.25f,39.67891f),new(-6.3628373f,.25f,30.421955f)];
        var descriptor=new PrefabDescriptor{NavCurves=curves};descriptor.Semaphores.Add(controller);
        foreach(var point in localPosts)descriptor.Semaphores.Add(new(){SemaphoreId=1,Type=MapType.UseProfile,Profile="crossing",Position=point});
        var raw=new Prefab{Uid=991400,Model="railtest",Variant="default"};
        var bounds=typeof(MapItem).GetProperty("Kdop",All)!.GetValue(raw)!;
        var field=bounds.GetType().GetField("Flags",All)!;var flags=field.GetValue(bounds)!;
        flags.GetType().GetProperty("Item",[typeof(int)])!.SetValue(flags,true,[19]);field.SetValue(bounds,flags);
        var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));parsed.Prefab=raw;parsed.Descriptor=descriptor;
        var start=new Node{Uid=991401,Position=World(Vector3.Zero),Rotation=heading};
        var end=new Node{Uid=991402,Position=World(new(0,0,200)),Rotation=heading};
        var path=new PrefabPath(parsed,start,end,curves,Direction.Forward,matrix,origin);
        var plan=new PlannedPathData{HasError=false,PlannedPath=[new(){item=parsed,prefabPath=path,startNode=start,endNode=end,direction=Direction.Forward,laneIndex=0}]};
        return new(descriptor,path,plan,RouteMatcher.Sample(plan),controller,World(Vector3.Zero),World(controller.Position),localPosts.Select(World).ToArray());
    }
    static LiveSignal Signal(Vector3 position,int state=3,LiveType type=LiveType.GATE,int id=0)=>new(){position=position,state=state,type=type,id=id};
    static LiveSignal[] Open(Fixture f)=>f.Posts.Select(p=>Signal(p)).ToArray();
    static RailCrossingDecision? Evaluate(Fixture f,LiveSignal[] signals,IReadOnlyDictionary<string,RailSignalProfile>? profiles=null)
        =>RailCrossingEvidence.Evaluate(f.Path,f.Controller,profiles??Profiles(),signals);
    static TrafficSpeedDecision Policy(Fixture f,LiveSignal[] signals,IReadOnlyDictionary<string,RailSignalProfile>? profiles=null,List<object>? observations=null)
        =>TollPlugin.EvaluateTrafficLights(f.Plan,f.Trace,f.Truck,signals,null,observations,profiles??Profiles());

    public static void Binding(Action<bool,string> check)
    {
        var f=Build();var open=Open(f);var good=Evaluate(f,open);
        check(good is {Clear:true}&&good.Expected.Length==2&&good.States.All(s=>s==3),
            "Two distinct PPD id1 posts remapped to controller id0 both contribute to crossing clearance");
        check(f.Posts.All(p=>Vector3.Distance(p,f.Center)>1)&&Policy(f,open).Clear,
            "Actual traffic policy accepts exact remapped post positions without a radius expansion around the central controller");
        check(!Evaluate(f,[open[0]])!.Clear&&!Evaluate(f,[open[0],Signal(f.Posts[0])])!.Clear,
            "One missing expected post cannot be replaced by a duplicate observation of the other");
        foreach(int state in new[]{0,1,2,8,99,-1}) {
            check(!Evaluate(f,[open[0],Signal(f.Posts[1],state)])!.Clear,
                "A closing, closed, opening or unknown crossing member cannot be cleared by its open partner: "+state);
        }
        check(!Evaluate(f,[open[0],Signal(f.Posts[1],3,id:1)])!.Clear,
            "The PPD locator id cannot replace the profile-remapped controller id in live evidence");
        check(!Evaluate(f,[open[0],Signal(f.Posts[1],8,LiveType.TRAFFICLIGHT)])!.Clear,
            "A green light at a required gate position cannot substitute for the missing gate actor");
        check(!Evaluate(f,[..open,Signal(f.Posts[1],1)])!.Clear,
            "Conflicting live replicas at a mapped post veto crossing clearance");
        check(!Evaluate(f,[open[0],Signal(f.Posts[1]+new Vector3(1.01f,0,0))])!.Clear&&
            !Evaluate(f,[open[0],Signal(f.Posts[1]+new Vector3(0,3,0))])!.Clear,
            "Same-id neighbouring or elevated posts cannot satisfy the own missing member");
        foreach(var warningAt in new[]{f.Center,f.Posts[0]})
            check(!Evaluate(f,[..open,Signal(warningAt,2,LiveType.TRAFFICLIGHT)])!.Clear,
                "An explicitly observed own red warning vetoes otherwise open gates");
        check(Evaluate(f,[..open,Signal(f.Center+new Vector3(25,0,0),2,LiveType.TRAFFICLIGHT)])!.Clear,
            "An unrelated same-id red light does not become an own crossing warning");
        foreach(string mappedType in new[]{"traffic_light","unsupported"}) {
            var extended=Build();var warningPoint=new Vector3(14,.25f,35);
            extended.Descriptor.Semaphores.Add(new(){SemaphoreId=2,Type=MapType.UseProfile,Profile="crossing",Position=warningPoint});
            var mapped=new Dictionary<string,RailSignalProfile>{{"crossing",new(
                ["barrier_distance","model_only",mappedType],["","gate","warning"],[0])}};
            check(Evaluate(extended,[..Open(extended),Signal(warningPoint,2,LiveType.TRAFFICLIGHT)],mapped) is not {Clear:true},
                "A remapped own warning or unsupported profile member cannot be silently omitted from an otherwise open group: "+mappedType);
        }
        var explicitMember=Build();explicitMember.Descriptor.Semaphores[2].Type=MapType.ModelOnly;
        check(Evaluate(explicitMember,[Signal(explicitMember.Posts[0]),Signal(explicitMember.Posts[1],1)]) is not {Clear:true},
            "An explicit ModelOnly map member cannot disappear from the group and conceal a closed own post");
        var ambiguous=Build();ambiguous.Descriptor.Semaphores[2].Position=ambiguous.Descriptor.Semaphores[1].Position;
        check(Evaluate(ambiguous,Open(ambiguous)) is not {Clear:true},
            "Coincident expected locators cannot each claim the same physical actor");
        var malformedCenter=Build();malformedCenter.Controller.Position=new(float.NaN,0,35);
        check(Evaluate(malformedCenter,Open(malformedCenter)) is not {Clear:true},
            "A malformed controller anchor cannot bypass its warning-light matching while retaining open post clearance");
        var transformed=Build(new(120,3,-80),Matrix4x4.CreateRotationY(.7f));
        check(Evaluate(transformed,Open(transformed)) is {Clear:true},
            "Expected post anchors use the verified prefab translation and rotation composition");
        check(Evaluate(transformed,open) is not {Clear:true},
            "Open actors from a different prefab placement cannot clear transformed crossing members");
        check(!Policy(f,open,new Dictionary<string,RailSignalProfile>()).Clear,
            "An unresolved profile cannot silently release an enabled crossing with no central light actor");
        var known=Policy(f,open);var closed=Policy(f,[Signal(f.Posts[0],1),open[1]]);
        check(known.StopDistance==null&&!closed.Clear&&closed.StopDistance is >0&&closed.StopIdentity?.StartsWith("crossing:")==true,
            "Actual traffic policy retains an upstream crossing stop only while the bound group is not open");
        var observations=new List<object>();var repeated=Policy(f,[Signal(f.Posts[0],1),open[1]],observations:observations);
        check(f.Path.GetSemaphores().Count()>1&&observations.Count==1&&Math.Abs(repeated.StopDistance!.Value-15)<.01,
            "Repeated controller references share one crossing group and the earliest approach stop line");
        var afterApproach=TollPlugin.EvaluateTrafficLights(f.Plan,f.Trace,new(0,0,30),
            [Signal(f.Posts[0],1),open[1]],null,railProfiles:Profiles());
        check(afterApproach.Clear&&afterApproach.StopDistance==null,
            "Passing the group's approach cannot reinterpret a later reference as a new stop in the crossing");
    }

    static Unit Unit(string name)
    {
        var u=new Unit("tr_semaphore_profile","tr_sem_prof."+name);
        u.Attributes["type"]=new List<string>{"barrier_distance","model_only"};
        u.Attributes["model"]=new List<string>{"","crossing_r"};
        u.Attributes["id_map"]=new List<int>{0};return u;
    }
    public static void Parser(Action<bool,string> check)
    {
        var baseline=Unit("crossing");var loaded=RailCrossingProfiles.FromUnits([baseline]);
        check(loaded.TryGetValue("crossing",out var profile)&&profile.Type(19)=="model_only"&&profile.Model(19)=="crossing_r"&&profile.MappedId(19)==0,
            "SCS profile arrays repeat their final entry for higher locator ids");
        var child=new Unit("tr_semaphore_profile","tr_sem_prof.inherited");child.Attributes["inherited"]=new LinkPointer("tr_sem_prof.crossing");
        child.Attributes["model"]=new List<string>{"","different_visual"};
        var inherited=RailCrossingProfiles.FromUnits([baseline,child]);
        check(inherited.TryGetValue("inherited",out var result)&&result.Type(0)=="barrier_distance"&&result.MappedId(1)==0&&result.Model(1)=="different_visual",
            "An inherited profile retains parent type/remapping while applying its explicitly provided model array");
        var country=Unit("crossing.country");country.Attributes["model"]=new List<string>{"","country_visual"};
        check(RailCrossingProfiles.FromUnits([baseline,country]).ContainsKey("crossing"),
            "Country-specific visuals with identical member presence and controller semantics remain usable");
        country.Attributes["id_map"]=new List<int>{0,1};
        check(!RailCrossingProfiles.FromUnits([baseline,country]).ContainsKey("crossing"),
            "A country-specific remapping conflict cannot grant an unspecified-country crossing clearance");
        country=Unit("crossing.country");country.Attributes["model"]=new List<string>{"",""};
        check(!RailCrossingProfiles.FromUnits([baseline,country]).ContainsKey("crossing"),
            "A country variant that removes an expected post is not equivalent to the base profile");
        var cyclic=new Unit("tr_semaphore_profile","tr_sem_prof.cycle");cyclic.Attributes["inherited"]=new LinkPointer(cyclic.Name);
        check(!RailCrossingProfiles.FromUnits([cyclic]).ContainsKey("cycle"),"Cyclic profile inheritance stays unresolved");
        var orphan=new Unit("tr_semaphore_profile","tr_sem_prof.orphan");orphan.Attributes["inherited"]=new LinkPointer("tr_sem_prof.absent");
        check(!RailCrossingProfiles.FromUnits([orphan]).ContainsKey("orphan"),"A missing inherited profile does not fabricate member defaults");
        check(!RailCrossingProfiles.FromUnits([baseline,Unit("crossing")]).ContainsKey("crossing"),
            "Duplicate profile definitions cannot choose an arbitrary binding winner");
        var malformed=Unit("malformed");malformed.Attributes["model"]=new List<object>{"",17};
        check(!RailCrossingProfiles.FromUnits([malformed]).ContainsKey("malformed"),
            "An invalid model value cannot be converted into an intentionally absent expected actor");
        var empty=new Unit("tr_semaphore_profile","tr_sem_prof.empty");empty.Attributes["inherited"]=new LinkPointer("tr_sem_prof.crossing");
        empty.Attributes["model"]=new List<string>();
        check(!RailCrossingProfiles.FromUnits([baseline,empty]).ContainsKey("empty"),
            "An explicitly empty array is not an absent attribute that silently inherits a usable model list");
    }

    public static void TickPolicy(Action<bool,string> check)
    {
        foreach(bool clear in new[]{true,false}) {
            var driver=new RollingStartIntegration.Fixture(withoutAcc:true,writeDiagnostic:(_,_)=>{});
            try {
                driver.Set("options",new TollOptions{CurvePreviewEnabled=false,BriskOpenRoads=false});
                driver.F8();driver.Probe();driver.State.DesiredSpeed=25;
                var mapped=Build(driver.Point);var signals=Open(mapped);if(!clear)signals[1].state=0;
                driver.Traffic=()=>Policy(mapped,signals);
                driver.Fresh();driver.Plugin.Tick();
                var command=driver.Outputs.LastOrDefault(e=>e.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
                check(command!=null&&driver.State.EnableAssists&&!driver.Plugin.FaultBraking&&
                    (clear?command.Variables.aforward>0&&command.Variables.abackward==0:command.Variables.aforward==0&&command.Variables.abackward<0),
                    "Actual Tick consumes independently bound crossing policy as ordinary propulsion/service braking: "+clear);
                if(!clear) {
                    foreach(var signal in signals)signal.state=3;
                    Thread.Sleep(20);driver.Fresh();driver.Plugin.Tick();
                    var released=driver.Outputs.LastOrDefault(e=>e.ChannelDefinition.Id==TollPlugin.LongitudinalChannelId);
                    check(released!=null&&released.Variables.abackward==0&&released.Variables.aforward>0&&
                        driver.State.EnableAssists&&!driver.Plugin.FaultBraking,
                        "Opening the complete crossing group ends its ordinary brake and resumes bounded propulsion in the same active Tick session");
                }
            } finally {driver.Call("StopDiagnostics");}
        }
    }
}
