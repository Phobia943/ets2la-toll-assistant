using System.Diagnostics;
using System.Reflection;
using MautAssistent;

public static class NativeAccDiagnosticsIntegration
{
    sealed class Fields
    {
        #pragma warning disable CS0414 // Deliberate native-style private fields read by reflection.
        float currentSpeed=5,currentAcceleration=-1,targetAcceleration=.35f,pedalOutput=-.1f;
        #pragma warning restore CS0414
        Dictionary<string,float> constraintValues=new(){{"native-leading",-.6f},{"optional",float.NaN}};
        public Dictionary<string,float> Values=>constraintValues;
        public float Trap=>throw new Exception("Property must never be read");
    }
    static Type Type=>typeof(TollPlugin).Assembly.GetType("MautAssistent.NativeAccDiagnostics")!;
    static object Read(object source)=>Type.GetMethod("ReadFields",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[source,Stopwatch.GetTimestamp()])!;
    static T Value<T>(object result,string property)=>(T)result.GetType().GetProperty(property)!.GetValue(result)!;
    public static void BoundedReadOnly(Action<bool,string> assert)
    {
        var fields=new Fields();var snapshot=Read(fields);
        assert(Value<bool>(snapshot,"Supported")&&!Value<bool>(snapshot,"AtomicSnapshot"),"Primitive observations explicitly do not claim an atomic native tick");
        assert(Value<float>(snapshot,"TargetAcceleration")==.35f&&Value<float>(snapshot,"PedalOutput")==-.1f,
            "Requested acceleration and actual native pedal field remain separate evidence");
        var copied=Value<IReadOnlyDictionary<string,float>>(snapshot,"Constraints");fields.Values["native-leading"]=1;
        assert(copied["native-leading"]==-.6f&&float.IsNaN(copied["optional"]),"Snapshot is detached and retains unknown/NaN rather than turning it into clearance");
        fields.Values.Clear();for(int i=0;i<65;i++)fields.Values.Add(i.ToString(),0);
        assert(!Value<bool>(Read(fields),"Supported"),"An oversized constraint collection is unavailable, never unbounded work");
        var untrusted=Type.GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[new Fields()])!;
        assert(!Value<bool>(untrusted,"Supported"),"Production capture refuses an unverified source even when field names match");
        var f=new RollingStartIntegration.Fixture(writeDiagnostic:(_,_)=>{});f.F8();f.Probe();
        f.DuringReadAcc=()=>throw new Exception("RecordMotion must not call the provider");
        f.Call("RecordMotion",f.Now,true);
        assert(!f.Plugin.FaultBraking,"Motion diagnostics neither invoke the foreign provider nor alter fault state");
        f.Call("StopDiagnostics");
    }
}
