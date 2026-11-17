using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;

namespace MautAssistent;

public sealed class TollRouter
{
    public sealed record RouteSearch(PrefabPath? Path,string Reason,int[] BlockedCurves) { public object? Evidence {get;init;} }
    public sealed record LaneAttempt(int Gate,string Payment,int FirstCurve,int LastCurve,string Reason,int[] BlockedCurves,object? Evidence);
    public LaneAttempt[] LaneAttempts {get;private set;}=[];
    public object? LaneDecisionContext {get;private set;}
    public object? LaneAttemptContext {get;private set;}
    readonly Func<PrefabPath,int,RouteSearch>? findClearRoute;
    public sealed record LaneChoice(int GateId,string Label);
    public LaneChoice[] Choices {get;private set;}=[];
    public string? CurrentKey {get;private set;}
    public int? SelectedGate {get;private set;}
    public TollBarrierPlan? SelectedBarrier {get;private set;}
    public bool ClearanceHoldRequested {get;private set;}
    public string? ClearanceHoldKey {get;private set;}
    public bool PlanningDeferred {get;private set;}
    public string? PlanningDeferredKey {get;private set;}
    public Vector3? StopBoundary {get;private set;}
    public string? StopKey {get;private set;}
    public double? StopDistance {get;private set;}
    public string? PrefetchIssue {get;private set;}
    public string? ManualStopIssue {get;private set;}
    public void CancelPlannedStop() {StopBoundary=null;StopKey=null;StopDistance=null;}
    public TollPayment PaymentFor(int gate)=>modelPayments.GetValueOrDefault(gate) is TollPayment.Automatic or TollPayment.Manual
        ?modelPayments[gate]:CurrentKey!=null&&preferences.Payments.TryGetValue(PaymentPolicy.Key(CurrentKey,gate),out var mode)?mode:TollPayment.Unknown;
    IReadOnlyDictionary<int,TollPayment> modelPayments = new Dictionary<int,TollPayment>();
    readonly Func<ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments;
    readonly Func<PrefabPath,bool>? readClearance;
    readonly Action<ParsedPrefab>? prefetch;
    readonly Func<ParsedPrefab,int,ManualStopZone?>? readManualStop;
    readonly Dictionary<int,PrefabPath> plans = new();
    readonly Dictionary<int,(bool ShortChange,int Changes)> entryCosts = new();
    double lastSearch = double.NegativeInfinity;
    PlannedItem? shortChangeRoad;
    public bool HasShortChange => shortChangeRoad != null;
    readonly PreferenceStore preferences;
    public TollRouter(PreferenceStore preferences, Func<ParsedPrefab,IReadOnlyDictionary<int,TollPayment>>? readPayments=null,
        Func<PrefabPath,bool>? readClearance=null,Action<ParsedPrefab>? prefetch=null,
        Func<ParsedPrefab,int,ManualStopZone?>? readManualStop=null,Func<PrefabPath,int,RouteSearch>? findClearRoute=null)
    {this.preferences=preferences;this.readPayments=readPayments;this.readClearance=readClearance;this.prefetch=prefetch;this.readManualStop=readManualStop;this.findClearRoute=findClearRoute;}
    public double? ManualStopOffset(double safeOffset)
    {
        ManualStopIssue=null;
        if(readManualStop==null)return null;
        try {return ReadManualStopOffset(safeOffset);}
        catch(Exception ex) {ManualStopIssue=ex.Message;return null;}
    }
    double? ReadManualStopOffset(double safeOffset)
    {
        if(SelectedGate is not int gate||PaymentFor(gate)!=TollPayment.Manual||!plans.TryGetValue(gate,out var plan))return null;
        var original=PrefabRefinement.OriginalFor(plan);
        if(!DirectedChain(original)||ViaGate(original,gate)==null)return null;
        var curves=original.Curves.Where(c=>c.SemaphoreId==gate).ToArray();
        var signal=original.GetSemaphores().FirstOrDefault(s=>s.Semaphore.SemaphoreId==gate);
        if(curves.Length!=1||signal==null)return null;
        double local=Vector3.Distance(curves[0].StartPosition,curves[0].EndPosition);
        double world=Vector3.Distance(signal.GetCurveStartWorldCoordinates().Position,signal.GetCurveEndWorldCoordinates().Position);
        if(!double.IsFinite(local)||local<1||!double.IsFinite(world)||Math.Abs(local-world)>Math.Max(.01,local*.005))return null;
        var single=(PrefabPath)clone.Invoke(original,null)!;single.Curves=[curves[0]];single.Length=curves[0].Length;
        var begin=single.Interpolate(.00001f,0);var end=single.Interpolate(.99999f,0);
        if(!begin.HasValue||!end.HasValue)return null;
        float alignment=Vector3.Dot(Vector3.Normalize(end.Value.Position-begin.Value.Position),
            Vector3.Normalize(signal.GetCurveEndWorldCoordinates().Position-signal.GetCurveStartWorldCoordinates().Position));
        if(!float.IsFinite(alignment)||alignment<.95f)return null;
        return readManualStop!(plan.Prefab,gate)?.Choose(safeOffset);
    }
    static bool DirectedChain(PrefabPath path)
    {
        var all=path.Prefab.Descriptor?.NavCurves;
        if(all==null||path.Curves.Count==0)return false;
        for(int i=0;i<path.Curves.Count;i++) {
            var c=path.Curves[i];int index=all.IndexOf(c);
            if(index<0||!float.IsFinite(c.Length)||c.Length<=0||c.AllowedVehicles is not (AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles)||c.TrafficRule.ToString()=="escort_only")return false;
            if(i+1<path.Curves.Count) {
                var next=path.Curves[i+1];
                if(!c.NextLines.Contains(all.IndexOf(next))||Vector3.Distance(c.EndPosition,next.StartPosition)>1.5)return false;
            }
        }
        return true;
    }
    public string Status {get;private set;}="Warte auf Route";
    readonly Dictionary<PrefabPath,PrefabPath> replacements=new();
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;

