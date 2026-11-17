using System.Reflection;
using System.Numerics;
using PathLib;
namespace MautAssistent;

// Independent adversaries against the actual nonempty plugin control path.
// Reuse only the existing synthetic road/source fixture; no plugin Init,
// singleton constructors, live maps or vehicle outputs are opened here.
internal static class StandardPolicyReviewTests
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    sealed class Subject
    {
        readonly Type type=typeof(RigControlIntegration).GetNestedType("Fixture",BindingFlags.NonPublic)!;
        readonly object value;
        internal Subject()
        {
            value=Activator.CreateInstance(type,Fields,null,new object[]{false},null)!;
            // The shared fixture increments Reads in a null-conditional
            // callback argument, so install a no-op to count real reads too.
            DuringRead=_=>{};
            type.GetMethod("Frame",Fields)!.Invoke(value,new object[]{40f,0d,0d,false,true});
            Observation=Observation with{Context=null,Envelope=null};
        }
        T Get<T>(string name)=>(T)type.GetField(name,Fields)!.GetValue(value)!;
        internal RollingStartIntegration.Fixture Driver=>Get<RollingStartIntegration.Fixture>("Driver");
        internal RigRuntimeObservation Observation {
            get=>Get<RigRuntimeObservation>("Observation");
            set=>type.GetField("Observation",Fields)!.SetValue(this.value,value);
        }
        internal Action<int>? DuringRead {set=>type.GetField("DuringRead",Fields)!.SetValue(this.value,value);}
        internal int Reads=>Get<int>("Reads");
        internal float Limit()=>(float)type.GetMethod("Limit",Fields)!.Invoke(value,null)!;
        internal void Bodies(params SceneBody[] bodies)=>type.GetField("Bodies",Fields)!.SetValue(value,bodies);
        internal SceneBody Body(float x,float z)=>(SceneBody)type.GetMethod("Body",Fields)!.Invoke(value,new object[]{x,z,false})!;
        internal void NextFrame()
        {
            type.GetMethod("Frame",Fields)!.Invoke(value,new object[]{40f,0d,0d,false,true});
            Observation=Observation with{Context=null,Envelope=null};
        }
    }
    internal static void SourceTransactions(Action<bool,string> assert)
    {
        var separate=new Subject();
        separate.Observation=separate.Observation with{Scene=separate.Observation.Scene! with{Session=777}};
        float initialLimit=separate.Limit();
        assert(initialLimit>.3f&&separate.Reads>=2,
            "Actual standard control accepts independent native/scene session IDs, with a real source re-read; limit="+initialLimit+
            ", reads="+separate.Reads+", status="+separate.Driver.Get<string>("sceneStatus"));
        assert(separate.Observation.Context==null&&separate.Observation.Envelope==null,
            "That standard permission required neither cargo nor axle proof");

        foreach(string changed in new[]{"native-session","scene-session","scene-generation"}) {
            var s=new Subject();
            s.DuringRead=n=> {
                if(n!=2)return;
                var o=s.Observation;
                s.Observation=changed switch {
                    "native-session"=>o with{MeasuredShape=o.MeasuredShape! with{Session=o.MeasuredShape!.Session+1}},
                    "scene-session"=>o with{Scene=o.Scene! with{Session=o.Scene!.Session+1}},
                    _=>o with{Scene=o.Scene! with{Generation=o.Scene!.Generation+1}}
                };
            };
            float limit=s.Limit();
            assert(s.Reads>=2&&limit<0&&!s.Driver.Plugin.FaultBraking,
                "A "+changed+" change during actual source validation withdraws standard clearance with an ordinary stop");
        }

        var expired=new Subject();
        expired.DuringRead=n=> {
            if(n!=2)return;
            // A genuinely newer final capture cannot rejuvenate the original
            // frame against which obstacle absence was actually calculated.
            Thread.Sleep(240);
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var o=expired.Observation;
            expired.Observation=o with{
                MeasuredShape=o.MeasuredShape! with{UtcMs=now,Sequence=o.MeasuredShape!.Sequence+2},
                Scene=o.Scene! with{UtcMs=now,Sequence=o.Scene!.Sequence+2}
            };
        };
        assert(expired.Limit()<0&&expired.Reads>=2&&!expired.Driver.Plugin.FaultBraking,
            "A fresh end capture cannot authorize a >200ms-old original standard scene/native transaction");
    }
    internal static void DetailIsolationAndCancellation(Action<bool,string> assert)
    {
        var s=new Subject();
        var monitor=s.Driver.Get<RigModelMonitor>("rigMonitor");
        typeof(RigModelMonitor).GetField("failure",Fields)!.SetValue(monitor,"prior detailed-only model departure");
        s.Driver.Set("holdPedalFloor",.65f);
        assert(monitor.Faulted,"The independent standard test begins with an actual detailed monitor latch");
        assert(s.Limit()>.3f&&!monitor.Faulted&&s.Driver.Get<float>("scenePedalFloor")==0,
            "Unknown cargo plus a prior detailed-only latch cannot create a hidden standard scene stop");
        assert(s.Driver.Get<float>("holdPedalFloor")==.65f,
            "Clearing a detailed/scene latch does not erase a separate branch's physical hold floor");

        var missing=new Subject();missing.Observation=missing.Observation with{MeasuredShape=null};
        assert(missing.Limit()<0&&!missing.Driver.Plugin.FaultBraking,
            "A valid-looking scene without its actual raw process/pose anchor cannot clear the standard corridor");

        var cancelled=new Subject();
        using(var operation=cancelled.Driver.Get<FaultBrake>("faultBrake").BeginOperation()) {
            cancelled.DuringRead=n=>{if(n==2)cancelled.Driver.F9();};
            cancelled.Limit();
            assert(cancelled.Reads>=2&&!cancelled.Driver.State.EnableAssists&&!cancelled.Driver.Plugin.Active&&
                cancelled.Driver.Get<float>("scenePedalFloor")==0&&cancelled.Driver.Get<float>("holdPedalFloor")==0,
                "F9 during a standard source transaction revokes all old physical hold authority");
        }
    }
    internal static void PracticalScopeAndMotion(Action<bool,string> assert)
    {
        var grade=new Subject();
        var own=grade.Observation.MeasuredShape!.Truck.Position;
        var item=grade.Driver.Get<PlannedPathData>("path").PlannedPath.Single();
        Vector3 Point(float delta)=>own+new Vector3(0,delta*.04f,delta);
        grade.Driver.Set("safetyTrace",new RouteTrace(
            new[]{Point(-140),Point(-40),Point(160),Point(360)},[],new[]{"approach",RouteMatcher.ItemIdentity(item),"exit"}));
        assert(grade.Limit()>.3f&&!grade.Driver.Plugin.FaultBraking,
            "A normal four-percent toll approach does not inherit the optional planar trailer-model rejection in standard guidance");

        var moving=new Subject();
        var body=moving.Body(4.5f,50);
        moving.Bodies(body);moving.NextFrame();
        assert(moving.Limit()>.3f,"An initially distant unknown-motion actor is outside the nominal standard corridor");
        Thread.Sleep(20);
        moving.Bodies(body with{Center=body.Center-new Vector3(.8f,0,0)});moving.NextFrame();
        assert(moving.Limit()<0&&!moving.Driver.Plugin.FaultBraking,
            "Observed rapid approach by the same physics-unknown actor retains history and cannot be reset to a fresh stationary assumption every evaluation");
    }
}
