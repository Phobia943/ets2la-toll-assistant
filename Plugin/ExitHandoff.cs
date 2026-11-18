using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;

namespace MautAssistent;

/// <summary>Join a confirmed toll path to an existing continuation of the same route.</summary>
public static class ExitHandoff
{
    internal static int RepairRoadConnections(PlannedPathData data, Vector3 truck)
    {
        int repairs = 0;
        for (int index=0; index+1<data.PlannedPath.Count; index++)
        {
            var before = data.PlannedPath[index];
            var next = data.PlannedPath[index+1];
            if (before.prefabPath is not { } connector || before.endNode.Uid != next.startNode.Uid ||
                next.laneIndex == 0 || next.item is not (ParsedRoad or ParsedRoadList)) continue;
            var end = connector.Interpolate(.99999f,0)?.Position;
            if (end == null) continue;

            float factor = next.direction == Direction.Backward ? 1 : 0;
            var side = next.laneIndex > 0 ? Side.Right : Side.Left;
            int count = next.item is ParsedRoadList roads ? roads.GetLaneCount(side) : ((ParsedRoad)next.item).GetLaneCount(side);
            if (Math.Abs((long)next.laneIndex) > count) continue;
            int lane = Math.Abs(next.laneIndex)-1;
            var entry = next.item is ParsedRoadList list ? list.InterpolateLane(factor,side,lane).Position
                : ((ParsedRoad)next.item).InterpolateLane(factor,side,lane).Position;
            int originalLane = next.laneIndex;
            if (Vector3.Distance(end.Value,entry)>2 && Align(data,index,truck,out _) && next.laneIndex!=originalLane)
                repairs++;
        }
        // Pruning can leave a road as the first item. Its exit still has to
        // meet the NEXT directed prefab, not merely the earlier toll exit.
        for(int index=0;index+1<data.PlannedPath.Count;index++) {
            if(data.PlannedPath[index].item is not (ParsedRoad or ParsedRoadList)||data.PlannedPath[index+1].prefabPath==null)continue;
            var basis=CaptureBasis(data);var staged=RouteSnapshot.Capture(data);
            if(EnsureContinuation(staged,index,truck,3,double.NaN,null,null,out _)&&TryCommit(data,staged,basis,out bool changed)&&changed)repairs++;
        }
        return repairs;
    }

    public static bool Align(PlannedPathData data,int index,Vector3 truck,out string? error)
        => Align(data,index,truck,double.NaN,out _,out error);
    // F9 can leave a target on the current road after the toll was pruned.
    // Reacquire its speed obligation without weakening legacy seam repair.
    internal static bool AdoptSlowTargets(PlannedPathData data,Vector3 truck,double speed,
        Func<PlannedItem,bool> knownExit,Func<PlannedItem,bool> ownsSlowRoad,out PlannedItem[] roads,out string? error)
    {
        roads=[];error=null;
        if(data.HasError||!double.IsFinite(speed)||speed<0){error="Kein aktueller langsamer Routenanschluss.";return false;}
        var basis=CaptureBasis(data);var staged=RouteSnapshot.Capture(data);var slow=new List<int>();
        for(int index=0;index<Math.Min(4,staged.PlannedPath.Count);index++) {
            var item=staged.PlannedPath[index];
            if(item.item is not (ParsedRoad or ParsedRoadList)||item.targetLaneIndex==0||item.targetLaneIndex==item.laneIndex||!knownExit(item))continue;
            if(!ExistingSlowTarget(staged,index,truck,speed,slow,ownsSlowRoad,out error))return false;
        }
        if(!TryCommit(data,staged,basis,out _)){error="Routenbasis während der Tempoprüfung geändert.";return false;}
        roads=slow.Distinct().Select(i=>data.PlannedPath[i]).ToArray();return true;
    }

