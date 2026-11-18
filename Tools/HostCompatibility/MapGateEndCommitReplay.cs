using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
namespace MautHostCompatibility;

internal static class MapGateEndCommitReplay
{
    internal static object Create(string fixtureFile,string sandbox,AssemblyLoadContext context,Assembly plugin,Vector3 truck)
    {
        string copy=Path.Combine(sandbox,"Tests.dll");File.Copy(Path.GetFullPath(fixtureFile),copy,false);
        using var locked=new FileStream(copy,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var module=ModuleDefinition.ReadModule(copy);
        if(module.GetType("<Module>").Methods.Any(m=>m.IsConstructor))throw new InvalidDataException("Gate fixture module initializer forbidden");
        var root=module.GetType("MapGateEndCommitHostFixture")??throw new InvalidDataException("Missing gate-end fixture");
        void Audit(TypeDefinition type) {
            foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var instruction in method.Body.Instructions) {
                if(instruction.OpCode.Code is Code.Calli or Code.Jmp)throw new InvalidDataException("Indirect gate fixture call forbidden");
                if(instruction.Operand is not MethodReference call)continue;
                string owner=call.DeclaringType.GetElementType().FullName;
                bool allowed=owner.StartsWith("MapGateEndCommitHostFixture",StringComparison.Ordinal)||
                    owner.StartsWith("MautAssistent.",StringComparison.Ordinal)||
                    owner.StartsWith("System.",StringComparison.Ordinal)&&!owner.StartsWith("System.IO.",StringComparison.Ordinal)&&
                    !owner.StartsWith("System.Threading.",StringComparison.Ordinal);
                if(!allowed||call.Name=="get_Current")throw new InvalidDataException("Unaudited gate fixture call: "+call.FullName);
            }
            foreach(var nested in type.NestedTypes)Audit(nested);
        }
        Audit(root);
        var assembly=context.LoadFromAssemblyPath(copy);
        if(assembly.ManifestModule.ModuleVersionId!=module.Mvid||AssemblyLoadContext.GetLoadContext(assembly)!=context||
            context.LoadFromAssemblyName(new AssemblyName("MautAssistent"))!=plugin)
            throw new InvalidDataException("Gate fixture escaped isolated product context");
        return assembly.GetType("MapGateEndCommitHostFixture",true)!.GetMethod("Create")!.Invoke(null,[truck])!;
    }
}
