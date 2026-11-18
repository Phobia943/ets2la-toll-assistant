using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using B=System.Reflection.BindingFlags;
namespace MautHostCompatibility;

// Only the existing Replay's sealed isolated context is accepted. Its actual
// GameTelemetry singleton/data accessors must be private field reads, so the
// unmodified native PathLib interpolation cannot discover or open live SDKs.
internal static class SlowExitHostReplay
{
    const B All=B.Public|B.NonPublic|B.Static|B.Instance;
    internal static int Run(string fixtureFile,string sandbox,AssemblyLoadContext context,Assembly plugin,Assembly game,
        object state,Action<bool,string> check)
    {
        if(context==AssemblyLoadContext.Default||AssemblyLoadContext.GetLoadContext(plugin)!=context||
            AssemblyLoadContext.GetLoadContext(game)!=context||AssemblyLoadContext.GetLoadContext(state.GetType().Assembly)!=context)
            throw new InvalidDataException("Slow exit requires the already sealed isolated host context");
        using(var host=ModuleDefinition.ReadModule(game.Location)) {
            var telemetry=host.GetType("ETS2LA.Game.Telemetry.GameTelemetry");
            foreach(var (name,field) in new[]{("get_Current","ReplayCurrent"),("GetCurrentData","ReplayData")}) {
                var body=telemetry.Methods.Single(m=>m.Name==name).Body.Instructions.Where(i=>i.OpCode.Code!=Code.Nop).ToArray();
                if(body.Length!=2||body[0].OpCode.Code!=Code.Ldsfld||body[0].Operand is not FieldReference f||
                    f.Name!=field||body[1].OpCode.Code!=Code.Ret)
                    throw new InvalidDataException("Native telemetry accessor is not a hermetic DTO read: "+name);
            }
        }
        string copied=Path.Combine(sandbox,"Tests.dll");File.Copy(Path.GetFullPath(fixtureFile),copied,false);
        using var seal=new FileStream(copied,FileMode.Open,FileAccess.Read,FileShare.Read);
        string hash=Convert.ToHexString(SHA256.HashData(seal));
        using var module=ModuleDefinition.ReadModule(copied);
        if(module.GetType("<Module>").Methods.Any(m=>m.IsConstructor))throw new InvalidDataException("Fixture module initializer forbidden");
        var root=module.GetType("RoutingContinuationIntegration")??throw new InvalidDataException("Missing slow-exit fixture");
        var visited=new HashSet<string>();
        void Audit(MethodDefinition method) {
            if(!visited.Add(method.FullName)||!method.HasBody)return;
            if(method.DeclaringType.Methods.FirstOrDefault(m=>m.IsConstructor&&m.IsStatic) is { } init)Audit(init);
            foreach(var i in method.Body.Instructions) {
                if(i.OpCode.Code is Code.Calli or Code.Jmp)throw new InvalidDataException("Indirect fixture call forbidden");
                if(i.Operand is not MethodReference call||i.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn))continue;
                string owner=call.DeclaringType.GetElementType().FullName;
                if(owner.StartsWith("RoutingContinuationIntegration",StringComparison.Ordinal)){Audit(call.Resolve());continue;}
                bool allowed=owner.StartsWith("System.",StringComparison.Ordinal)&&!owner.StartsWith("System.IO.",StringComparison.Ordinal)&&
                    !owner.StartsWith("System.Threading.Tasks.",StringComparison.Ordinal)||
                    owner=="System.IO.Path"&&call.Name is "GetTempPath" or "Combine"||
                    owner.StartsWith("MautAssistent.",StringComparison.Ordinal)||owner.StartsWith("PathLib.",StringComparison.Ordinal)||
                    owner.StartsWith("TruckLib.",StringComparison.Ordinal)||owner is "ETS2LA.Controls.ControlChangeEventArgs" or
                    "ETS2LA.Game.Data.ParsedRoad" or "ETS2LA.Game.Data.ParsedRoadList" or "ETS2LA.Game.Data.PrefabPath" or
                    "ETS2LA.Game.Telemetry.GameTelemetry" or "ETS2LA.State.ApplicationState";
                if(!allowed||call.Name=="get_Current")throw new InvalidDataException("Unaudited slow-exit fixture call: "+call.FullName);
            }
        }
        Audit(root.Methods.Single(m=>m.Name=="SlowExitNative"));
        var loaded=context.LoadFromAssemblyPath(copied);
        if(loaded.ManifestModule.ModuleVersionId!=module.Mvid||context.LoadFromAssemblyName(new AssemblyName("MautAssistent"))!=plugin)
            throw new InvalidDataException("Slow-exit fixture escaped pinned product context");
        var telemetryType=game.GetType("ETS2LA.Game.Telemetry.GameTelemetry",true)!;
        var data=telemetryType.GetField("ReplayData")!.GetValue(null)!;
        object ObjectField(object owner,string name) {
            var f=owner.GetType().GetField(name)!;return f.GetValue(owner)??
                (f.FieldType.IsValueType?Activator.CreateInstance(f.FieldType)!:System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(f.FieldType));
        }
        void Scalar(object owner,string name,double value) {
            var field=owner.GetType().GetField(name,All|B.IgnoreCase);
            if(field!=null)field.SetValue(owner,Convert.ChangeType(value,field.FieldType));
            else {var p=owner.GetType().GetProperty(name,All|B.IgnoreCase)!;p.SetValue(owner,Convert.ChangeType(value,p.PropertyType));}
        }
        Action<Vector3,double> place=(position,speed)=>{
            var placement=ObjectField(data,"truckPlacement");var coordinate=ObjectField(placement,"coordinate");
            Scalar(coordinate,"x",position.X);Scalar(coordinate,"y",position.Y);Scalar(coordinate,"z",position.Z);
            placement.GetType().GetField("coordinate")!.SetValue(placement,coordinate);data.GetType().GetField("truckPlacement")!.SetValue(data,placement);
            var floats=ObjectField(data,"truckFloat");Scalar(floats,"speed",speed);Scalar(floats,"speedLimit",100/3.6);
            data.GetType().GetField("truckFloat")!.SetValue(data,floats);
        };
        int checks=0;Action<bool,string> counted=(valid,reason)=>{check(valid,reason);checks++;};
        loaded.GetType("RoutingContinuationIntegration",true)!.GetMethod("SlowExitNative")!.Invoke(null,[counted,place,state]);
        File.WriteAllText(Path.Combine(sandbox,"slow-exit-replay.json"),System.Text.Json.JsonSerializer.Serialize(new{
            checks,passed=true,fixtureSha256=hash,fixtureMvid=module.Mvid,pluginMvid=plugin.ManifestModule.ModuleVersionId,
            syntheticMap=true,actualNativePathInterpolation=true,actualRouterApply=true,actualF9=true,liveMapsOpened=false}));
        return checks;
    }
}