    // Short exits require the caller to own a speed constraint for every
    // returned native road item. NaN preserves the conservative legacy API.
    public static bool Align(PlannedPathData data,int index,Vector3 truck,double speed,
        out PlannedItem[] slowRoads,out string? error,Func<PlannedItem,bool>? ownsSlowRoad=null)
    {
        slowRoads=[];
        if(data.HasError||index<0||index>=data.PlannedPath.Count){error="Mautausfahrt besitzt keinen gültigen Routenabschnitt.";return false;}
        var basis=CaptureBasis(data);var staged=RouteSnapshot.Capture(data);var slow=new List<int>();
        if(!AlignImmediate(staged,index,truck,out error)||
            !EnsureContinuation(staged,index+1,truck,3,speed,slow,ownsSlowRoad,out error))return false;
        if(!TryCommit(data,staged,basis,out _)){error="Routenbasis wurde während der Anschlussprüfung geändert.";return false;}
        slowRoads=slow.Distinct().Select(i=>data.PlannedPath[i]).ToArray();
        return true;
    }

    // All edits are staged. A rejected following connection cannot leave half
    // of an exit repair committed to the host's shared planning items.
    static readonly FieldInfo[] itemFields=typeof(PlannedItem).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
    sealed record ItemBasis(PlannedItem Item,object?[] Values,PrefabPath[]? Options);
    sealed record PlanBasis(object Container,bool HasError,ItemBasis[] Items);
    static PlanBasis CaptureBasis(PlannedPathData data)=>new(data.PlannedPath,data.HasError,data.PlannedPath.Select(i=>
        new ItemBasis(i,itemFields.Select(f=>f.GetValue(i)).ToArray(),i.otherPrefabPaths?.ToArray())).ToArray());
    // Host geometry is immutable; all mutable item fields (including native
    // lane-change state) must still equal the pre-staging basis. The caller
    // owns route mutation serialization during this short compare/commit.
    static bool TryCommit(PlannedPathData actual,PlannedPathData staged,PlanBasis basis,out bool changed)
    {
        changed=false;
        if(!ReferenceEquals(actual.PlannedPath,basis.Container)||actual.HasError!=basis.HasError||
            actual.PlannedPath.Count!=basis.Items.Length||staged.PlannedPath.Count!=basis.Items.Length)return false;
        for(int i=0;i<basis.Items.Length;i++) {
            var saved=basis.Items[i];var current=actual.PlannedPath[i];
            if(!ReferenceEquals(current,saved.Item)||itemFields.Where((f,n)=>!Equals(f.GetValue(current),saved.Values[n])).Any()||
                !(current.otherPrefabPaths??[]).SequenceEqual(saved.Options??[]))return false;
        }
        for(int i=0;i<actual.PlannedPath.Count;i++) {
            var a=actual.PlannedPath[i];var b=staged.PlannedPath[i];
            bool lane=a.laneIndex!=b.laneIndex,target=a.targetLaneIndex!=b.targetLaneIndex;
            if(lane||target) {
                a.laneIndex=b.laneIndex;
                if(a.item is ParsedRoad or ParsedRoadList) {
                    a.targetLaneIndex=a.laneIndex;
                    a.GenerateLaneChange(a.direction==Direction.Backward?1:0,false);
                }
                a.targetLaneIndex=b.targetLaneIndex;changed=true;
            }
            if(!ReferenceEquals(a.prefabPath,b.prefabPath)) {a.prefabPath=b.prefabPath;changed=true;}
            if(!(a.otherPrefabPaths??[]).SequenceEqual(b.otherPrefabPaths??[])) {a.otherPrefabPaths=b.otherPrefabPaths??[];changed=true;}
            if(a.allowedToLaneChange!=b.allowedToLaneChange){a.allowedToLaneChange=b.allowedToLaneChange;changed=true;}
        }
        return true;
    }

