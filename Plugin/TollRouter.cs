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
    public Vector3? StopBoundary {get;private set;}
    public string? StopKey {get;private set;}
    public double? StopDistance {get;private set;}
    public void CancelPlannedStop() {StopBoundary=null;StopKey=null;StopDistance=null;}
    public TollPayment PaymentFor(int gate)=>CurrentKey!=null&&preferences.Payments.TryGetValue(PaymentPolicy.Key(CurrentKey,gate),out var mode)?mode:
        modelPayments.GetValueOrDefault(gate);
    IReadOnlyDictionary<int,TollPayment> modelPayments = new Dictionary<int,TollPayment>();
    readonly Func<ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments;
    readonly Dictionary<int,PrefabPath> plans = new();
    double lastSearch = double.NegativeInfinity;
    PlannedItem? shortChangeRoad;
    readonly PreferenceStore preferences;
    public TollRouter(PreferenceStore preferences, Func<ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments=null)
    {this.preferences=preferences;this.readPayments=readPayments;}
    public string Status {get;private set;}="Warte auf Route";
    readonly Dictionary<PrefabPath,PrefabPath> replacements=new();
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;

    static string Key(PrefabPath path)=>$"{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}";
    public void Discover(PlannedPathData data,Vector3 truck,ETS2LA.Game.SDK.Semaphore[] sdkGates,double speed=0)
    {
        if(data.HasError)return;
        for(int index=0;index<data.PlannedPath.Count;index++) {
            var item=data.PlannedPath[index];
            var p=item.prefabPath;
            if(p==null)continue;
            p=PrefabRefinement.OriginalFor(p);
            var routeGates=p.GetSemaphores().Where(s=>sdkGates.Any(g=>g.type==ETS2LA.Game.SDK.SemaphoreType.GATE &&
                g.id==s.Semaphore.SemaphoreId && Vector3.Distance(g.GetWorldCoordinates(),s.GetWorldOrientedPoint().Position)<1)).ToArray();
            if(routeGates.Length==0)continue;
            var first=routeGates[0];var world=first.GetWorldOrientedPoint().Position;
            var forward=Vector3.Normalize(first.GetCurveEndWorldCoordinates().Position-first.GetCurveStartWorldCoordinates().Position);
            if(Vector3.Dot(world-truck,forward)<-10 || Vector3.Distance(world,truck)>1000)continue;
            var key=Key(p);
            // Do not replace a pending stop with a newly planned/different plaza.
            if(StopBoundary.HasValue && key!=StopKey)return;
            double now=System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if(CurrentKey==key && (SelectedGate.HasValue || now-lastSearch < .5))return;
            lastSearch=now;
            modelPayments=readPayments?.Invoke(p.Prefab) ?? new Dictionary<int,TollPayment>();
            plans.Clear();
            replacements.Clear();
            var right=Vector3.Cross(forward,Vector3.UnitY);
            var lanes=new List<(int gate,float lateral,bool reachable)>();
            foreach(var c in p.Prefab.Descriptor!.NavCurves.Where(c=>c.SemaphoreId>=0)) {
                var single=(PrefabPath)clone.Invoke(p,null)!;single.Curves=[c];single.Length=c.Length;
                var sem=single.GetSemaphores().FirstOrDefault();if(sem==null)continue;
                var heading=Vector3.Normalize(sem.GetCurveEndWorldCoordinates().Position-sem.GetCurveStartWorldCoordinates().Position);
                if(Vector3.Dot(heading,forward)<.8)continue;
                if(!sdkGates.Any(g=>g.type==ETS2LA.Game.SDK.SemaphoreType.GATE && g.id==c.SemaphoreId &&
                    Vector3.Distance(g.GetWorldCoordinates(),sem.GetWorldOrientedPoint().Position)<1))continue;
                var alternatives = new[] { p }.Concat(item.otherPrefabPaths ?? [])
                    .Where(option => Key(option) == key);
                foreach (var alternative in alternatives)
                {
                    var route = ViaGate(alternative, c.SemaphoreId);
                    if (route == null || TollEntry.Plan(data,index,p,route,truck,speed) == null) continue;
                    plans.TryAdd(c.SemaphoreId,route);
                    break;
                }
                lanes.Add((c.SemaphoreId,Vector3.Dot(sem.GetWorldOrientedPoint().Position-world,right),plans.ContainsKey(c.SemaphoreId)));
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
        var automatic=Choices.Where(c=>PaymentFor(c.GateId)==TollPayment.Automatic).ToArray();
        SelectedGate=automatic.Any(c=>c.GateId==SelectedGate)?SelectedGate:
            automatic.FirstOrDefault()?.GateId ?? Choices.FirstOrDefault(c=>
                PaymentFor(c.GateId)==TollPayment.Manual && modelPayments.GetValueOrDefault(c.GateId)==TollPayment.Manual)?.GateId;
        if (SelectedGate.HasValue) CancelPlannedStop();
        Status=SelectedGate is int gate?LaneStatus(gate):
            "Keine eindeutig passende Lkw-Mautspur – manuell übernehmen";
    }
    string LaneStatus(int gate) => (PaymentFor(gate)==TollPayment.Automatic
        ? "Automatisch abrechnende Lkw-Spur: " : "Lkw-Spur mit manueller Zahlung: ") +
        Choices.FirstOrDefault(c=>c.GateId==gate)?.Label;
    public void Confirm(int gate,TollPayment payment)
    {
        if(CurrentKey==null || !Choices.Any(c=>c.GateId==gate))throw new ArgumentException("Keine erreichbare Mautspur ausgewählt");
        if(!Enum.IsDefined(payment))throw new ArgumentException("Ungültige Zahlart");
        var values=new Dictionary<string,int>(preferences.Gates){[CurrentKey]=gate};
        var payments=new Dictionary<string,TollPayment>(preferences.Payments){[PaymentPolicy.Key(CurrentKey,gate)]=payment};
        preferences.Save(preferences.Options,values,payments);SelectedGate=gate;replacements.Clear();SelectAutomatic();
        if(SelectedGate.HasValue)CancelPlannedStop();
    }

    public double? UpdateStopDistance(PlannedPathData data,Vector3 truck,double speed)
    {
        if(StopBoundary is not Vector3 boundary)return null;
        var trace=RouteMatcher.SampleForTracking(data,truck,speed);
        var target=trace.Project(boundary);var current=trace.Project(truck);
        if(target.Offset>1 || current.Offset>3 || (trace.EndsAtGap && target.Along>=trace.Length-.01))
            throw new InvalidOperationException("Haltegrenze der Mautzufahrt nicht mehr sicher verfolgbar.");
        StopDistance=target.Along-current.Along-ApproachStop.BufferMetres;
        return StopDistance;
    }

    static Vector3 CommonApproachEnd(PrefabPath source)
    {
        source=PrefabRefinement.OriginalFor(source);
        // Stop before any choice, restriction or semaphore. Only a unique,
        // unrestricted chain can be followed without a confirmed toll lane.
        var prefix=new List<NavCurve>();
        foreach(var curve in source.Curves) {
            if(curve.AllowedVehicles is not (AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) || curve.SemaphoreId>=0)break;
            prefix.Add(curve);
            if(curve.NextLines.Count!=1)break;
            int routeIndex=source.Curves.IndexOf(curve);
            if(routeIndex+1>=source.Curves.Count || !curve.NextLines.Contains(source.Prefab.Descriptor!.NavCurves.IndexOf(source.Curves[routeIndex+1])))break;
        }
        var copy=(PrefabPath)clone.Invoke(source,null)!;
        if(prefix.Count==0)return source.Interpolate(.00001f,0)!.Value.Position;
        copy.Curves=prefix;copy.Length=prefix.Sum(c=>c.Length);
        return copy.Interpolate(.99999f,0)!.Value.Position;
    }

    public static PrefabPath? ViaGate(PrefabPath source,int gate)
    {
        source=PrefabRefinement.OriginalFor(source);
        var all=source.Prefab.Descriptor?.NavCurves;
        if(all==null || source.Curves.Count==0)return null;
        var graph=all.Select((c,i)=>new CurveLink(i,c.NextLines.ToArray(),c.Length,
            (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) &&
            c.TrafficRule.ToString() != "escort_only",c.SemaphoreId)).ToArray();
        int first=all.IndexOf(source.Curves[0]),last=all.IndexOf(source.Curves[^1]);
        var ids=CurveGraph.ViaGate(graph,first,last,gate);
        if(ids==null)return null;
        var result=(PrefabPath)clone.Invoke(source,null)!;
        result.Curves=ids.Select(i=>all[i]).ToList();result.Length=result.Curves.Sum(c=>c.Length);
        return PrefabRefinement.Refine(result);
    }
    public bool Apply(PlannedPathData data,Vector3 truck,out string? error,double speed=0)
    {
        error=null;
        if(data.HasError)return false;
        for(int itemIndex=0;itemIndex<data.PlannedPath.Count;itemIndex++)
        {
            var item=data.PlannedPath[itemIndex];
            var current=item.prefabPath;
            if(current==null || Key(current)!=CurrentKey)continue;
            if(!SelectedGate.HasValue) {
                StopBoundary ??= CommonApproachEnd(current);StopKey ??= CurrentKey;
                item.allowedToLaneChange=false;
                Status="Unbestätigte Mautspur: geplanter Halt vor der Aufteilung; F9 zur Übernahme";
                return true;
            }
            // The gate must be reachable from the existing entry lane and exit. Never
            // invent a lateral jump at a road boundary or use the opposite carriageway.
            if(!replacements.TryGetValue(current,out var replacement))
            {
                replacement=plans.TryGetValue(SelectedGate.Value,out var plan) ? plan : ViaGate(current,SelectedGate.Value);
                if(replacement==null) {
                    error="Die gewählte Lkw-Spur ist von dieser Einfahrt nicht erreichbar.";return false;
                }
                var changes = TollEntry.Plan(data,itemIndex,current,replacement,truck,speed);
                if (changes == null)
                {error="Die gewählte Mautspur kann von der aktuellen Fahrspur nicht mehr erreicht werden.";return false;}
                foreach (var change in changes.Apply) change();
                if (changes.SlowRoad != null) shortChangeRoad=changes.SlowRoad;
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
            if(!ExitHandoff.Align(data,itemIndex,truck,out error))return false;
            Status=LaneStatus(SelectedGate.Value);
        }
        return true;
    }

    public TollDecision LimitShortChange(PlannedPathData data, RouteTrace trace, Vector3 truck, double speed, TollDecision decision)
    {
        if (shortChangeRoad == null) return decision;
        if (!data.PlannedPath.Contains(shortChangeRoad))
        {
            shortChangeRoad=null;
            return decision;
        }
        float start = shortChangeRoad.direction==Direction.Backward?1:0;
        var snapshot=RouteSnapshot.Capture(data);
        var road=snapshot.PlannedPath[data.PlannedPath.IndexOf(shortChangeRoad)];
        var startPoint=road.Interpolate(start,0,false)?.Position;
        if (!startPoint.HasValue) throw new InvalidOperationException("Beginn des Maut-Spurwechsels fehlt.");
        double ahead = Math.Max(0,trace.Project(startPoint.Value).Along-trace.Project(truck).Along-speed*1.5);
        double cap = Math.Sqrt(TollEntry.ShortChangeSpeed*TollEntry.ShortChangeSpeed+2.4*ahead);
        float limit = (float)Math.Clamp((cap-speed)*.8,-2.5,.35);
        if (float.IsFinite(decision.MaxAcceleration) && decision.MaxAcceleration <= limit) return decision;
        return decision with { Phase="Tempo für Maut-Spurwechsel", TargetSpeed=cap, MaxAcceleration=limit };
    }
}
