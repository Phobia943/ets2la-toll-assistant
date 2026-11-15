using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MautHostCompatibility;

internal static class Rewrite
{
    internal static MethodDefinition Method(TypeDefinition type,string name)=>type.Methods.Single(m=>m.Name==name);
    internal static void CopyBody(MethodDefinition source,MethodDefinition destination,TypeDefinition template,TypeDefinition target,TypeDefinition? externalOutput=null)
    {
        source.Body.SimplifyMacros();
        var module=target.Module;var body=new MethodBody(destination){InitLocals=source.Body.InitLocals};destination.Body=body;
        var instructions=new Dictionary<Instruction,Instruction>();
        foreach(var original in source.Body.Instructions){var copy=Instruction.Create(OpCodes.Nop);copy.OpCode=original.OpCode;instructions.Add(original,copy);body.Instructions.Add(copy);}
        foreach(var local in source.Body.Variables)body.Variables.Add(new(module.ImportReference(local.VariableType)));
        object? Operand(object? operand)=>operand switch {
            Instruction instruction=>instructions[instruction],
            Instruction[] branches=>branches.Select(i=>instructions[i]).ToArray(),
            VariableDefinition local=>body.Variables[local.Index],
            ParameterDefinition parameter=>parameter.Index<0?body.ThisParameter:destination.Parameters[parameter.Index],
            FieldReference field when field.DeclaringType.FullName==template.FullName=>target.Fields.Single(f=>f.Name==field.Name),
            MethodReference method when method.DeclaringType.FullName==template.FullName=>target.Methods.Single(m=>m.Name==method.Name&&m.Parameters.Count==method.Parameters.Count),
            MethodReference method when externalOutput!=null&&method.DeclaringType.FullName==typeof(OutputTemplate).FullName=>module.ImportReference(Method(externalOutput,method.Name)),
            MethodReference method=>module.ImportReference(method),
            FieldReference field=>module.ImportReference(field),
            TypeReference type=>type.FullName==template.FullName?target:module.ImportReference(type),
            CallSite site=>throw new NotSupportedException("Unexpected indirect call in template: "+site),
            _=>operand
        };
        foreach(var original in source.Body.Instructions)instructions[original].Operand=Operand(original.Operand);
        foreach(var handler in source.Body.ExceptionHandlers)body.ExceptionHandlers.Add(new(handler.HandlerType){
            CatchType=handler.CatchType==null?null:module.ImportReference(handler.CatchType),
            TryStart=instructions[handler.TryStart],TryEnd=handler.TryEnd==null?null:instructions[handler.TryEnd],
            HandlerStart=instructions[handler.HandlerStart],HandlerEnd=handler.HandlerEnd==null?null:instructions[handler.HandlerEnd],
            FilterStart=handler.FilterStart==null?null:instructions[handler.FilterStart]});
        body.OptimizeMacros();
    }
    internal static TypeDefinition Output(ModuleDefinition module,TypeDefinition template)
    {
        var target=module.GetType("ETS2LA.Game.Output.GameOutput")??throw new InvalidDataException("Output type missing");
        var definition=module.GetType("ETS2LA.Game.Output.ControlChannelDefinition");
        definition.Fields.Add(new("MautEpoch",FieldAttributes.Public,module.TypeSystem.Int64));
        var clone=new MethodDefinition("MautClone",MethodAttributes.Public|MethodAttributes.HideBySig,definition);
        var cloneIl=clone.Body.GetILProcessor();cloneIl.Emit(OpCodes.Ldarg_0);
        cloneIl.Emit(OpCodes.Call,module.ImportReference(typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!));
        cloneIl.Emit(OpCodes.Castclass,definition);cloneIl.Emit(OpCodes.Ret);definition.Methods.Add(clone);
        foreach(var field in template.Fields.Where(f=>f.Name.StartsWith("Maut",StringComparison.Ordinal))) {
            var copy=new FieldDefinition(field.Name,field.Attributes,module.ImportReference(field.FieldType));
            foreach(var attribute in field.CustomAttributes.Where(a=>a.AttributeType.FullName==typeof(ThreadStaticAttribute).FullName))
                copy.CustomAttributes.Add(new(module.ImportReference(attribute.Constructor)));
            if(field.HasConstant)copy.Constant=field.Constant;target.Fields.Add(copy);
        }
        foreach(var method in template.Methods.Where(m=>m.Name.StartsWith("Maut",StringComparison.Ordinal))) {
            var copy=new MethodDefinition(method.Name,method.Attributes,module.ImportReference(method.ReturnType));
            foreach(var parameter in method.Parameters)copy.Parameters.Add(new(parameter.Name,parameter.Attributes,module.ImportReference(parameter.ParameterType)));
            target.Methods.Add(copy);
        }
        foreach(var method in template.Methods.Where(m=>m.Name.StartsWith("Maut",StringComparison.Ordinal)||m.Name is "Tick" or "OnControlEvent"))
            CopyBody(method,Method(target,method.Name),template,target);
        var epochGetter=Method(target,"MautDefinitionEpoch");epochGetter.Body=new(epochGetter);
        var getterIl=epochGetter.Body.GetILProcessor();getterIl.Emit(OpCodes.Ldarg_0);getterIl.Emit(OpCodes.Ldfld,definition.Fields.Single(f=>f.Name=="MautEpoch"));getterIl.Emit(OpCodes.Ret);
        var snapshot=Method(target,"MautSnapshotDefinition");snapshot.Body=new(snapshot);
        var snapshotIl=snapshot.Body.GetILProcessor();snapshotIl.Emit(OpCodes.Ldarg_0);snapshotIl.Emit(OpCodes.Callvirt,clone);snapshotIl.Emit(OpCodes.Ret);
        var cctor=Method(target,".cctor");var first=cctor.Body.Instructions[0];var il=cctor.Body.GetILProcessor();
        il.InsertBefore(first,Instruction.Create(OpCodes.Newobj,module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        il.InsertBefore(first,Instruction.Create(OpCodes.Stsfld,target.Fields.Single(f=>f.Name=="MautGate")));
        return target;
    }
    internal static void State(ModuleDefinition module,TypeDefinition output)
    {
        var state=module.GetType("ETS2LA.State.ApplicationState");
        var mode=module.GetType("ETS2LA.State.DrivingMode");
        foreach(var expected in new[]{("AdaptiveCruiseControlOnly",0),("FullSelfDriving",1),("LaneAssistOnly",2)})
            if(mode.Fields.SingleOrDefault(f=>f.Name==expected.Item1)?.Constant is not int value||value!=expected.Item2)
                throw new InvalidDataException("Unverified driving-mode enum");
        WrapSetter(Method(state,"set_EnableAssists"),output,"MautSetAssists");
        WrapSetter(Method(state,"set_DrivingMode"),output,"MautSetMode");
    }
    static void WrapSetter(MethodDefinition method,TypeDefinition output,string callback)
    {
        if(method.Body.ExceptionHandlers.Count!=0||method.Body.Instructions.Count>10)throw new InvalidDataException("Unverified state setter shape");
        var module=method.Module;var gate=module.ImportReference(output.Fields.Single(f=>f.Name=="MautGate"));
        method.Body.SimplifyMacros();var il=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
        var end=Instruction.Create(OpCodes.Ret);var finallyStart=Instruction.Create(OpCodes.Ldsfld,gate);
        il.InsertBefore(first,Instruction.Create(OpCodes.Ldsfld,gate));
        il.InsertBefore(first,Instruction.Create(OpCodes.Call,module.ImportReference(typeof(Monitor).GetMethod("Enter",[typeof(object)])!)));
        foreach(var ret in method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret).ToArray()) {
            il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_1));
            if(callback=="MautSetAssists") {
                il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(ret,Instruction.Create(OpCodes.Call,Method(method.DeclaringType,"get_DrivingMode")));
            }
            il.InsertBefore(ret,Instruction.Create(OpCodes.Call,module.ImportReference(Method(output,callback))));
            ret.OpCode=OpCodes.Leave;ret.Operand=end;
        }
        il.Append(finallyStart);il.Emit(OpCodes.Call,module.ImportReference(typeof(Monitor).GetMethod("Exit",[typeof(object)])!));il.Emit(OpCodes.Endfinally);il.Append(end);
        method.Body.ExceptionHandlers.Add(new(ExceptionHandlerType.Finally){TryStart=first,TryEnd=finallyStart,HandlerStart=finallyStart,HandlerEnd=end});
        method.Body.OptimizeMacros();
    }
    internal static int StampPublishers(ModuleDefinition module,string typeName,TypeDefinition output)
    {
        var type=module.GetType(typeName)??throw new InvalidDataException("Native plugin type missing");int sites=0;
        var tick=Method(type,"Tick");tick.Body.SimplifyMacros();
        var tickEpoch=new VariableDefinition(module.TypeSystem.Int64);tick.Body.Variables.Add(tickEpoch);tick.Body.InitLocals=true;
        var tickIl=tick.Body.GetILProcessor();var tickFirst=tick.Body.Instructions[0];
        tickIl.InsertBefore(tickFirst,Instruction.Create(OpCodes.Call,module.ImportReference(Method(output,"MautCaptureEpoch"))));
        tickIl.InsertBefore(tickFirst,Instruction.Create(OpCodes.Stloc,tickEpoch));
        foreach(var method in type.Methods.Where(m=>m.HasBody)) {
            var creations=method.Body.Instructions.Where(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference m&&m.DeclaringType.FullName=="ETS2LA.Game.Output.ControlChannelDefinition").ToArray();
            if(creations.Length==0)continue;
            method.Body.SimplifyMacros();var il=method.Body.GetILProcessor();ParameterDefinition? origin=null;
            if(!ReferenceEquals(method,tick)) {
                if(typeName!="LaneAssist.LaneAssistPlugin"||method.Name!="CheckPlaySound"||method.Parameters.Count!=2)
                    throw new InvalidDataException("Publisher origin scope is not verified: "+method.Name);
                var calls=type.Methods.Where(m=>m.HasBody).SelectMany(caller=>caller.Body.Instructions
                    .Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName==type.FullName&&r.Name==method.Name)
                    .Select(i=>(caller,i))).ToArray();
                if(calls.Length==0||calls.Any(c=>!ReferenceEquals(c.caller,tick)))throw new InvalidDataException("Unverified pause helper caller");
                origin=new("MautOriginEpoch",ParameterAttributes.None,module.TypeSystem.Int64);method.Parameters.Add(origin);
                foreach(var call in calls){tickIl.InsertBefore(call.i,Instruction.Create(OpCodes.Ldloc,tickEpoch));call.i.Operand=method;}
            }
            var field=module.ImportReference(output.Module.GetType("ETS2LA.Game.Output.ControlChannelDefinition").Fields.Single(f=>f.Name=="MautEpoch"));
            foreach(var creation in creations) {
                var dup=Instruction.Create(OpCodes.Dup);il.InsertAfter(creation,dup);
                var load=origin==null?Instruction.Create(OpCodes.Ldloc,tickEpoch):Instruction.Create(OpCodes.Ldarg,origin);
                il.InsertAfter(dup,load);il.InsertAfter(load,Instruction.Create(OpCodes.Stfld,field));sites++;
            }
            method.Body.OptimizeMacros();
        }
        tick.Body.OptimizeMacros();
        if(sites==0)throw new InvalidDataException("No verified native publisher found");return sites;
    }
    internal static void AccDeadBand(ModuleDefinition module)
    {
        var tick=Method(module.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin"),"Tick");
        var matches=tick.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ret&&i.Previous?.OpCode==OpCodes.Stfld&&
            i.Previous.Operand is FieldReference f&&f.Name=="targetAcceleration"&&i.Previous.Previous?.OpCode==OpCodes.Ldc_R4&&
            i.Previous.Previous.Operand is float value&&value==-.5f).ToArray();
        if(matches.Length!=1)throw new InvalidDataException("Low-speed return pattern changed");
        matches[0].OpCode=OpCodes.Nop;matches[0].Operand=null;
    }
    internal static void Coasting(ModuleDefinition game,ModuleDefinition acc,TypeDefinition seed,TypeDefinition inputSeed,TypeDefinition output)
    {
        // Successful complete reads only, never construction of an empty container.
        foreach(var pair in new[]{("TrafficProvider","MautTrafficAt"),("ParkedVehiclesProvider","MautParkedAt"),("SemaphoreProvider","MautSemaphoreAt")}) {
            var provider=game.GetType("ETS2LA.Game.SDK."+pair.Item1);
            var update=Method(provider,"Update");var il=update.Body.GetILProcessor();
            var publishes=update.Body.Instructions.Where(i=>i.Operand is MethodReference m&&m.Name=="Publish").ToArray();
            if(publishes.Length!=1)throw new InvalidDataException("Unverified SDK read publisher");
            var originalClose=Method(provider,"CloseMemory");
            var successfulCloses=update.Body.Instructions.SkipWhile(i=>i!=publishes[0]).Where(i=>
                i.Operand is MethodReference m&&m.DeclaringType.FullName==provider.FullName&&m.Name=="CloseMemory").ToArray();
            if(successfulCloses.Length!=1)throw new InvalidDataException("Unverified normal reconnect close");
            // An intentional reconnect after a successful read does not erase
            // the still-fresh snapshot. It cannot renew its original timestamp.
            var reconnect=new MethodDefinition("MautReconnectMemory",MethodAttributes.Private,game.TypeSystem.Void);
            provider.Methods.Add(reconnect);CopyBody(originalClose,reconnect,provider,provider);
            successfulCloses[0].Operand=reconnect;
            var call=Instruction.Create(OpCodes.Call,game.ImportReference(typeof(System.Diagnostics.Stopwatch).GetMethod("GetTimestamp")!));
            var dataField=output.Fields.Single(f=>f.Name==pair.Item2[..^2]+"Data");
            var load=Instruction.Create(OpCodes.Ldarg_0);var read=Instruction.Create(OpCodes.Ldfld,provider.Fields.Single(f=>f.Name=="_currentData"));
            var save=Instruction.Create(OpCodes.Stsfld,dataField);
            il.InsertAfter(publishes[0],load);il.InsertAfter(load,read);il.InsertAfter(read,save);
            il.InsertAfter(save,call);il.InsertAfter(call,Instruction.Create(OpCodes.Stsfld,output.Fields.Single(f=>f.Name==pair.Item2)));
            foreach(var method in new[]{Method(provider,"CloseMemory")}) {
                var processor=method.Body.GetILProcessor();var first=method.Body.Instructions[0];
                processor.InsertBefore(first,Instruction.Create(OpCodes.Ldc_I8,0L));
                processor.InsertBefore(first,Instruction.Create(OpCodes.Stsfld,output.Fields.Single(f=>f.Name==pair.Item2)));
                processor.InsertBefore(first,Instruction.Create(OpCodes.Ldnull));processor.InsertBefore(first,Instruction.Create(OpCodes.Stsfld,dataField));
            }
            var thread=Method(provider,"UpdateThread");var threadIl=thread.Body.GetILProcessor();
            foreach(var handler in thread.Body.ExceptionHandlers.Where(h=>h.HandlerType==ExceptionHandlerType.Catch)) {
                var first=handler.HandlerStart;var zero=Instruction.Create(OpCodes.Ldc_I8,0L);
                threadIl.InsertBefore(first,zero);threadIl.InsertBefore(first,Instruction.Create(OpCodes.Stsfld,output.Fields.Single(f=>f.Name==pair.Item2)));
                foreach(var boundary in thread.Body.ExceptionHandlers) {
                    if(boundary.TryEnd==first)boundary.TryEnd=zero;if(boundary.HandlerEnd==first)boundary.HandlerEnd=zero;
                }
                handler.HandlerStart=zero;
            }
        }
        var curve=acc.GetType("AdaptiveCruiseControl.Constraints.PathCurvatureConstraint");
        var maximum=Method(curve,"GetMaxAcceleration");maximum.Body.SimplifyMacros();var ci=maximum.Body.GetILProcessor();
        ci.InsertBefore(maximum.Body.Instructions[0],Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautBeginCurve"))));
        var samples=maximum.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="Velocity"&&f.DeclaringType.DeclaringType?.FullName==curve.FullName).ToArray();
        if(samples.Length!=1)throw new InvalidDataException("Unverified raw curvature sample");
        ci.InsertBefore(samples[0],Instruction.Create(OpCodes.Dup));
        ci.InsertBefore(samples[0],Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautCurveSample"))));
        var final=maximum.Body.Instructions.Last(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="lastMaxAccel");
        ci.InsertBefore(final,Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautFinishCurve"))));
        foreach(var handler in maximum.Body.ExceptionHandlers.Where(h=>h.HandlerType==ExceptionHandlerType.Catch)) {
            var first=handler.HandlerStart;
            var mark=Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautInvalidateCurve")));
            ci.InsertBefore(first,mark);
            foreach(var boundary in maximum.Body.ExceptionHandlers) {
                if(boundary.TryEnd==first)boundary.TryEnd=mark;if(boundary.HandlerEnd==first)boundary.HandlerEnd=mark;
            }
            handler.HandlerStart=mark;
        }
        maximum.Body.OptimizeMacros();
        var target=acc.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin");
        var inputs=new MethodDefinition("MautInputsKnown",MethodAttributes.Public|MethodAttributes.Static,acc.TypeSystem.Boolean);
        foreach(var p in Method(inputSeed,"MautInputsKnown").Parameters)inputs.Parameters.Add(new(p.Name,p.Attributes,acc.ImportReference(p.ParameterType)));
        target.Methods.Add(inputs);CopyBody(Method(inputSeed,"MautInputsKnown"),inputs,inputSeed,target,output);
        foreach(var check in new[]{("LeadingVehicleConstraint",0,new[]{"trafficData","parkedVehicleData"}),("PathSemaphoreConstraint",1,new[]{"semaphoreData"})}) {
            var constraint=acc.GetType("AdaptiveCruiseControl.Constraints."+check.Item1);var noLimit=Method(constraint,"NoLimit");
            var ni=noLimit.Body.GetILProcessor();var first=noLimit.Body.Instructions[0];
            ni.InsertBefore(first,Instruction.Create(OpCodes.Ldc_I4,check.Item2));ni.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_1));
            ni.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));ni.InsertBefore(first,Instruction.Create(OpCodes.Ldfld,constraint.Fields.Single(f=>f.Name=="telemetryData")));
            ni.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));ni.InsertBefore(first,Instruction.Create(OpCodes.Ldfld,constraint.Fields.Single(f=>f.Name=="plannedPathData")));
            ni.InsertBefore(first,Instruction.Create(OpCodes.Call,inputs));
            foreach(string field in check.Item3) {
                ni.InsertBefore(first,Instruction.Create(OpCodes.Ldarg_0));ni.InsertBefore(first,Instruction.Create(OpCodes.Ldfld,constraint.Fields.Single(f=>f.Name==field)));
            }
            if(check.Item3.Length==1)ni.InsertBefore(first,Instruction.Create(OpCodes.Ldnull));
            ni.InsertBefore(first,Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautMarkNoLimit"))));
        }
        var helper=new MethodDefinition("MautApplyCoast",MethodAttributes.Public,acc.TypeSystem.Void);
        foreach(var p in Method(seed,"MautApplyCoast").Parameters)helper.Parameters.Add(new(p.Name,p.Attributes,acc.ImportReference(p.ParameterType)));
        target.Methods.Add(helper);CopyBody(Method(seed,"MautApplyCoast"),helper,seed,target,output);
        var tick=Method(target,"Tick");tick.Body.SimplifyMacros();var ti=tick.Body.GetILProcessor();
        var epoch=tick.Body.Variables.Single(v=>v.VariableType.MetadataType==MetadataType.Int64);
        var stores=tick.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="ETS2LA.Game.Output.ControlEvent"&&f.Name=="Variables").ToArray();
        if(stores.Length!=1)throw new InvalidDataException("Unverified ACC variables publisher");
        var variables=new VariableDefinition(stores[0].Operand is FieldReference variableField?acc.ImportReference(variableField.FieldType):throw new InvalidDataException());tick.Body.Variables.Add(variables);
        ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Dup));ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Stloc,variables));
        ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Ldarg_0));ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Ldloc,variables));
        ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Ldloc,epoch));ti.InsertBefore(stores[0],Instruction.Create(OpCodes.Call,helper));tick.Body.OptimizeMacros();
        // No value from a preceding cycle or a removed constraint may grant coasting.
        var evaluate=Method(target,"EvaluateConstraints");var ei=evaluate.Body.GetILProcessor();var start=evaluate.Body.Instructions[0];
        ei.InsertBefore(start,Instruction.Create(OpCodes.Call,acc.ImportReference(Method(output,"MautBeginEvaluation"))));
        ei.InsertBefore(start,Instruction.Create(OpCodes.Ldarg_0));ei.InsertBefore(start,Instruction.Create(OpCodes.Ldfld,target.Fields.Single(f=>f.Name=="constraintValues")));
        ei.InsertBefore(start,Instruction.Create(OpCodes.Callvirt,acc.ImportReference(typeof(Dictionary<string,float>).GetMethod("Clear")!)));
    }
}