    static string Key(PrefabPath path)=>$"{path.Prefab.Prefab.Uid}:{path.StartNode.Uid}:{path.EndNode.Uid}";

    internal void ResetSelection()
    {
        CurrentKey=null;SelectedGate=null;SelectedBarrier=null;Choices=[];Status="Warte auf Route";
        LaneAttempts=[];LaneDecisionContext=null;LaneAttemptContext=null;
        ClearanceHoldRequested=false;
        ClearanceHoldKey=null;PlanningDeferred=false;PlanningDeferredKey=null;
        plans.Clear();entryCosts.Clear();replacements.Clear();
        shortChangeRoad=null;CancelPlannedStop();
    }

    public void Discover(PlannedPathData data,Vector3 truck,ETS2LA.Game.SDK.Semaphore[] sdkGates,double speed=0)
    {
        if(data.HasError)return;
        // Warm only the bounded model worker; no lane or SDK gate is inferred.
        // This can finish before the game's short-range gate feed sees the plaza.
        PrefetchIssue=null;
        if(prefetch!=null)foreach(var prefab in data.PlannedPath.Where(i=>i.prefabPath!=null)
            .Select(i=>i.prefabPath!.Prefab).Where(p=>p.Descriptor?.NavCurves.Any(c=>c.SemaphoreId>=0)==true).Distinct().Take(4)) {
            try {prefetch(prefab);}catch(Exception ex) {PrefetchIssue=ex.Message;}
        }
        for(int index=0;index<data.PlannedPath.Count;index++) {
            var item=data.PlannedPath[index];
            var p=item.prefabPath;
            if(p==null)continue;
            p=PrefabRefinement.OriginalFor(p);
            var mapped=TollSignals.ForPath(p).ToArray();
            bool Live(ParsedSemaphore s)=>sdkGates.Any(g=>g.type==ETS2LA.Game.SDK.SemaphoreType.GATE&&
                g.id==s.Semaphore.SemaphoreId&&Vector3.Distance(g.GetWorldCoordinates(),s.GetWorldOrientedPoint().Position)<1);
            var liveRoute=mapped.Where(Live).ToArray();
            // An already inspected toll model can plan the physical lane before
            // the short-range SDK feed sees any gate. Pending/unknown models
            // remain prefetch-only; stored user preferences are not model proof.
            IReadOnlyDictionary<int,TollPayment> knownPayments;
            try {knownPayments=readPayments?.Invoke(p.Prefab)??new Dictionary<int,TollPayment>();}
            catch(Exception ex) when(liveRoute.Length==0){PrefetchIssue=ex.Message;continue;}
            bool earlyModel=readClearance!=null&&knownPayments.Values.Any(mode=>mode is TollPayment.Automatic or TollPayment.Manual);
            var routeGates=liveRoute.Length>0?liveRoute:earlyModel?mapped:[];
            if(routeGates.Length==0)continue;
            var first=routeGates[0];var world=first.GetWorldOrientedPoint().Position;
            var headingDelta=first.GetCurveEndWorldCoordinates().Position-first.GetCurveStartWorldCoordinates().Position;
            if(!float.IsFinite(world.X)||!float.IsFinite(world.Y)||!float.IsFinite(world.Z)||!float.IsFinite(headingDelta.LengthSquared())||headingDelta.LengthSquared()<.01)continue;
            var forward=Vector3.Normalize(headingDelta);
            if(Vector3.Dot(world-truck,forward)<-10 || Vector3.Distance(world,truck)>1000)continue;
            var key=Key(p);
            // Do not replace a pending stop with a newly planned/different plaza.
            if(StopBoundary.HasValue && key!=StopKey)return;
            modelPayments=knownPayments;
            double now=System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
            if(CurrentKey==key && SelectedGate.HasValue) {
                if(PaymentFor(SelectedGate.Value)==TollPayment.Manual&&now-lastSearch>=.5) {
                    lastSearch=now;
                    TryUpgradeAutomatic(data,index,p,truck,sdkGates,speed);
                }
                return;
            }
            if(CurrentKey==key && now-lastSearch < .5)return;
            lastSearch=now;
            plans.Clear();
            entryCosts.Clear();
            replacements.Clear();
            var right=Vector3.Cross(forward,Vector3.UnitY);
            var lanes=new List<(int gate,float lateral,bool reachable)>();
            var attempts=new List<LaneAttempt>();
            var anchors=new Dictionary<int,(Vector3 Anchor,Vector3 Heading)>();var ambiguous=new HashSet<int>();
            foreach(var c in p.Prefab.Descriptor!.NavCurves.Where(c=>c.SemaphoreId>=0)) {
                var single=(PrefabPath)clone.Invoke(p,null)!;single.Curves=[c];single.Length=c.Length;
                var sem=TollSignals.ForPath(single).FirstOrDefault();if(sem==null)continue;
                var delta=sem.GetCurveEndWorldCoordinates().Position-sem.GetCurveStartWorldCoordinates().Position;
                if(!float.IsFinite(delta.LengthSquared())||delta.LengthSquared()<.01)continue;
                var heading=Vector3.Normalize(delta);
                if(Vector3.Dot(heading,forward)<.8)continue;
                bool live=Live(sem);
                if(!live&&!(earlyModel&&knownPayments.GetValueOrDefault(c.SemaphoreId) is TollPayment.Automatic or TollPayment.Manual))continue;
                var anchor=sem.GetWorldOrientedPoint().Position;
                if(anchors.TryGetValue(c.SemaphoreId,out var existingAnchor)&&
                    (Vector3.Distance(existingAnchor.Anchor,anchor)>1||Vector3.Dot(existingAnchor.Heading,heading)<.95f))ambiguous.Add(c.SemaphoreId);
                else anchors[c.SemaphoreId]=(anchor,heading);
                var alternatives = new[] { p }.Concat(item.otherPrefabPaths ?? [])
                    .Where(option => Key(option) == key);
                foreach (var alternative in alternatives)
                {
                    var original=PrefabRefinement.OriginalFor(alternative);
                    if(original.Curves.Count==0) {
                        attempts.Add(new(c.SemaphoreId,PaymentFor(c.SemaphoreId).ToString(),-1,-1,"InvalidSourceShape",[],null));
                        continue;
                    }
                    int firstCurve=p.Prefab.Descriptor.NavCurves.IndexOf(original.Curves.First()),lastCurve=p.Prefab.Descriptor.NavCurves.IndexOf(original.Curves.Last());
                    var search=findClearRoute?.Invoke(alternative,c.SemaphoreId);
                    var route = findClearRoute!=null?search?.Path:ViaGate(alternative, c.SemaphoreId);
                    string? rejected=route==null?search?.Reason??"NoDirectedTruckPath":readClearance?.Invoke(route)==false?"ClearanceUnavailable":null;
                    var entry=rejected==null?TollEntry.Plan(data,index,p,route!,truck,speed):null;
                    rejected??=entry==null?"EntryNotReachable":null;
                    attempts.Add(new(c.SemaphoreId,PaymentFor(c.SemaphoreId).ToString(),firstCurve,lastCurve,rejected??"Reachable",search?.BlockedCurves??[],search?.Evidence));
                    if(rejected!=null||entry==null||route==null)continue;
                    var cost = (entry.SlowRoad != null, entry.Apply.Count);
                    if (entryCosts.TryGetValue(c.SemaphoreId,out var existing) && cost.CompareTo(existing) >= 0) continue;
                    entryCosts[c.SemaphoreId]=cost;
                    plans[c.SemaphoreId]=route;
                    if (cost == (false,0)) break;
                }
                lanes.Add((c.SemaphoreId,Vector3.Dot(sem.GetWorldOrientedPoint().Position-world,right),plans.ContainsKey(c.SemaphoreId)));
            }
            // The existing preference/choice API identifies one gate per local
            // ID. Do not squeeze two physical booths into that single identity.
            foreach(int gate in ambiguous){plans.Remove(gate);entryCosts.Remove(gate);}
            CurrentKey=key;
            Choices=lanes.GroupBy(l=>l.gate).Select(g=>g.OrderByDescending(l=>l.lateral).First())
                .OrderByDescending(l=>l.lateral).Select((l,i)=>new {l,i}).Where(x=>x.l.reachable&&!ambiguous.Contains(x.l.gate))
                .Select(x=>new LaneChoice(x.l.gate,$"{x.i+1}. Durchfahrt von rechts")).ToArray();
            SelectedGate=preferences.Gates.TryGetValue(key,out var chosen)?chosen:null;
            SelectAutomatic();
            LaneAttempts=attempts.ToArray();
            LaneDecisionContext=new{utc=DateTimeOffset.UtcNow,plaza=CurrentKey,truck=new{truck.X,truck.Y,truck.Z},speed,
                selected=SelectedGate,routePlan=RouteSnapshot.Describe(RouteSnapshot.Capture(data))};
            LaneAttemptContext=LaneDecisionContext;
            return;
        }
        if(CurrentKey!=null&&!StopBoundary.HasValue&&!data.PlannedPath.Any(i=>i.prefabPath!=null&&Key(i.prefabPath)==CurrentKey))ResetSelection();
    }
    void RefreshSelectedBarrier(PlannedItem? applied=null)
    {
        SelectedBarrier=null;
        if(CurrentKey==null||SelectedGate is not int gate||!plans.TryGetValue(gate,out var selected))return;
        var original=PrefabRefinement.OriginalFor(selected);
        var signals=TollSignals.ForPath(original).Where(s=>s.Semaphore.SemaphoreId==gate).ToArray();
        if(signals.Length!=1)return;
        var signal=signals[0];var anchor=signal.GetWorldOrientedPoint().Position;
        var heading=signal.GetCurveEndWorldCoordinates().Position-signal.GetCurveStartWorldCoordinates().Position;
        if(!float.IsFinite(anchor.X)||!float.IsFinite(anchor.Y)||!float.IsFinite(anchor.Z)||!float.IsFinite(heading.LengthSquared())||heading.LengthSquared()<.01)return;
        SelectedBarrier=new(CurrentKey,original.Prefab.Prefab.Uid,gate,anchor,Vector3.Normalize(heading),applied==null?null:RouteMatcher.ItemIdentity(applied));
    }