    static bool EnsureContinuation(PlannedPathData data,int index,Vector3 truck,int depth,double speed,List<int>? slow,Func<PlannedItem,bool>? ownsSlowRoad,out string? error)
    {
        error=null;if(index<0||index+1>=data.PlannedPath.Count)return true;
        var before=data.PlannedPath[index];var next=data.PlannedPath[index+1];
        if(before.endNode.Uid!=next.startNode.Uid){error="Folgeabschnitte besitzen keinen gemeinsamen gerichteten Routenknoten.";return false;}
        if(depth<=0) {
            if(DirectConnection(before,next))return true;
            error="Begrenzte Anschlussprüfung erreicht den unveränderten Restpfad nicht eindeutig.";return false;
        }
        if(double.IsFinite(speed)&&before.item is ParsedRoad or ParsedRoadList&&
            before.targetLaneIndex!=0&&before.targetLaneIndex!=before.laneIndex&&
            !ExistingSlowTarget(data,index,truck,speed,slow!,ownsSlowRoad,out error))return false;
        // Retaining an already connected host choice grants no new vehicle or
        // payment permission. In particular a separately proven Automatic path
        // may legitimately carry the AI SmallVehicles preference. Any changed
        // connector below still has to satisfy the strict candidate contract.
        if(DirectConnection(before,next))return EnsureContinuation(data,index+1,truck,depth-1,speed,slow,ownsSlowRoad,out error);
        if(before.item is ParsedRoad or ParsedRoadList && next.prefabPath!=null) {
            if(!RoadToPrefab(data,index,truck,speed,slow,out error))return false;
        } else if(before.prefabPath!=null) {
            if(!AlignImmediate(data,index,truck,out error))return false;
        } else if(!DirectConnection(before,next)) {
            error="Unveränderter Folgeabschnitt besitzt keinen nachgewiesenen geometrischen Anschluss.";return false;
        }
        return EnsureContinuation(data,index+1,truck,depth-1,speed,slow,ownsSlowRoad,out error);
    }

    static bool ExistingSlowTarget(PlannedPathData data,int index,Vector3 truck,double speed,List<int> slow,
        Func<PlannedItem,bool>? ownsSlowRoad,out string? error)
    {
        error=null;var road=data.PlannedPath[index];bool reverse=road.direction==Direction.Backward;
        double length=road.GetLength();float end=reverse?0:1,factor=road.GetFactorFor(truck);
        if(!double.IsFinite(length)||length<=0||!float.IsFinite(factor)||
            !RouteSnapshot.TryLaneChangeStart(road,out float began)) {error="Native Spurwechselphase ist nicht nachgewiesen.";return false;}
        double nativeDistance=Math.Abs(end-(float.IsNaN(began)?factor:began))*length;
        if(!double.IsFinite(nativeDistance)) {error="Native Spurwechselphase ist ungültig.";return false;}
        if(nativeDistance>=TollEntry.FullChangeMetres)return true;
        if(ownsSlowRoad?.Invoke(road)==true)return true;
        var side=road.laneIndex<0?Side.Left:Side.Right;
        int count=road.item is ParsedRoadList list?list.GetLaneCount(side):((ParsedRoad)road.item).GetLaneCount(side);
        if(!road.allowedToLaneChange||road.laneIndex==0||Math.Sign(road.laneIndex)!=Math.Sign(road.targetLaneIndex)||
            Math.Abs(road.targetLaneIndex-road.laneIndex)!=1||Math.Abs((long)road.laneIndex)>count||Math.Abs((long)road.targetLaneIndex)>count) {
            error="Kurzer vorhandener Spurwechsel besitzt keine gültige Nachbarspur.";return false;
        }
        Vector3 Point(float t)=>road.item is ParsedRoadList group?group.InterpolateLane(t,side,Math.Abs(road.laneIndex)-1).Position:
            ((ParsedRoad)road.item).InterpolateLane(t,side,Math.Abs(road.laneIndex)-1).Position;
        bool onRoad=Vector3.Distance(Point(factor),truck)<=3;
        double remaining=Math.Abs(end-factor)*length;
        bool allowed=onRoad&&speed>=0&&speed<=TollEntry.ShortChangeSpeed&&remaining>=TollEntry.MinimumChangeMetres;
        if(!onRoad&&Math.Abs((reverse?1:0)-factor)*length<3&&remaining>=TollEntry.MinimumChangeMetres&&speed>=0) {
            var prefix=new PlannedPathData{HasError=false,PlannedPath=data.PlannedPath.Take(index).ToList()};
            if(prefix.PlannedPath.Count>0) {
                var trace=RouteMatcher.Sample(prefix);var projection=BarrierBinding.UniqueCurrent(trace,truck,trace.Length);
                double braking=Math.Max(0,(speed*speed-TollEntry.ShortChangeSpeed*TollEntry.ShortChangeSpeed)/2.4)+speed*1.5;
                allowed=trace.IdentityKnown&&projection!=null&&trace.Length-projection.Along>=Math.Max(5,braking);
            }
        }
        if(!allowed){error="Kurzer vorhandener Spurwechsel ist noch nicht langsam und erreichbar genug.";return false;}
        slow.Add(index);return true;
    }

