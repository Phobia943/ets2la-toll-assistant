using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;

namespace MautAssistent;

public sealed class TollRouter
{
    public sealed record LaneChoice(int GateId,string Label);
    public LaneChoice[] Choices {get;private set;}=[];
    public string? CurrentKey {get;private set;}
    public int? SelectedGate {get;private set;}
    public TollPayment PaymentFor(int gate)=>CurrentKey!=null&&preferences.Payments.TryGetValue(PaymentPolicy.Key(CurrentKey,gate),out var mode)?mode:TollPayment.Unknown;
    readonly PreferenceStore preferences;
    public TollRouter(PreferenceStore preferences) {this.preferences=preferences;}
    public string Status {get;private set;}="Warte auf Route";
    readonly Dictionary<PrefabPath,PrefabPath> replacements=new();
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;

    static string Key(PrefabPath path)=>$"{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}";
    public void Discover(PlannedPathData data,Vector3 truck,ETS2LA.Game.SDK.Semaphore[] sdkGates)
    {
        if(data.HasError)return;
        foreach(var item in data.PlannedPath) {
            var p=item.prefabPath;
            if(p==null)continue;
            var routeGates=p.GetSemaphores().Where(s=>sdkGates.Any(g=>g.type==ETS2LA.Game.SDK.SemaphoreType.GATE &&
                g.id==s.Semaphore.SemaphoreId && Vector3.Distance(g.GetWorldCoordinates(),s.GetWorldOrientedPoint().Position)<1)).ToArray();
            if(routeGates.Length==0)continue;
            var first=routeGates[0];var world=first.GetWorldOrientedPoint().Position;
            var forward=Vector3.Normalize(first.GetCurveEndWorldCoordinates().Position-first.GetCurveStartWorldCoordinates().Position);
            if(Vector3.Dot(world-truck,forward)<-10 || Vector3.Distance(world,truck)>300)continue;
            var key=Key(p);
            if(CurrentKey==key)return;
            var right=Vector3.Cross(forward,Vector3.UnitY);
            var lanes=new List<(int gate,float lateral,bool reachable)>();
            foreach(var c in p.Prefab.Descriptor!.NavCurves.Where(c=>c.SemaphoreId>=0)) {
                var single=(PrefabPath)clone.Invoke(p,null)!;single.Curves=[c];single.Length=c.Length;
                var sem=single.GetSemaphores().FirstOrDefault();if(sem==null)continue;
                var heading=Vector3.Normalize(sem.GetCurveEndWorldCoordinates().Position-sem.GetCurveStartWorldCoordinates().Position);
                if(Vector3.Dot(heading,forward)<.8)continue;
                if(!sdkGates.Any(g=>g.type==ETS2LA.Game.SDK.SemaphoreType.GATE && g.id==c.SemaphoreId &&
                    Vector3.Distance(g.GetWorldCoordinates(),sem.GetWorldOrientedPoint().Position)<1))continue;
                lanes.Add((c.SemaphoreId,Vector3.Dot(sem.GetWorldOrientedPoint().Position-world,right),ViaGate(p,c.SemaphoreId)!=null));
            }
            CurrentKey=key;
            Choices=lanes.OrderByDescending(l=>l.lateral).Select((l,i)=>new {l,i}).Where(x=>x.l.reachable)
                .Select(x=>new LaneChoice(x.l.gate,$"{x.i+1}. Durchfahrt von rechts")).ToArray();
            SelectedGate=preferences.Gates.TryGetValue(key,out var chosen)?chosen:null;
            SelectAutomatic();
            return;
        }
    }
    void SelectAutomatic()
    {
        if(CurrentKey==null)return;
        SelectedGate=PaymentPolicy.ChooseAutomatic(CurrentKey,Choices.Select(c=>c.GateId),SelectedGate,preferences.Payments);
        Status=SelectedGate.HasValue?"Automatische Lkw-Mautspur: "+Choices.First(c=>c.GateId==SelectedGate).Label:
            "Keine bestätigte automatische Lkw-Mautspur – manuell übernehmen";
    }
    public void Confirm(int gate,TollPayment payment)
    {
        if(CurrentKey==null || !Choices.Any(c=>c.GateId==gate))throw new ArgumentException("Keine erreichbare Mautspur ausgewählt");
        if(!Enum.IsDefined(payment))throw new ArgumentException("Ungültige Zahlart");
        var values=new Dictionary<string,int>(preferences.Gates){[CurrentKey]=gate};
        var payments=new Dictionary<string,TollPayment>(preferences.Payments){[PaymentPolicy.Key(CurrentKey,gate)]=payment};
        preferences.Save(preferences.Options,values,payments);SelectedGate=gate;replacements.Clear();SelectAutomatic();
    }

    public static PrefabPath? ViaGate(PrefabPath source,int gate)
    {
        var all=source.Prefab.Descriptor?.NavCurves;
        if(all==null || source.Curves.Count==0)return null;
        var graph=all.Select((c,i)=>new CurveLink(i,c.NextLines.ToArray(),c.Length,
            c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles,c.SemaphoreId)).ToArray();
        int first=all.IndexOf(source.Curves[0]),last=all.IndexOf(source.Curves[^1]);
        var ids=CurveGraph.ViaGate(graph,first,last,gate);
        if(ids==null)return null;
        var result=(PrefabPath)clone.Invoke(source,null)!;
        result.Curves=ids.Select(i=>all[i]).ToList();result.Length=result.Curves.Sum(c=>c.Length);
        return result;
    }
    public bool Apply(PlannedPathData data,Vector3 truck,out string? error)
    {
        error=null;
        if(data.HasError)return false;
        foreach(var item in data.PlannedPath)
        {
            var current=item.prefabPath;
            if(current==null || Key(current)!=CurrentKey)continue;
            if(!SelectedGate.HasValue) {error="Keine bestätigte automatische Lkw-Mautspur. Bitte selbst lenken und bis zur Zahlungsaufforderung fahren; die Assistenz nutzt hier keinen pauschalen 8-m-Zahlhaltepunkt.";return false;}
            // The gate must be reachable from the existing entry lane and exit. Never
            // invent a lateral jump at a road boundary or use the opposite carriageway.
            if(!replacements.TryGetValue(current,out var replacement))
            {
                replacement=ViaGate(current,SelectedGate.Value);
                if(replacement==null) {
                    error="Die bestätigte Lkw-Spur ist von dieser Einfahrt nicht erreichbar.";return false;
                }
                replacements[current]=replacement;replacements[replacement]=replacement;
                if(replacements.Count>128)replacements.Clear();
            }
            if(!ReferenceEquals(current,replacement))
            {
                // Changing an already diverged lane would steer across islands.
                var oldPoint=current.Interpolate(current.GetFactorForPoint(truck),0)?.Position;
                var newPoint=replacement.Interpolate(replacement.GetFactorForPoint(truck),0)?.Position;
                if(oldPoint.HasValue&&newPoint.HasValue&&Vector3.Distance(truck,oldPoint.Value)<5&&Vector3.Distance(oldPoint.Value,newPoint.Value)>2) {
                    error="Zu spät für den Wechsel zur bestätigten Lkw-Spur. Bitte manuell einordnen.";return false;
                }
                item.prefabPath=replacement;
                item.otherPrefabPaths=[replacement];
                item.allowedToLaneChange=false;
            }
            Status="Bestätigte Lkw-Spur: "+Choices.FirstOrDefault(c=>c.GateId==SelectedGate)?.Label;
        }
        return true;
    }
}