    // An initially unavailable automatic lane may become proven before the
    // split. Search separately, retain the committed lane on any failure, and
    // let Apply recompute and validate actual changes against the current plan.
    void TryUpgradeAutomatic(PlannedPathData data,int index,PrefabPath current,Vector3 truck,
        ETS2LA.Game.SDK.Semaphore[] sdkGates,double speed)
    {
        if(shortChangeRoad!=null||data.PlannedPath.Any(i=>i.targetLaneIndex!=0))return;
        try {
            var trace=RouteMatcher.SampleForTracking(data,truck,speed);
            if(BoundaryCoverage(trace,data.PlannedPath[index],CommonApproachEnd(current),truck,out double remaining)!=PrefixCoverage.Connected||
                remaining<Math.Max(15,Math.Max(0,speed)*1.5+10))return;
            var trial=new TollRouter(preferences,readPayments,readClearance,prefetch,readManualStop,findClearRoute);
            var snapshot=RouteSnapshot.Capture(data);
            trial.Discover(snapshot,truck,sdkGates,speed);
            LaneAttempts=trial.LaneAttempts;
            LaneAttemptContext=trial.LaneAttemptContext;
            if(trial.CurrentKey!=CurrentKey||trial.SelectedGate is not int gate||trial.PaymentFor(gate)!=TollPayment.Automatic||
                !trial.plans.TryGetValue(gate,out var candidate)||!CanRestoreApplied(data,index,current,candidate,truck,speed)||
                readClearance?.Invoke(candidate)==false)return;
            plans.Clear();foreach(var pair in trial.plans)plans[pair.Key]=pair.Value;
            entryCosts.Clear();foreach(var pair in trial.entryCosts)entryCosts[pair.Key]=pair.Value;
            replacements.Clear(); // The prior manual mapping is not the new automatic path.
            Choices=trial.Choices;modelPayments=trial.modelPayments;SelectedGate=gate;
            LaneDecisionContext=trial.LaneDecisionContext;
            CancelPlannedStop();Status=LaneStatus(gate);RefreshSelectedBarrier();
        }catch(InvalidOperationException) { /* No route proof: retain the current lane. */ }
    }
    void SelectAutomatic()
    {
        if(CurrentKey==null)return;
        var reachable=Choices.OrderBy(c=>entryCosts[c.GateId]).ToArray();
        var automatic=reachable.Where(c=>PaymentFor(c.GateId)==TollPayment.Automatic).ToArray();
        SelectedGate=automatic.Any(c=>c.GateId==SelectedGate)?SelectedGate:
            automatic.FirstOrDefault()?.GateId ?? reachable.FirstOrDefault(c=>
                PaymentFor(c.GateId)==TollPayment.Manual && modelPayments.GetValueOrDefault(c.GateId)==TollPayment.Manual)?.GateId;
        if (SelectedGate.HasValue) CancelPlannedStop();
        Status=SelectedGate is int gate?LaneStatus(gate):
            "Keine eindeutig passende Lkw-Mautspur – manuell übernehmen";
        RefreshSelectedBarrier();
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
        CleanupAbsentHold(data);
        if(PlanningDeferred)return null;
        if(StopBoundary is not Vector3 boundary)return null;
        var trace=RouteMatcher.SampleForTracking(data,truck,speed);
        var item=data.PlannedPath.FirstOrDefault(i=>i.prefabPath!=null&&Key(i.prefabPath)==StopKey);
        if(item==null){CancelPlannedStop();return null;}
        var coverage=BoundaryCoverage(trace,item,boundary,truck,out double distance);
        if(coverage==PrefixCoverage.Deferred){Defer(CurrentKey);return null;}
        if(coverage!=PrefixCoverage.Connected){UnknownHold(CurrentKey,"Maut-Haltegrenze oder aktuelle Position ist nicht eindeutig – geplanter Halt bis zur erneuten Prüfung");return null;}
        StopDistance=distance;
        return StopDistance;
    }