    static bool DirectConnection(PlannedItem before,PlannedItem next)
    {
        bool Endpoint(PlannedItem item,bool start,out Vector3 point,out Vector3 heading)
        {
            point=heading=default;
            if(item.prefabPath is { } path) {
                var a=path.Interpolate(start?.00001f:.99999f,0)?.Position;
                var b=path.Interpolate(start?.002f:.998f,0)?.Position;
                if(a is not Vector3 p||b is not Vector3 q)return false;
                point=p;heading=start?q-p:p-q;
            } else if(item.item is ParsedRoad or ParsedRoadList) {
                int lane=!start&&item.targetLaneIndex!=0&&item.allowedToLaneChange?item.targetLaneIndex:item.laneIndex;
                var side=item.laneIndex<0?Side.Left:Side.Right;
                int count=item.item is ParsedRoadList group?group.GetLaneCount(side):((ParsedRoad)item.item).GetLaneCount(side);
                if(lane==0||Math.Sign(lane)!=Math.Sign(item.laneIndex)||Math.Abs((long)lane)>count)return false;
                bool reverse=item.direction==Direction.Backward;float f=start?(reverse?1:0):(reverse?0:1);
                Vector3 At(float t)=>item.item is ParsedRoadList roads?roads.InterpolateLane(t,side,Math.Abs(lane)-1).Position:
                    ((ParsedRoad)item.item).InterpolateLane(t,side,Math.Abs(lane)-1).Position;
                point=At(f);var q=At(f+(start?1:-1)*(reverse?-.002f:.002f));heading=start?q-point:point-q;
            } else return false;
            return float.IsFinite(point.LengthSquared())&&float.IsFinite(heading.LengthSquared())&&heading.LengthSquared()>.000001;
        }
        return before.endNode.Uid==next.startNode.Uid&&Endpoint(before,false,out var a,out var ah)&&Endpoint(next,true,out var b,out var bh)&&
            Vector3.Distance(a,b)<=1.5&&Vector3.Dot(Vector3.Normalize(ah),Vector3.Normalize(bh))>=.8;
    }

