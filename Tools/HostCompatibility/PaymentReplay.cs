using System.Diagnostics;
using System.Reflection;

namespace MautHostCompatibility;

internal static class PaymentReplay
{
    internal static void Run(Type output,object host,Action<bool> frame,Action<bool> enable,Func<long> epoch,
        Func<double> activation,Action<bool,string> assert,List<bool> writes,Action ordinaryChannel)
    {
        var lease=output.GetMethod("MautSetPaymentLease")!;
        var cancel=output.GetMethod("MautCancelPayment")!;
        var permission=output.GetField("MautPaymentPermission")!;
        var registerPermission=output.GetMethod("MautRegisterPaymentPermission")!;
        var clearPermission=output.GetMethod("MautClearPaymentPermission")!;
        Func<long,bool> oldPermission=_=>false,newPermission=_=>true;
        registerPermission.Invoke(null,[oldPermission]);
        registerPermission.Invoke(null,[newPermission]);
        clearPermission.Invoke(null,[oldPermission]);
        assert(ReferenceEquals(permission.GetValue(null),newPermission),"Old plugin cleanup cannot clear a newly registered payment callback");
        clearPermission.Invoke(null,[newPermission]);
        assert(permission.GetValue(null)==null,"Current plugin cleanup atomically clears its own payment callback");
        long Source()=>(long)output.GetField("MautOwnSourceEpoch")!.GetValue(null)!;
        void Send(long attempt,long? e=null,long? source=null,long? stamp=null,long? observation=null) {
            long at=stamp??Stopwatch.GetTimestamp();lease.Invoke(null,[attempt,e??epoch(),source??Source(),at,observation??at]);
        }
        bool valid=true;
        permission.SetValue(null,(Func<long,bool>)(_=>valid));
        var channels=(System.Collections.IDictionary)output.GetField("Channels")!.GetValue(host)!;
        channels.Clear();enable(false);enable(true);
        foreach(bool modern in new[]{false,true}) {
            long attempt=Stopwatch.GetTimestamp();Send(attempt);frame(modern);
            assert(activation()==1,"Payment semantic activate reaches the legacy input sink even when modern pedals are selected: "+modern);
            Thread.Sleep(120);Send(attempt);frame(modern);
            assert(activation()==0,"Payment ends after its original100ms deadline; renewing its lease never makes another edge");
            enable(false);enable(true);Send(attempt);frame(modern);
            assert(activation()==0,"F9 then F8 cannot replay an already consumed payment request");
        }
        ordinaryChannel();frame(false);
        long singleEdge=Stopwatch.GetTimestamp();Send(singleEdge);frame(false);writes.Clear();channels.Clear();frame(false);
        assert(activation()==1&&writes.Count>0&&writes.All(value=>value),"Removing the last ordinary channel cannot insert a false/true edge into an owned payment pulse");
        cancel.Invoke(null,[epoch()]);frame(false);
        long rearmed=Stopwatch.GetTimestamp();Send(rearmed);frame(false);enable(false);enable(true);
        Send(rearmed);frame(false);assert(activation()==0,"A high payment pulse cannot be rebound across F9/F8 before the next physical frame");
        long reconnected=Stopwatch.GetTimestamp();Send(reconnected);frame(false);
        output.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);Send(reconnected);frame(false);
        assert(activation()==0,"A high payment pulse cannot be rebound across a provider reconnect before the next physical frame");
        long pending=Stopwatch.GetTimestamp(),oldEpoch=epoch();Send(pending);enable(false);enable(true);Send(pending);frame(false);
        assert(activation()==0,"Takeover before physical payment frame withdraws an uncommitted semantic input");
        Send(pending,oldEpoch);frame(false);assert(activation()==0,"Late old-session payment cannot rearm after F9");
        foreach(string loss in new[]{"focus","pause","zone drift","gate state","stale telemetry"}) {
            valid=true;long attempt=Stopwatch.GetTimestamp();Send(attempt);valid=false;frame(false);
            assert(activation()==0,"Final payment context is checked again before true: "+loss);
            valid=true;Send(attempt);frame(false);
            assert(activation()==0,"Returning context cannot repeat a withdrawn payment: "+loss);
            attempt=Stopwatch.GetTimestamp();Send(attempt);frame(false);
            assert(activation()==1,"Independent fresh payment attempt starts before context withdrawal: "+loss);
            valid=false;frame(false);assert(activation()==0,"An active pulse clears on context withdrawal: "+loss);
        }
        valid=true;
        long beforeReconnect=Stopwatch.GetTimestamp();Send(beforeReconnect);frame(false);
        output.GetMethod("MautInvalidateOwnSources")!.Invoke(null,null);frame(false);
        assert(activation()==0,"Provider reconnect clears an active payment pulse");
        Send(beforeReconnect);frame(false);assert(activation()==0,"Provider reconnect cannot reissue the same consumed payment");
        long fresh=Stopwatch.GetTimestamp();Send(fresh);frame(false);
        output.GetMethod("MautCancelPaymentAttempt")!.Invoke(null,[epoch(),fresh-1]);frame(false);
        assert(activation()==1,"Late cleanup from another payment attempt cannot cancel the current physical pulse");
        cancel.Invoke(null,[epoch()]);frame(false);assert(activation()==0,"Explicit payment cancellation clears the sink with no ordinary channels");
        Send(fresh);frame(false);assert(activation()==0,"Explicit payment cancellation consumes the attempt even in the same driving epoch");
        foreach(long stale in new[]{0L,-1L,Stopwatch.GetTimestamp()-Stopwatch.Frequency,Stopwatch.GetTimestamp()+Stopwatch.Frequency}) {
            long attempt=Stopwatch.GetTimestamp();Send(attempt,observation:stale);frame(false);
            assert(activation()==0,"Invalid or stale original payment evidence cannot acquire a physical input lease: "+stale);
        }
        long delayed=Stopwatch.GetTimestamp();Send(delayed);Thread.Sleep(120);frame(false);
        assert(activation()==0,"Low-FPS delayed first frame cannot stretch an expired payment pulse into the future");
        var all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var legacy=output.GetField("legacyAccessor",all)!.GetValue(host);
        var offsets=(Dictionary<string,int>)output.GetField("legacyShmOffsets",all)!.GetValue(host)!;
        var toggle=output.GetMethod("ToggleBool",all)!;
        cancel.Invoke(null,[epoch()]);frame(false);
        toggle.Invoke(host,[legacy,offsets["activate"]]);
        assert(activation()==0,"The real generic ToggleBool cannot write activate after payment withdrawal");
        long mismatched=Stopwatch.GetTimestamp();
        permission.SetValue(null,(Func<long,bool>)(request=>request!=mismatched));
        Send(mismatched);frame(false);assert(activation()==0,"Payment context is explicitly bound to the original attempt identity");
        permission.SetValue(null,(Func<long,bool>)(_=>{Thread.Sleep(120);return true;}));
        Send(Stopwatch.GetTimestamp());frame(false);assert(activation()==0,"A delayed permission observer cannot extend the original physical pulse deadline");
        permission.SetValue(null,(Func<long,bool>)(_=>{Thread.Sleep(40);return true;}));
        Send(Stopwatch.GetTimestamp(),observation:Stopwatch.GetTimestamp()-Stopwatch.Frequency*19/100);frame(false);
        assert(activation()==0,"Payment source expiry during final permission read is rechecked at physical write time");
        permission.SetValue(null,(Func<long,bool>)(_=>throw new InvalidOperationException("injected context failure")));
        Send(Stopwatch.GetTimestamp());frame(false);assert(activation()==0,"Payment permission read failure is contained and neutral");
        permission.SetValue(null,null);cancel.Invoke(null,[epoch()]);enable(false);frame(false);
    }
}
