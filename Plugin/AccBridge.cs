using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace MautAssistent;

/// <summary>
/// ACC 2.1.0 compatibility adapter. Plugin assemblies have isolated load contexts,
/// so the small subclass must derive from the *loaded* ACC constraint type.
/// No patches to DLLs, private fields, output channels or desired speed.
/// </summary>
public sealed class AccBridge
{
    public const string ConstraintName = "Mautassistent (experimentell)";
    public object? Constraint { get; private set; }
    object? owner;
    FieldInfo? listField;
    volatile bool live;

    public void Attach(object acc, Func<float> evaluate, Func<string> status)
    {
        if (ReferenceEquals(owner, acc) && Constraint != null)
        {
            var current = (IList)listField!.GetValue(acc)!;
            if (current.Contains(Constraint)) return;
        }
        Detach();
        var type = acc.GetType();
        var field = type.GetField("constraints") ?? throw new NotSupportedException("ACC-Begrenzungen nicht verfügbar.");
        var baseType = type.Assembly.GetType("AdaptiveCruiseControl.Constraints.Constraint", true)!;
        if (field.FieldType != typeof(List<>).MakeGenericType(baseType)) throw new NotSupportedException("ACC-Schnittstelle geändert.");
        var original = (IList)field.GetValue(acc)!;
        // Wait until OnEnable has installed the three native constraints.
        if (original.Count < 3) return;
        live = true;
        var bridge = CreateConstraint(baseType, () => live ? evaluate() : float.NaN, status);
        var replacement = (IList)Activator.CreateInstance(field.FieldType)!;
        foreach (var item in original) replacement.Add(item);
        replacement.Add(bridge);
        // Swap the list rather than invalidating an in-progress ACC enumeration.
        field.SetValue(acc, replacement);
        owner = acc; listField = field; Constraint = bridge;
    }

    public void Detach()
    {
        live = false; // Any old enumerator immediately receives a neutral bound.
        if (owner != null && listField != null && Constraint != null)
        {
            var original = (IList)listField.GetValue(owner)!;
            var replacement = (IList)Activator.CreateInstance(listField.FieldType)!;
            foreach (var item in original) if (!ReferenceEquals(item, Constraint)) replacement.Add(item);
            listField.SetValue(owner, replacement);
        }
        owner = null; listField = null; Constraint = null;
    }

    public static object CreateConstraint(Type baseType, Func<float> evaluate, Func<string> status)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new("MautBridge_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Bridge").DefineType("MautConstraint", TypeAttributes.Public|TypeAttributes.Sealed, baseType);
        var evalField = type.DefineField("Evaluate", typeof(Func<float>), FieldAttributes.Public);
        var statusField = type.DefineField("ReadStatus", typeof(Func<string>), FieldAttributes.Public);
        var ctor = baseType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic, null, Type.EmptyTypes, null)!;
        var ci = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes).GetILGenerator();
        ci.Emit(OpCodes.Ldarg_0); ci.Emit(OpCodes.Call, ctor); ci.Emit(OpCodes.Ret);
        foreach (var method in baseType.GetMethods().Where(m => m.IsAbstract))
        {
            var mb = type.DefineMethod(method.Name, MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.HideBySig,
                method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
            var il = mb.GetILGenerator();
            switch (method.Name)
            {
                case "get_Name": il.Emit(OpCodes.Ldstr, ConstraintName); break;
                case "get_Status": il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, statusField); il.Emit(OpCodes.Callvirt, typeof(Func<string>).GetMethod("Invoke")!); break;
                case "set_Status": break;
                case "GetMaxAcceleration": il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, evalField); il.Emit(OpCodes.Callvirt, typeof(Func<float>).GetMethod("Invoke")!); break;
                case "GetLogData":
                    il.Emit(OpCodes.Newobj, method.ReturnType.GetConstructor(Type.EmptyTypes)!);
                    il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldstr, ConstraintName);
                    il.Emit(OpCodes.Callvirt, method.ReturnType.GetProperty("ConstraintName")!.SetMethod!);
                    // A log read must not advance the controller a second time.
                    il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_R4, float.NaN);
                    il.Emit(OpCodes.Callvirt, method.ReturnType.GetProperty("MaxAcceleration")!.SetMethod!); break;
                default: throw new NotSupportedException("Unbekannte ACC-Methode: " + method.Name);
            }
            il.Emit(OpCodes.Ret); type.DefineMethodOverride(mb, method);
        }
        var built = type.CreateType()!;
        var instance = Activator.CreateInstance(built)!;
        built.GetField("Evaluate")!.SetValue(instance, evaluate);
        built.GetField("ReadStatus")!.SetValue(instance, status);
        return instance;
    }
}