    static bool RoadToPrefab(PlannedPathData data,int index,Vector3 truck,double speed,List<int>? slow,out string? error)
    {
        error=null;var road=data.PlannedPath[index];var next=data.PlannedPath[index+1];var current=next.prefabPath!;
        var ends=road.item is ParsedRoadList list?(list.StartNode,list.EndNode):(((ParsedRoad)road.item).StartNode,((ParsedRoad)road.item).EndNode);
        bool backward=road.direction==Direction.Backward;float end=backward?0:1;
        double length=road.GetLength();var side=road.laneIndex<0?Side.Left:Side.Right;
        int count=road.item is ParsedRoadList roads?roads.GetLaneCount(side):((ParsedRoad)road.item).GetLaneCount(side);
        if(road.targetLaneIndex!=0&&!road.allowedToLaneChange){error="Gespeichertes Anschlussziel besitzt keine aktuelle Spurwechselberechtigung.";return false;}
        int effective=road.targetLaneIndex==0?road.laneIndex:road.targetLaneIndex;
        if(road.laneIndex==0||!double.IsFinite(length)||length<=0||Math.Abs((long)road.laneIndex)>count||
            Math.Sign(effective)!=Math.Sign(road.laneIndex)||Math.Abs((long)effective)>count||
            (backward?ends.Item1:ends.Item2).Uid!=next.startNode.Uid) {
            error="Gerichtete Endspur der Anschlussstraße ist unklar.";return false;
        }
        Vector3 Point(float f,int lane)=>road.item is ParsedRoadList group?group.InterpolateLane(f,side,lane).Position:
            ((ParsedRoad)road.item).InterpolateLane(f,side,lane).Position;
        float delta=(float)Math.Min(.01,1/length)*(backward?-1:1);
        bool Joins(PrefabPath path,int lane)
        {
            if(path.StartNode.Uid!=next.startNode.Uid||path.EndNode.Uid!=next.endNode.Uid||
                path.Prefab.Prefab?.Uid!=current.Prefab.Prefab?.Uid||
                !ReferenceEquals(path.Prefab.Descriptor,current.Prefab.Descriptor))return false;
            var a=path.Interpolate(.00001f,0)?.Position;var b=path.Interpolate(.002f,0)?.Position;
            var p=Point(end,lane);var heading=p-Point(end-delta,lane);
            return a is Vector3 start&&b is Vector3 forward&&Vector3.Distance(p,start)<=1.5&&
                Vector3.Dot(Vector3.Normalize(heading),Vector3.Normalize(forward-start))>=.8;
        }
        bool Truck(PrefabPath path)=>PrefabRefinement.OriginalFor(path).Curves is {Count:>0} curves&&curves.All(c=>
            (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles)&&c.TrafficRule.ToString()!="escort_only");
        if(Truck(current)&&Joins(current,Math.Abs(effective)-1))return true;
        float occupiedFactor=current.GetFactorForPoint(truck);var occupied=current.Interpolate(occupiedFactor,0)?.Position;
        if(occupied is Vector3 at&&occupiedFactor*current.Length>2&&Vector3.Distance(at,truck)<3) {
            error="Folgeverbindung wird bereits befahren; keine seitliche Routenänderung.";return false;
        }
        bool Directed(PrefabPath path) {
            var original=PrefabRefinement.OriginalFor(path);var all=original.Prefab.Descriptor?.NavCurves;
            if(all==null)return false;
            for(int i=0;i<original.Curves.Count;i++) {
                var curve=original.Curves[i];if(all.IndexOf(curve)<0||curve.SemaphoreId>=0)return false;
                if(i+1<original.Curves.Count&&(!curve.NextLines.Contains(all.IndexOf(original.Curves[i+1]))||
                    Vector3.Distance(curve.EndPosition,original.Curves[i+1].StartPosition)>1.5))return false;
            }
            return true;
        }
        var originalCurrent=PrefabRefinement.OriginalFor(current);
        var rules=originalCurrent.Curves.Select(c=>c.TrafficRule.ToString()).Distinct().Order().ToArray();
        var alternatives=(next.otherPrefabPaths??[]).Where(p=>originalCurrent.Curves.All(c=>c.SemaphoreId<0)&&
                Truck(p)&&Directed(p)&&PrefabRefinement.OriginalFor(p).Curves.Select(c=>c.TrafficRule.ToString()).Distinct().Order().SequenceEqual(rules)&&
                Joins(p,Math.Abs(effective)-1))
            .DistinctBy(p=>{var original=PrefabRefinement.OriginalFor(p);return(original.CurveDirection,string.Join(',',original.Curves.Select(c=>original.Prefab.Descriptor!.NavCurves.IndexOf(c))));}).Take(2).ToArray();
        if(alternatives.Length==1) {
            next.prefabPath=PrefabRefinement.Refine(alternatives[0]);next.otherPrefabPaths=[next.prefabPath];next.allowedToLaneChange=false;return true;
        }
        if(alternatives.Length>1){error="Mehrdeutige Folgeverbindung hinter der Maut.";return false;}
        var lanes=Enumerable.Range(0,count).Where(l=>Truck(current)&&Joins(current,l)).ToArray();
        if(lanes.Length!=1){error="Keine eindeutige physische Endspur zum Folgeabschnitt.";return false;}
        int wanted=Math.Sign(road.laneIndex)*(lanes[0]+1);
        if(!road.allowedToLaneChange||road.targetLaneIndex!=0||Math.Abs(wanted-road.laneIndex)!=1) {
            error="Folgeabschnitt erfordert einen nicht verfügbaren Spurwechsel.";return false;
        }
        float factor=road.GetFactorFor(truck);
        if(!float.IsFinite(factor)){error="Aktuelle Position vor dem Folgewechsel ist unklar.";return false;}
        bool onRoad=Vector3.Distance(Point(factor,Math.Abs(road.laneIndex)-1),truck)<=3;
        double remaining=Math.Abs(end-factor)*length;double beforeRoad=0;
        if(!onRoad) {
            try {
                var prefix=new PlannedPathData{HasError=false,PlannedPath=data.PlannedPath.Take(index).ToList()};
                var trace=RouteMatcher.Sample(prefix);var projection=BarrierBinding.UniqueCurrent(trace,truck,trace.Length);
                if(!trace.IdentityKnown||projection==null||trace.Length-projection.Along<5)throw new InvalidOperationException();
                beforeRoad=trace.Length-projection.Along;remaining=length;
            }catch(InvalidOperationException){error="Künftige Anschlussstraße ist nicht vor dem Lkw bewiesen.";return false;}
            catch(ArgumentException){error="Künftige Anschlussstraße ist nicht eindeutig verfolgbar.";return false;}
        }
        if(remaining<TollEntry.FullChangeMetres) {
            double braking=double.IsFinite(speed)&&speed>=0?
                Math.Max(0,(speed*speed-TollEntry.ShortChangeSpeed*TollEntry.ShortChangeSpeed)/2.4)+speed*1.5:double.PositiveInfinity;
            bool shortAllowed=slow!=null&&double.IsFinite(speed)&&speed>=0&&remaining>=TollEntry.MinimumChangeMetres&&
                (onRoad?speed<=TollEntry.ShortChangeSpeed:
                    Math.Abs((backward?1:0)-factor)*length<3&&beforeRoad>=Math.Max(5,braking));
            if(!shortAllowed){error="Zu wenig nachgewiesene Straßenlänge für den Folge-Spurwechsel; geplanter Halt erforderlich.";return false;}
            slow!.Add(index);
        }
        road.targetLaneIndex=wanted;return true;
    }

