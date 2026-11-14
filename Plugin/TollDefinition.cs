using TruckLib.Sii;

namespace MautAssistent;

/// <summary>Payment semantics come from the unit, never a country or a name substring.</summary>
internal readonly record struct TollDefinition(bool Known,TollPayment Payment)
{
    static readonly string[] Attributes=["model","animation","type","toll","trigger_length","trigger_width",
        "trigger_distance","trigger_offset","max_activation_speed"];
    public static TollDefinition Read(Unit unit,bool specialJob)
    {
        if(unit.Class!="tollgate"||unit.Attributes.Count==0||unit.Attributes.Keys.Any(k=>!Attributes.Contains(k,StringComparer.Ordinal)))return default;
        if(unit.Attributes.ContainsKey("model")!=unit.Attributes.ContainsKey("animation"))return default;
        foreach(var (key,expected) in new[]{("model","/model/activation/tollgate.pmd"),("animation","/model/activation/tollgate.pma")})
            if(unit.Attributes.TryGetValue(key,out var value)&&((object?)value is not string text||text!=expected))return default;
        string type="";
        if(unit.Attributes.TryGetValue("type",out var kind)) {
            type=(object?)kind switch {string text=>text,LinkPointer link=>link.Value,TruckLib.Token token=>token.ToString(),_=>"?"};
            if(type is not ("" or "start" or "end" or "one_time" or "oversize" or "oversize_disabled"))return default;
        }
        bool automatic=false;
        foreach(var pair in unit.Attributes.Where(p=>p.Key is not ("model" or "animation" or "type"))) {
            double? number=(object?)pair.Value switch {int v=>v,float v=>v,double v=>v,_=>null};
            if(number is not { } n||!double.IsFinite(n))return default;
            if(pair.Key is "trigger_length" or "trigger_width" or "trigger_distance" && n<=0)return default;
            if(pair.Key=="max_activation_speed") {if(n<0||n>250)return default;automatic=n>0;}
            if(pair.Key=="toll"&&n<0)return default;
        }
        // Recognized but disabled for the current job is not an unknown child
        // model. It contributes no usable payment marker.
        if(type=="oversize"&&!specialJob||type=="oversize_disabled"&&specialJob)return new(true,TollPayment.Unknown);
        return new(true,automatic?TollPayment.Automatic:TollPayment.Manual);
    }
}
