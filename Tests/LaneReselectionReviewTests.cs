using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ETS2LA.Game.Data;
using MautAssistent;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

public static class LaneReselectionReviewTests
{
    sealed class Fixture
    {
        internal readonly TollRouter Router;
        internal readonly PlannedPathData Data;
        internal readonly PlannedItem Item;
        internal readonly PrefabPath Original;
        internal bool AutoClear;
        internal Func<PrefabPath,bool>? Override;
        internal Fixture()
        {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
            NavCurve Curve(Vector3 a,Vector3 b,int gate=-1)=>new(){StartPosition=a,EndPosition=b,
                StartRotation=q,EndRotation=q,Length=Vector3.Distance(a,b),SemaphoreId=gate,AllowedVehicles=AllowedVehicles.AllVehicles};
            var curves=new List<NavCurve>{Curve(new(0,0,-400),new(0,0,20))};
            foreach(var lane in new[]{(Gate:4,X:0f),(Gate:12,X:-6f)}) {
                int first=curves.Count;curves[0].NextLines.Add(first);
                curves.Add(Curve(new(0,0,20),new(lane.X,0,60)));
                curves.Add(Curve(new(lane.X,0,60),new(lane.X,0,80),lane.Gate));
                curves.Add(Curve(new(lane.X,0,80),new(0,0,120)));
                curves[first].NextLines.Add(first+1);curves[first+1].NextLines.Add(first+2);curves[first+2].NextLines.Add(7);
            }
            curves.Add(Curve(new(0,0,120),new(0,0,180)));
            var descriptor=new PrefabDescriptor{NavCurves=curves};
            descriptor.Semaphores.Add(new(){SemaphoreId=4,Position=new(2.5f,0,79.6f)});
            descriptor.Semaphores.Add(new(){SemaphoreId=12,Position=new(-3.5f,0,79.6f)});
            var parsed=(ParsedPrefab)RuntimeHelpers.GetUninitializedObject(typeof(ParsedPrefab));
            parsed.Prefab=new(){Uid=8800,Model="fixture",Variant="default"};parsed.Descriptor=descriptor;
            Original=new(parsed,new Node{Uid=8801},new Node{Uid=8802},new[]{0,1,2,3,7}.Select(i=>curves[i]).ToList(),
                Direction.Forward,Matrix4x4.Identity,Vector3.Zero);
            Item=new(){item=parsed,startNode=Original.StartNode,endNode=Original.EndNode,prefabPath=Original,direction=Direction.Forward,laneIndex=0};
            Data=new(){HasError=false,PlannedPath=[Item]};
            var prefs=new PreferenceStore(Path.Combine(Path.GetTempPath(),"critical-reselection-unused-"+Guid.NewGuid()+".json"));
            Router=new(prefs,_=>new Dictionary<int,TollPayment>{{4,TollPayment.Manual},{12,TollPayment.Automatic}},p=>{
                if(Override!=null)return Override(p);
                return AutoClear||!PrefabRefinement.OriginalFor(p).Curves.Any(c=>c.SemaphoreId==12);
            });
        }
        internal void Initial(Action<bool,string> check)
        {
            Router.Discover(Data,new(0,0,-300),[]);
            check(Router.SelectedGate==4,"A genuinely unavailable automatic certificate initially selects the reachable manual fallback");
            check(Router.Apply(Data,new(0,0,-300),out var error)&&error==null,"The manual fallback is actually applied, establishing a real replacement mapping");
            check(!ReferenceEquals(Item.prefabPath,Original)&&PrefabRefinement.OriginalFor(Item.prefabPath!).Curves.Any(c=>c.SemaphoreId==4),
                "The regression begins with an applied manual path rather than a selection-only stub");
        }
        internal void Search(Vector3 position,double speed=0)
        {
            // Retry timing is not the behavior under test. Avoid sleeps while
            // exercising the exact real Discover/upgrade path.
            typeof(TollRouter).GetField("lastSearch",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Router,double.NegativeInfinity);
            Router.Discover(Data,position,[],speed);
        }
    }
    public static void BeforeAndAfterCommit(Action<bool,string> check)
    {
        var f=new Fixture();f.Initial(check);var manual=f.Item.prefabPath;
        f.AutoClear=true;f.Search(new(0,0,-300));
        check(f.Router.SelectedGate==12&&f.Router.PaymentFor(12)==TollPayment.Automatic,"New positive automatic evidence is reconsidered before the physical split");
        check(ReferenceEquals(f.Item.prefabPath,manual),"Searching a new candidate alone does not mutate the published manual route");
        check(f.Router.Apply(f.Data,new(0,0,-300),out var error)&&error==null,"The automatic upgrade must pass actual Apply against the real current plan");
        var gates=PrefabRefinement.OriginalFor(f.Item.prefabPath!).Curves.Where(c=>c.SemaphoreId>=0).Select(c=>c.SemaphoreId).ToArray();
        check(gates.SequenceEqual(new[]{12})&&f.Router.SelectedBarrier?.GateId==12,
            "Old manual replacement ownership cannot leave the actual path manual while the selected gate claims automatic");
        f.Search(new(0,0,-250));
        check(f.Router.SelectedGate==12&&PrefabRefinement.OriginalFor(f.Item.prefabPath!).Curves.Any(c=>c.SemaphoreId==12),
            "A completed automatic upgrade remains stable on subsequent native updates");
        var late=new Fixture();late.Initial(check);var held=late.Item.prefabPath;var barrier=late.Router.SelectedBarrier;
        late.AutoClear=true;late.Search(new(0,0,40));
        check(late.Router.SelectedGate==4&&ReferenceEquals(late.Item.prefabPath,held)&&late.Router.SelectedBarrier==barrier,
            "An automatic option appearing after the split cannot replace an already applied manual lane");
        check(late.Router.Apply(late.Data,new(0,0,40),out error)&&error==null&&ReferenceEquals(late.Item.prefabPath,held),
            "After the split a failed optional upgrade leaves the existing manual replacement mapping usable");
    }
    public static void FailuresRetainOwnership(Action<bool,string> check)
    {
        var changing=new Fixture();changing.Initial(check);var manual=changing.Item.prefabPath;
        changing.AutoClear=true;changing.Item.targetLaneIndex=2;changing.Search(new(0,0,-300));
        check(changing.Router.SelectedGate==4&&ReferenceEquals(changing.Item.prefabPath,manual)&&changing.Item.targetLaneIndex==2,
            "An existing native lane-change target prevents an opportunistic automatic upgrade and is never overwritten");
        var revoked=new Fixture();revoked.Initial(check);manual=revoked.Item.prefabPath;var previousBarrier=revoked.Router.SelectedBarrier;
        int autoChecks=0;
        revoked.Override=p=>!PrefabRefinement.OriginalFor(p).Curves.Any(c=>c.SemaphoreId==12)||++autoChecks==1;
        revoked.Search(new(0,0,-300));
        check(autoChecks>=2&&revoked.Router.SelectedGate==4&&ReferenceEquals(revoked.Item.prefabPath,manual)&&revoked.Router.SelectedBarrier==previousBarrier,
            "An automatic proof revoked between candidate discovery and final validation cannot erase the committed manual selection");
        revoked.Override=null;
        check(revoked.Router.Apply(revoked.Data,new(0,0,-300),out var error)&&error==null&&ReferenceEquals(revoked.Item.prefabPath,manual),
            "A rejected candidate must not clear or corrupt the old manual replacement ownership");
        var absent=new Fixture();absent.Initial(check);manual=absent.Item.prefabPath;
        absent.Search(new(0,0,-300));
        check(absent.Router.SelectedGate==4&&ReferenceEquals(absent.Item.prefabPath,manual),"A failed search which still has no automatic proof leaves the exact manual route intact");
    }
}