    static bool AlignImmediate(PlannedPathData data,int index,Vector3 truck,out string? error)
    {
        error=null;
        if(index+1>=data.PlannedPath.Count)return true; // Host has not extended the plan yet.
        var toll=data.PlannedPath[index];var road=data.PlannedPath[index+1];
        if(toll.prefabPath==null)return true;
        if(toll.endNode.Uid!=road.startNode.Uid) {error="Mautausfahrt und Anschlussstraße haben keinen gemeinsamen Routenknoten.";return false;}
        var exit=toll.prefabPath.Interpolate(.99999f,0)?.Position;
        var before=toll.prefabPath.Interpolate(.999f,0)?.Position;
        if (road.prefabPath is { } connector)
        {
            if (exit.HasValue && before.HasValue && AlignConnector(road,connector,exit.Value,exit.Value-before.Value,truck))
                return true;
            error="Kein eindeutiger Anschluss am Verbindungsstück hinter der Mautstelle.";
            return false;
        }
        if(road.item is not (ParsedRoad or ParsedRoadList))return true;
        if(!exit.HasValue||!before.HasValue||road.laneIndex==0||!float.IsFinite(road.GetLength())||road.GetLength()<=0) {error="Mautausfahrt oder Fahrtrichtung der Anschlussstraße ist unklar.";return false;}
        bool backward=road.direction==Direction.Backward;
        float entry=backward?1:0;
        var side=road.laneIndex<0?Side.Left:Side.Right;
        int count=road.item is ParsedRoadList list?list.GetLaneCount(side):((ParsedRoad)road.item).GetLaneCount(side);
        if(Math.Abs((long)road.laneIndex)>count) {error="Ungültige Anschluss-Spur hinter dem Mauttor.";return false;}
        Vector3 Point(float t,int lane)=>road.item is ParsedRoadList roads?roads.InterpolateLane(t,side,lane,0).Position:
            ((ParsedRoad)road.item).InterpolateLane(t,side,lane,0).Position;
        var heading=Vector3.Normalize(exit.Value-before.Value);
        if(!float.IsFinite(heading.LengthSquared())) {error="Richtung der Mautausfahrt ist unklar.";return false;}
        var candidates=new List<(int lane,float gap)>();
        for(int lane=0;lane<count;lane++) {
            var p=Point(entry,lane);
            float step=Math.Min(.01f,1/Math.Max(1,road.GetLength()));
            var next=Point(entry+(backward?-step:step),lane);
            var forward=Vector3.Normalize(next-p);
            float alignment=Vector3.Dot(heading,forward);
            if(!float.IsFinite(alignment)||alignment<.8f)continue;
            float gap=Vector3.Distance(exit.Value,p);
            if(float.IsFinite(gap))candidates.Add((Math.Sign(road.laneIndex)*(lane+1),gap));
        }
        var ranked=candidates.OrderBy(c=>c.gap).ToArray();
        if(ranked.Length==0 || ranked[0].gap>1.5f || (ranked.Length>1&&ranked[1].gap-ranked[0].gap<.75f)) {
            error="Keine eindeutig anschließende Fahrspur hinter dem Mauttor.";return false;
        }
        int chosen=ranked[0].lane;
        if(chosen==road.laneIndex)return true;
        float factor=road.GetFactorFor(truck);
        if(!float.IsFinite(factor)) {error="Lkw-Position an der Mautausfahrt ist unklar.";return false;}
        float travel=(backward?1-factor:factor)*road.GetLength();
        var oldPoint=Point(factor,Math.Abs(road.laneIndex)-1);
        if(travel>2 && Vector3.Distance(truck,oldPoint)<3) {
            error="Anschluss-Spurwechsel zu spät; bitte manuell übernehmen.";return false;
        }
        // A target lane is not an entry lane. Start on the physically connected
        // lane; retain a valid later target, with fresh lane-change state.
        int target=road.targetLaneIndex;
        if(target==chosen || Math.Sign(target)!=Math.Sign(chosen) || Math.Abs((long)target)>count)target=0;
        // Pathfinding publishes a new list but shares its PlannedItem objects.
        // Replacing only the event's list entry would leave the host route wrong.
        // Equal current/target lanes reset the host's lane-change state through
        // its public API, before restoring a valid future target.
        road.laneIndex=chosen;
        road.targetLaneIndex=chosen;
        road.GenerateLaneChange(entry,false);
        road.targetLaneIndex=target;
        return true;
    }