    enum PrefixCoverage {Connected,Deferred,Unknown}
    static PrefixCoverage ItemCoverage(RouteTrace trace,PlannedItem item)
    {
        if(!trace.IdentityKnown)return PrefixCoverage.Unknown;
        if(!RouteMatcher.TryItemIdentity(item,out var identity))return trace.EndsAtGap?PrefixCoverage.Deferred:PrefixCoverage.Unknown;
        if(trace.TryItemRange(identity,out _,out _))return PrefixCoverage.Connected;
        // SampleForTracking only returns a gap prefix when the current truck
        // still has the required native stopping reserve. No permission is
        // inferred for items outside its exact directed identities.
        return trace.EndsAtGap?PrefixCoverage.Deferred:PrefixCoverage.Unknown;
    }
    static bool TryBoundaryDistance(RouteTrace trace,PlannedItem item,Vector3 point,Vector3 truck,out double distance)
    {
        distance=0;
        if(!trace.IdentityKnown||!RouteMatcher.TryItemIdentity(item,out var identity)||!trace.TryItemRange(identity,out double start,out double end))return false;
        var target=trace.ProjectNearForTracking(point,(start+end)/2,(end-start)/2+.00001);
        var current=BarrierBinding.UniqueCurrent(trace,truck,target.Along);
        if(target.Offset>1||Math.Abs(target.HeightOffset)>.5||target.Along<start-.01||target.Along>end+.01||
            current==null||current.Offset>3||Math.Abs(current.HeightOffset)>2||trace.EndsAtGap&&target.Along>=trace.Length-.01)return false;
        distance=target.Along-current.Along-ApproachStop.BufferMetres;
        return double.IsFinite(distance);
    }
    static PrefixCoverage BoundaryCoverage(RouteTrace trace,PlannedItem item,Vector3 point,Vector3 truck,out double distance)
    {
        distance=0;
        if(BarrierBinding.UniqueCurrent(trace,truck,trace.Length)==null)return PrefixCoverage.Unknown;
        var itemCoverage=ItemCoverage(trace,item);
        if(itemCoverage!=PrefixCoverage.Connected)return itemCoverage;
        if(TryBoundaryDistance(trace,item,point,truck,out distance))return PrefixCoverage.Connected;
        if(trace.EndsAtGap&&RouteMatcher.TryItemIdentity(item,out var identity)&&trace.TryItemRange(identity,out _,out double end)&&end>=trace.Length-.01) {
            var projected=trace.Project(point);
            if(projected.Along>=trace.Length-.01)return PrefixCoverage.Deferred;
        }
        return PrefixCoverage.Unknown;
    }
    void ClearOwnedHold(string? key)
    {
        if(ClearanceHoldKey!=key)return;
        ClearanceHoldRequested=false;ClearanceHoldKey=null;
    }
    void CleanupAbsentHold(PlannedPathData data)
    {
        if(data.HasError||data.PlannedPath.Count==0)return;
        if(ClearanceHoldKey is { } owner&&!data.PlannedPath.Any(i=>i.prefabPath!=null&&Key(i.prefabPath)==owner))ClearOwnedHold(owner);
    }
    void UnknownHold(string? key,string status)
    {
        // One currently represented owner continues to dominate a new unknown
        // request. Never overwrite its identity and later clear it as another
        // plaza's deferred metadata.
        if(key!=null&&(!ClearanceHoldRequested||ClearanceHoldKey==key)){ClearanceHoldRequested=true;ClearanceHoldKey=key;}
        PlanningDeferred=false;PlanningDeferredKey=null;
        if(StopKey==key)CancelPlannedStop();SelectedBarrier=null;Status=status;
    }
    void Defer(string? key)
    {
        PlanningDeferred=true;PlanningDeferredKey=key;
        ClearOwnedHold(key);
        if(StopKey==key)CancelPlannedStop();
        Status="Mautplanung vorgemerkt – zuerst den tatsächlich verbundenen Routenabschnitt verfolgen";
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

    public static PrefabPath? ViaGate(PrefabPath source,int gate,IReadOnlyList<bool>? blockedCurves=null,bool allowSmallVehiclePreference=false)
    {
        source=PrefabRefinement.OriginalFor(source);
        var all=source.Prefab.Descriptor?.NavCurves;
        if(all==null || source.Curves.Count==0||blockedCurves!=null&&blockedCurves.Count!=all.Count)return null;
        var graph=all.Select((c,i)=>new CurveLink(i,c.NextLines.ToArray(),c.Length,
            (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles ||
                allowSmallVehiclePreference&&blockedCurves!=null&&c.AllowedVehicles==AllowedVehicles.SmallVehicles) &&
            c.TrafficRule.ToString() != "escort_only" && (blockedCurves==null||!blockedCurves[i]),c.SemaphoreId)).ToArray();
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
        CleanupAbsentHold(data);
        bool matched=false;
        for(int itemIndex=0;itemIndex<data.PlannedPath.Count;itemIndex++)
        {
            var item=data.PlannedPath[itemIndex];
            var current=item.prefabPath;
            if(current==null || Key(current)!=CurrentKey)continue;
            matched=true;
            RouteTrace prefix;
            try {prefix=RouteMatcher.SampleForTracking(data,truck,speed);}
            catch(InvalidOperationException ex){error=ex.Message;return false;}
            if(BarrierBinding.UniqueCurrent(prefix,truck,prefix.Length)==null){UnknownHold(CurrentKey,"Lkw-Position im verbundenen Routenabschnitt ist mehrdeutig – geplanter Halt bis zur erneuten Prüfung");return true;}
            var admitted=ItemCoverage(prefix,item);
            if(admitted==PrefixCoverage.Deferred){Defer(CurrentKey);return true;}
            PlanningDeferred=false;PlanningDeferredKey=null;
            if(admitted!=PrefixCoverage.Connected) {
                SelectedBarrier=null;
                if(TryHoldBeforeBranch(data,item,current,truck,speed))return true;
                UnknownHold(CurrentKey,"Aktueller Mautabschnitt ist nicht eindeutig im verbundenen Routenpräfix enthalten – geplanter Halt");return true;
            }
            if(!SelectedGate.HasValue) {
                SelectedBarrier=null;
                var boundary=CommonApproachEnd(current);
                var boundaryCoverage=BoundaryCoverage(prefix,item,boundary,truck,out double distance);
                if(boundaryCoverage==PrefixCoverage.Deferred){Defer(CurrentKey);return true;}
                if(boundaryCoverage!=PrefixCoverage.Connected){UnknownHold(CurrentKey,"Maut-Haltegrenze ist im verbundenen Routenabschnitt nicht eindeutig verfolgbar – geplanter Halt");return true;}
                StopBoundary=boundary;StopKey=CurrentKey;StopDistance=distance;
                item.allowedToLaneChange=false;
                Status="Unbestätigte Mautspur: geplanter Halt vor der Aufteilung; F9 zur Übernahme";
                return true;
            }
            // The gate must be reachable from the existing entry lane and exit. Never
            // invent a lateral jump at a road boundary or use the opposite carriageway.
            bool alreadyApplied=replacements.TryGetValue(current,out var replacement);
            if(!alreadyApplied) {
                // Native refreshes may allocate a new wrapper around the exact
                // applied directed prefab route. This proves topology only;
                // current clearance is still checked below on every call.
                replacement=replacements.Values.Distinct().FirstOrDefault(known=>SameAppliedRoute(current,known));
                if(replacement!=null)alreadyApplied=true;
            }
            TollEntry.Changes? pendingChanges=null;
            if(!alreadyApplied)
            {
                replacement=plans.TryGetValue(SelectedGate.Value,out var plan) ? plan : ViaGate(current,SelectedGate.Value);
                var changes = replacement == null ? null : TollEntry.Plan(data,itemIndex,current,replacement,truck,speed);
                if (changes == null || replacement == null || readClearance?.Invoke(replacement)==false)
                {
                    if (TryHoldBeforeBranch(data,item,current,truck,speed)) return true;
                    error="Keine erreichbare Lkw-Mautspur und zu wenig Strecke für einen geplanten Halt.";
                    return false;
                }
                // Validate the new exit and check for a late lateral jump before
                // committing lane changes to the host's mutable route.
                var oldPoint=current.Interpolate(current.GetFactorForPoint(truck),0)?.Position;
                var newPoint=replacement.Interpolate(replacement.GetFactorForPoint(truck),0)?.Position;
                if(oldPoint.HasValue&&newPoint.HasValue&&Vector3.Distance(truck,oldPoint.Value)<5&&Vector3.Distance(oldPoint.Value,newPoint.Value)>2)
                {
                    if (TryHoldBeforeBranch(data,item,current,truck,speed)) return true;
                    error="Zu spät für den Wechsel zur bestätigten Lkw-Spur. Bitte manuell einordnen.";
                    return false;
                }
                if (itemIndex+1 < data.PlannedPath.Count)
                {
                    var check = RouteSnapshot.Capture(data);
                    check.PlannedPath[itemIndex].prefabPath = replacement;
                    if (!ExitHandoff.Align(check,itemIndex,truck,out error))
                    {
                        if (!TryHoldBeforeBranch(data,item,current,truck,speed)) return false;
                        error=null;
                        return true;
                    }
                }
                pendingChanges=changes;
            }
            if(replacement==null){error="Bestätigte Mautspur fehlt in der aktuellen Planung.";return false;}
            if(readClearance?.Invoke(replacement)==false) {
                // This exact mapping was already committed after topology AND
                // clearance validation. A new/pending rig-envelope certificate
                // revokes movement permission, not the physical lane topology.
                // The caller MUST aggregate a finite normal proof-stop while
                // this flag is set; it is never an old certificate permission.
                if(alreadyApplied) {
                    if(!ClearanceHoldRequested||ClearanceHoldKey==CurrentKey){ClearanceHoldRequested=true;ClearanceHoldKey=CurrentKey;}
                    Status="Mautspur bleibt erhalten – geplanter Halt bis zur aktuellen Geometrie-Freigabe";
                    if(!ReferenceEquals(current,replacement)) {
                        if(SameAppliedRoute(current,replacement)) {
                            replacements[current]=current;
                            RefreshSelectedBarrier(item);
                        } else if(CanRestoreApplied(data,itemIndex,current,replacement,truck,speed)) {
                            item.prefabPath=replacement;item.otherPrefabPaths=[replacement];item.allowedToLaneChange=false;
                            if(ExitHandoff.Align(data,itemIndex,truck,out _))RefreshSelectedBarrier(item);
                            else SelectedBarrier=null;
                        } else {
                            // A known source-wrapper key is not proof that an
                            // old selected route is physically restorable now.
                            SelectedBarrier=null;
                            Status="Aktuelle Mautspur nicht wiederherstellbar – geplanter Halt bis zur erneuten Prüfung";
                        }
                    }
                    return true;
                }
                if(TryHoldBeforeBranch(data,item,current,truck,speed))return true;
                error="Geometrie-Freigabe vor dem ersten Einspielen der Mautspur nicht verfügbar.";
                return false;
            }
            ClearOwnedHold(CurrentKey);
            if(!ReferenceEquals(current,replacement))
            {
                // Changing an already diverged lane would steer across islands.
                var oldPoint=current.Interpolate(current.GetFactorForPoint(truck),0)?.Position;
                var newPoint=replacement.Interpolate(replacement.GetFactorForPoint(truck),0)?.Position;
                if(oldPoint.HasValue&&newPoint.HasValue&&Vector3.Distance(truck,oldPoint.Value)<5&&Vector3.Distance(oldPoint.Value,newPoint.Value)>2) {
                    error="Zu spät für den Wechsel zur bestätigten Lkw-Spur. Bitte manuell einordnen.";return false;
                }
                if(pendingChanges!=null) {
                    foreach(var change in pendingChanges.Apply)change();
                    if(pendingChanges.SlowRoad!=null)shortChangeRoad=pendingChanges.SlowRoad;
                    pendingChanges=null;
                }
                item.prefabPath=replacement;
                item.otherPrefabPaths=[replacement];
                item.allowedToLaneChange=false;
            }
            if(pendingChanges!=null) {
                foreach(var change in pendingChanges.Apply)change();
                if(pendingChanges.SlowRoad!=null)shortChangeRoad=pendingChanges.SlowRoad;
            }
            if(!ExitHandoff.Align(data,itemIndex,truck,out error))return false;
            if(replacements.Count>128)replacements.Clear();
            replacements[current]=replacement;replacements[replacement]=replacement;
            RefreshSelectedBarrier(item);
            Status=LaneStatus(SelectedGate.Value);
        }
        if(!matched&&CurrentKey!=null)Defer(CurrentKey);
        return true;
    }

    static bool SameAppliedRoute(PrefabPath candidate,PrefabPath applied)
    {
        var a=PrefabRefinement.OriginalFor(candidate);var b=PrefabRefinement.OriginalFor(applied);
        if(Key(a)!=Key(b)||a.CurveDirection!=b.CurveDirection||!ReferenceEquals(a.Prefab,b.Prefab)||
            !ReferenceEquals(a.Prefab.Descriptor,b.Prefab.Descriptor)||!a.Curves.SequenceEqual(b.Curves))return false;
        foreach(float factor in new[]{.00001f,.25f,.5f,.75f,.99999f}) {
            var p=a.Interpolate(factor,0);var q=b.Interpolate(factor,0);
            if(!p.HasValue||!q.HasValue||p.Value.Position!=q.Value.Position)return false;
        }
        return true;
    }

    static bool CanRestoreApplied(PlannedPathData data,int index,PrefabPath current,PrefabPath applied,Vector3 truck,double speed)
    {
        var changes=TollEntry.Plan(data,index,current,applied,truck,speed);
        if(changes==null||changes.Apply.Count!=0)return false;
        var oldPoint=current.Interpolate(current.GetFactorForPoint(truck),0)?.Position;
        var newPoint=applied.Interpolate(applied.GetFactorForPoint(truck),0)?.Position;
        if(!oldPoint.HasValue||!newPoint.HasValue||Vector3.Distance(truck,oldPoint.Value)<5&&Vector3.Distance(oldPoint.Value,newPoint.Value)>2)return false;
        var check=RouteSnapshot.Capture(data);check.PlannedPath[index].prefabPath=applied;
        return ExitHandoff.Align(check,index,truck,out _);
    }

    bool TryHoldBeforeBranch(PlannedPathData data, PlannedItem item, PrefabPath approach, Vector3 truck, double speed)
    {
        try
        {
            var boundary = CommonApproachEnd(approach);
            var trace = RouteMatcher.SampleForTracking(data, truck, speed);
            var coverage=BoundaryCoverage(trace,item,boundary,truck,out double distance);
            if(coverage==PrefixCoverage.Deferred){Defer(CurrentKey);return true;}
            if(coverage!=PrefixCoverage.Connected)return false;
            _ = ApproachStop.Evaluate(speed, distance);
            SelectedGate = null;
            SelectedBarrier = null;
            StopBoundary = boundary;
            StopKey = CurrentKey;
            StopDistance = distance;
            item.allowedToLaneChange = false;
            Status = "Mautspur nicht mehr erreichbar: geplanter Halt vor der Aufteilung";
            return true;
        }
        catch (InvalidOperationException) { return false; }
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
