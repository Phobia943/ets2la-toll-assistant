using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MautHostCompatibility;

internal static class PrefabRoutingReplay
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static void Run(Assembly game, Action<bool,string> assert)
    {
        var parsed = game.GetType("ETS2LA.Game.Data.ParsedPrefab",true)!;
        var host = RuntimeHelpers.GetUninitializedObject(parsed);
        var descriptorField = parsed.GetField("Descriptor")!;
        var descriptor = Activator.CreateInstance(descriptorField.FieldType)!;
        descriptorField.SetValue(host,descriptor);
        static object? Get(object owner,string name) => owner.GetType().GetProperty(name)?.GetValue(owner) ?? owner.GetType().GetField(name)!.GetValue(owner);
        static void Set(object owner,string name,object value)
        {
            if(owner.GetType().GetProperty(name) is {} p) p.SetValue(owner,value);
            else owner.GetType().GetField(name)!.SetValue(owner,value);
        }
        var curves = (IList)Get(descriptor,"NavCurves")!;
        var curveType = curves.GetType().GetGenericArguments()[0];
        object Curve(float from,float to)
        {
            var curve = Activator.CreateInstance(curveType)!;
            Set(curve,"StartPosition",new Vector3(from,0,0));
            Set(curve,"EndPosition",new Vector3(to,0,0));
            Set(curve,"StartRotation",Quaternion.Identity);
            Set(curve,"EndRotation",Quaternion.Identity);
            Set(curve,"Length",Math.Abs(to-from));
            curves.Add(curve);return curve;
        }
        var a=Curve(0,10);var b=Curve(10,20);var c=Curve(20,30);var wrong=Curve(40,0);
        IList<int> Links(object curve,string name)=>(IList<int>)Get(curve,name)!;
        Links(a,"NextLines").Add(1);Links(b,"NextLines").Add(2);
        Links(a,"PreviousLines").Add(3);Links(wrong,"NextLines").Add(0);Links(wrong,"NextLines").Add(2);
        var traverse=parsed.GetMethod("TraverseCurveUntilTarget",All)!;
        IList Find(object from,object to)=>(IList)traverse.Invoke(host,[from,to])!;
        var found=Find(a,c);
        assert(found.Cast<object>().SequenceEqual(new[]{a,b,c}),"Actual native prefab search cannot shortcut through a previous curve");
        assert(Find(c,a).Count==0,"A physically reversed graph is not a second driving direction");
        assert(Find(b,b).Count==1,"A valid single-curve route remains available");
        Links(b,"NextLines").Add(0);Links(c,"NextLines").Add(-1);Links(c,"NextLines").Add(999);
        assert(Find(a,c).Count==3&&Find(c,a).Count==0,"Cycles and invalid link indices cannot invent a route or loop forever");
        Links(b,"NextLines").Clear();
        assert(Find(a,c).Count==0,"A missing directed continuation stays missing instead of using the reverse graph");
        Links(b,"NextLines").Add(2);Set(b,"StartPosition",new Vector3(25,0,0));
        assert(Find(a,c).Count==0,"A declared next link with disconnected endpoints is not drivable");
        Set(b,"StartPosition",new Vector3(10,0,0));Set(b,"Length",float.NaN);
        assert(Find(a,c).Count==0,"Invalid curve geometry cannot become a connecting route");
        Set(b,"Length",10f);
        var foreign=Activator.CreateInstance(curveType)!;
        assert(Find(foreign,c).Count==0,"A curve outside the current descriptor cannot be a search entry");
        assert(Links(a,"PreviousLines").Count==1&&Links(a,"NextLines").Count==1,"Searching leaves canonical map adjacency untouched");

        var backA=Curve(30,20);var backB=Curve(20,10);var backC=Curve(10,0);
        Links(backA,"NextLines").Add(5);Links(backB,"NextLines").Add(6);
        assert(Find(backA,backC).Cast<object>().SequenceEqual(new[]{backA,backB,backC})&&Find(a,c).Count==3,
            "Both traffic directions remain reachable through their own directed lane chains");
        assert(Find(a,backC).Count==0&&Find(backA,c).Count==0,"Opposing lanes cannot be spliced together by geometric proximity");

        // Native Backward is not permission to concatenate PreviousLines. Its
        // interpolation still follows each stored curve from Start to End.
        Set(b,"EndPosition",new Vector3(30,0,0));Set(b,"Length",20f);
        Set(c,"StartPosition",new Vector3(30,0,0));Set(c,"EndPosition",new Vector3(60,0,0));Set(c,"Length",30f);
        var pathType=game.GetType("ETS2LA.Game.Data.PrefabPath",true)!;
        var ctor=pathType.GetConstructors().Single();var parameters=ctor.GetParameters();
        var start=Activator.CreateInstance(parameters[1].ParameterType)!;
        var end=Activator.CreateInstance(parameters[2].ParameterType)!;
        Vector3[]? forwardSamples=null;
        foreach(string direction in new[]{"Forward","Backward"})
        {
            var path=ctor.Invoke([host,start,end,Find(a,c),Enum.Parse(parameters[4].ParameterType,direction),Matrix4x4.Identity,Vector3.Zero]);
            var interpolate=pathType.GetMethod("Interpolate")!;
            Vector3 At(float t)=>(Vector3)Get(interpolate.Invoke(path,[t,0f])!,"Position")!;
            assert(Vector3.Distance(At(.0001f),new(0,0,0))<.01f&&Vector3.Distance(At(.9999f),new(60,0,0))<.01f,
                "Directed native endpoints remain correct with interpolation mode "+direction);
            var samples=new[]{.05f,.125f,.25f,.4f,.6f,.75f,.9f}.Select(At).ToArray();
            if(forwardSamples==null) forwardSamples=samples;
            else assert(samples.Zip(forwardSamples).All(pair=>Vector3.Distance(pair.First,pair.Second)<.001f),
                "Native Forward and Backward preserve all interior positions with unequal curve lengths");
        }
    }
}