    static bool AlignConnector(PlannedItem item, PrefabPath current, Vector3 exit, Vector3 heading, Vector3 truck)
    {
        double Gap(PrefabPath path)
        {
            if (path.StartNode.Uid != item.startNode.Uid || path.EndNode.Uid != item.endNode.Uid)
                return double.PositiveInfinity;
            var entry=path.Interpolate(.00001f,0)?.Position;
            var ahead=path.Interpolate(.01f,0)?.Position;
            if (entry is not { } start || ahead is not { } next ||
                !(Vector3.Dot(Vector3.Normalize(next-start),Vector3.Normalize(heading)) >= .8f))
                return double.PositiveInfinity;
            return Vector3.Distance(start,exit);
        }

        bool TruckAllowed(PrefabPath path) => path.Curves.Count>0 && path.Curves.All(c=>
            (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) &&
            c.TrafficRule.ToString()!="escort_only");
        if (TruckAllowed(current) && Gap(current) <= 1.5) return true;
        float factor = current.GetFactorForPoint(truck);
        var occupied = current.Interpolate(factor,0)?.Position;
        if (!float.IsFinite(factor) || (factor*current.Length > 2 && occupied is { } point && Vector3.Distance(truck,point) < 3) ||
            current.Curves.Any(c => c.SemaphoreId >= 0))
            return false;

        var candidates=(item.otherPrefabPaths ?? []).Where(path=>TruckAllowed(path) && path.Curves.All(c=>c.SemaphoreId<0))
            .Select(path=>(Path:PrefabRefinement.Refine(path),Gap:Gap(path))).Where(option=>option.Gap<=1.5)
            .DistinctBy(option=>option.Path).OrderBy(option=>option.Gap).Take(2).ToArray();
        if (candidates.Length==0 || (candidates.Length==2 && candidates[1].Gap-candidates[0].Gap<.75)) return false;
        item.prefabPath=candidates[0].Path;
        item.otherPrefabPaths=[item.prefabPath];
        item.allowedToLaneChange=false;
        return true;
    }
}
