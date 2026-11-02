namespace MautAssistent;

public sealed record CurveLink(int Id, int[] Next, double Length, bool TruckAllowed, int GateId);
public static class CurveGraph
{
    // Search includes whether the requested gate was crossed. No path through another
    // gate or a car-only curve is ever accepted as a fallback.
    public static int[]? ViaGate(IReadOnlyList<CurveLink> curves, int start, int end, int gate)
    {
        var byId=curves.ToDictionary(c=>c.Id);
        if(!byId.ContainsKey(start)||!byId.ContainsKey(end)) return null;
        var queue=new PriorityQueue<(int id,bool crossed),double>();
        var costs=new Dictionary<(int,bool),double>();
        var previous=new Dictionary<(int,bool),(int,bool)>();
        var first=(start,byId[start].GateId==gate);
        costs[first]=0; queue.Enqueue(first,0);
        while(queue.TryDequeue(out var current,out var cost))
        {
            if(cost!=costs[current]) continue;
            var c=byId[current.id];
            if(!c.TruckAllowed || (c.GateId>=0 && c.GateId!=gate)) continue;
            if(current.id==end && current.crossed)
            {
                var result=new List<int>{current.id};
                while(previous.TryGetValue(current,out var p)) {current=p;result.Add(current.id);}
                result.Reverse();return result.ToArray();
            }
            foreach(var next in c.Next)
            {
                if(!byId.TryGetValue(next,out var n)||!double.IsFinite(n.Length)||n.Length<=0) continue;
                var key=(next,current.crossed||n.GateId==gate);
                double nc=cost+n.Length;
                if(costs.TryGetValue(key,out var old)&&old<=nc) continue;
                costs[key]=nc;previous[key]=current;queue.Enqueue(key,nc);
            }
        }
        return null;
    }
}
