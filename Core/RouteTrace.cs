using System.Numerics;

namespace MautAssistent;
public sealed record TraceProjection(double Along, double Offset, int SegmentEndIndex=0,double HeightOffset=0);
public readonly record struct TraceSlicePoint(Vector3 Position,Vector3 Direction,double Along,string? SegmentIdentity,bool Connector);
public sealed class RouteTrace
{
    readonly Vector3[] points;
    readonly double[] distances;
    readonly HashSet<int> connectorEnds;
    readonly string[]? segmentKeys;
    public double Length => distances[^1];
    public Vector3 End=>points[^1];
    public bool EndsAtGap { get; init; }
    public bool IdentityKnown { get; init; }
    public string? EndReason { get; init; }
    public RouteTrace(IEnumerable<Vector3> source,IEnumerable<int>? connectorEndIndexes=null)
        :this(source,connectorEndIndexes,null) { }
    public RouteTrace(IEnumerable<Vector3> source,IEnumerable<int>? connectorEndIndexes,IEnumerable<string>? segmentIdentities)
    {
        points=source.ToArray(); distances=new double[points.Length];
        segmentKeys=segmentIdentities?.ToArray();
        IdentityKnown=segmentKeys!=null;
        connectorEnds=connectorEndIndexes?.ToHashSet()??[];
        if(points.Length<2 || points.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)))
            throw new ArgumentException("Ungültige Routenpunkte");
        if(connectorEnds.Any(i=>i<1||i>=points.Length||Vector3.Distance(points[i-1],points[i])>2))
            throw new ArgumentException("Ungültige Abschnittsverbindung");
        if(segmentKeys!=null&&(segmentKeys.Length!=points.Length-1||segmentKeys.Any(string.IsNullOrEmpty)))
            throw new ArgumentException("Ungültige Korridorzuordnung");
        for(int i=1;i<points.Length;i++) distances[i]=distances[i-1]+Vector3.Distance(points[i-1],points[i]);
    }
    // Compare directed samples, topology and every seam through the required
    // future corridor. Pruning behind the truck is permitted; extrapolation,
    // merely similar headings, another lane/level or a branch are not.
    public bool MatchesAhead(RouteTrace other,Vector3 origin,double requiredAhead,out double ownAlong,out double otherAlong)
    {
        ownAlong=otherAlong=0;
        if(!double.IsFinite(requiredAhead)||requiredAhead<0)return false;
        var a=ProjectForTracking(origin);var b=other.ProjectForTracking(origin);
        if(a.Offset>3||b.Offset>3||Math.Abs(a.HeightOffset)>2||Math.Abs(b.HeightOffset)>2)return false;
        int i=a.SegmentEndIndex,j=b.SegmentEndIndex;
        if(i<1||j<1||(segmentKeys==null)!=(other.segmentKeys==null))return false;
        ownAlong=a.Along;otherAlong=b.Along;
        double needed=Math.Min(requiredAhead,other.Length-b.Along);
        for(;;i++,j++) {
            if(i>=points.Length||j>=other.points.Length)
                return i==points.Length&&j==other.points.Length&&EndsAtGap==other.EndsAtGap;
            if(points[i-1]!=other.points[j-1]||points[i]!=other.points[j]||
                connectorEnds.Contains(i)!=other.connectorEnds.Contains(j)||
                (segmentKeys!=null&&segmentKeys[i-1]!=other.segmentKeys![j-1]))return false;
            if(distances[i]-a.Along>=needed) {
                bool atEnd=i==points.Length-1||j==other.points.Length-1;
                return !atEnd||(i==points.Length-1&&j==other.points.Length-1&&EndsAtGap==other.EndsAtGap);
            }
        }
    }
    public TraceProjection Project(Vector3 point)
    {
        double best=double.PositiveInfinity,along=0;int segment=0;
        for(int i=1;i<points.Length;i++)
        {
            var delta=points[i]-points[i-1];
            float len=delta.LengthSquared();
            if(len<.000001)continue;
            float t=Math.Clamp(Vector3.Dot(point-points[i-1],delta)/len,0,1);
            double d=Vector3.DistanceSquared(point,points[i-1]+t*delta);
            if(d<best) {best=d;along=distances[i-1]+t*Math.Sqrt(len);segment=i;}
        }
        return new(along,Math.Sqrt(best),segment);
    }
    public TraceProjection ProjectForTracking(Vector3 point)
    {
        // Keep the 3D segment choice, including stacked road levels, and measure
        // lane departure horizontally like the native pathfinder.
        var projection=Project(point);int i=projection.SegmentEndIndex;
        if(i<1)return projection;
        double length=distances[i]-distances[i-1];
        float factor=length>0?(float)((projection.Along-distances[i-1])/length):0;
        var nearest=Vector3.Lerp(points[i-1],points[i],factor);
        double horizontal=Vector2.Distance(new(point.X,point.Z),new(nearest.X,nearest.Z));
        return projection with {Offset=horizontal,HeightOffset=point.Y-nearest.Y};
    }
    public bool TryItemRange(string identity,out double start,out double end)
    {
        start=end=0;
        if(!IdentityKnown||segmentKeys==null||string.IsNullOrEmpty(identity))return false;
        bool found=false,left=false;
        for(int i=1;i<points.Length;i++) {
            bool matches=!connectorEnds.Contains(i)&&segmentKeys[i-1]==identity;
            if(matches) {
                if(left)return false; // Repeated identity has ambiguous topology.
                if(!found){start=distances[i-1];found=true;}
                end=distances[i];
            } else if(found)left=true;
        }
        return found&&end>start;
    }
    // A body corner must stay on the directed road neighbourhood selected by
    // its root. A later crossing/upper deck is not ground below this vehicle.
    public TraceProjection ProjectNearForTracking(Vector3 point,double rootAlong,double radius)
    {
        if(!double.IsFinite(rootAlong)||!double.IsFinite(radius)||radius<=0)throw new ArgumentException("Ungültiges lokales Routenfenster");
        double best=double.PositiveInfinity,along=0;int segment=0;Vector3 nearest=default;
        double low=Math.Max(0,rootAlong-radius),high=Math.Min(Length,rootAlong+radius);
        for(int i=1;i<points.Length;i++) {
            double length=distances[i]-distances[i-1];
            if(length<.000001||distances[i]<low||distances[i-1]>high||connectorEnds.Contains(i))continue;
            var delta=points[i]-points[i-1];
            float t=Math.Clamp(Vector3.Dot(point-points[i-1],delta)/delta.LengthSquared(),
                (float)Math.Clamp((low-distances[i-1])/length,0,1),(float)Math.Clamp((high-distances[i-1])/length,0,1));
            var candidate=points[i-1]+t*delta;double distance=Vector3.DistanceSquared(point,candidate);
            if(distance<best){best=distance;nearest=candidate;along=distances[i-1]+t*length;segment=i;}
        }
        if(segment==0)throw new InvalidOperationException("Lokaler Straßenbezug fehlt");
        return new(along,Vector2.Distance(new(point.X,point.Z),new(nearest.X,nearest.Z)),segment,point.Y-nearest.Y);
    }
    // Keep a bounded known road tail under the articulated rig when the native
    // planner prunes completed items. Reuse GEOMETRY, never a cached profile or
    // route freshness: all overlapping directed points/identities must agree.
    public RouteTrace? WithVerifiedTail(RouteTrace previous,Vector3 origin,double behind=40,double ahead=20)
    {
        if(!IdentityKnown||!previous.IdentityKnown||segmentKeys==null||previous.segmentKeys==null||
            !double.IsFinite(behind)||behind<=0||behind>60||!double.IsFinite(ahead)||ahead<0)return null;
        var current=ProjectForTracking(origin);if(current.Offset>3||Math.Abs(current.HeightOffset)>.35||Length-current.Along<ahead)return null;
        for(int join=1;join<previous.points.Length-1;join++) {
            if(previous.points[join]!=points[0])continue;
            bool matched=true;int end=0;
            for(;end<points.Length&&join+end<previous.points.Length;end++) {
                if(points[end]!=previous.points[join+end]||end>0&&
                    (segmentKeys[end-1]!=previous.segmentKeys[join+end-1]||connectorEnds.Contains(end)!=previous.connectorEnds.Contains(join+end))) {matched=false;break;}
                if(distances[end]>=current.Along+ahead)break;
            }
            if(!matched||end>=points.Length||join+end>=previous.points.Length||distances[end]<current.Along+ahead)continue;
            double minimum=previous.distances[join]-behind;int start=join;
            while(start>0&&previous.distances[start-1]>=minimum)start--;
            if(start==join)return this;
            int prefix=join-start;
            var combined=previous.points[start..join].Concat(points);
            var keys=previous.segmentKeys[start..join].Concat(segmentKeys);
            var seams=previous.connectorEnds.Where(i=>i>start&&i<=join).Select(i=>i-start)
                .Concat(connectorEnds.Select(i=>i+prefix));
            return new RouteTrace(combined,seams,keys){IdentityKnown=true,EndsAtGap=EndsAtGap,EndReason=EndReason};
        }
        return null;
    }
    public Vector3 DirectionAt(double along)
    {
        Vector3 previous=Vector3.Zero;
        for(int i=1;i<points.Length;i++) {
            // The <=2 m seam between separately sampled planned items is only
            // geometric continuity. It is not a directed driving segment.
            if(connectorEnds.Contains(i))continue;
            var delta=points[i]-points[i-1];delta.Y=0;
            if(delta.LengthSquared()<.000001f)continue;
            previous=Vector3.Normalize(delta);
            if(distances[i]>=along)return previous;
        }
        return previous;
    }
    // Read-only interpolation of the final directed trace. No extrapolation.
    public bool TryAtAlong(double along,out TraceSlicePoint sample)
    {
        sample=default;
        if(!double.IsFinite(along)||along<0||along>Length)return false;
        int end=1;while(end<points.Length-1&&distances[end]<along)end++;
        while(end<points.Length-1&&distances[end]-distances[end-1]<1e-6)end++;
        double length=distances[end]-distances[end-1];if(length<1e-6)return false;
        float fraction=(float)Math.Clamp((along-distances[end-1])/length,0,1);
        Vector3 Tangent(int vertex) {
            Vector3 before=vertex>0?points[vertex]-points[vertex-1]:Vector3.Zero;
            Vector3 after=vertex<points.Length-1?points[vertex+1]-points[vertex]:Vector3.Zero;
            if(before.LengthSquared()>1e-8)before=Vector3.Normalize(before);
            if(after.LengthSquared()>1e-8)after=Vector3.Normalize(after);
            var sum=before+after;return sum.LengthSquared()>1e-8?Vector3.Normalize(sum):Vector3.Zero;
        }
        var direction=Vector3.Lerp(Tangent(end-1),Tangent(end),fraction);
        if(direction.LengthSquared()<.1)return false;
        sample=new(Vector3.Lerp(points[end-1],points[end],fraction),Vector3.Normalize(direction),along,
            segmentKeys?[end-1],connectorEnds.Contains(end));return true;
    }
    // Nominal road corridor, independent of the articulated no-slip model's
    // near-flat scope. Keep every actual vertex, directed seam and endpoint;
    // never extend a missing road or invent a gap connection.
    public bool TryStandardSlice(double from,double to,double maxStep,out IReadOnlyList<TraceSlicePoint> slice,out string? issue)
    {
        slice=[];issue=null;
        if(!IdentityKnown||segmentKeys==null||!double.IsFinite(from)||!double.IsFinite(to)||
            !double.IsFinite(maxStep)||from<0||to<=from||to>Length||to-from>300||maxStep is <.05 or >.5)
            {issue="Bounded identified standard trace is missing";return false;}
        var numericalSeams=new HashSet<int>();
        bool NumericalSeam(int i) {
            if(!connectorEnds.Contains(i)||i<2||i>=points.Length-1||connectorEnds.Contains(i-1)||connectorEnds.Contains(i+1))return false;
            var a=points[i-1]-points[i-2];var b=points[i+1]-points[i];
            return a.LengthSquared()>=1e-8&&b.LengthSquared()>=1e-8&&
                Vector3.Dot(Vector3.Normalize(a),Vector3.Normalize(b))>=.95f&&
                Vector3.Distance(points[i-1],points[i])<=StandardSeamPrecision(points[i-1],points[i]);
        }
        for(int i=1;i<points.Length;i++) {
            if(distances[i]<=from||distances[i-1]>=to)continue;
            var delta=points[i]-points[i-1];
            if(!connectorEnds.Contains(i)) {
                if(delta.LengthSquared()<1e-8){issue="Degenerate standard segment";return false;}
                continue;
            }
            if(i<2||i>=points.Length-1||connectorEnds.Contains(i-1)||connectorEnds.Contains(i+1))
                {issue="Standard seam lacks directed neighbours";return false;}
            var a=points[i-1]-points[i-2];var b=points[i+1]-points[i];
            if(a.LengthSquared()<1e-8||b.LengthSquared()<1e-8)
                {issue="Standard seam lacks nondegenerate directed neighbours";return false;}
            bool forward=delta.LengthSquared()>=1e-8&&Vector3.Dot(delta,a)>0&&Vector3.Dot(delta,b)>0;
            // RouteMatcher retains both independently interpolated item ends.
            // At large world coordinates they can coincide or differ by a few
            // representable float steps, including a small negative projection.
            // Only a marked seam with agreeing real neighbours gets this bound;
            // no road is extended and the original identities remain untouched.
            bool numerical=NumericalSeam(i);
            if(!forward&&!numerical){issue="Standard seam reverses its directed trace";return false;}
            if(numerical)numericalSeams.Add(i);
        }
        // Endpoint tangent interpolation also reads the immediately adjacent
        // segment outside the requested window. Classify ONLY those numerical
        // supports; do not sample/authorize extra geometry or reject distant
        // seams outside the actual corridor.
        foreach(int i in connectorEnds)
            if(i>=2&&i<points.Length-1&&distances[i+1]>=from&&distances[i-2]<=to&&NumericalSeam(i))numericalSeams.Add(i);
        bool StandardPoint(double along,out TraceSlicePoint sample) {
            sample=default;int end=1;while(end<points.Length-1&&distances[end]<along)end++;
            while(end<points.Length-1&&distances[end]-distances[end-1]<1e-6)end++;
            double length=distances[end]-distances[end-1];if(length<1e-6)return false;
            float factor=(float)Math.Clamp((along-distances[end-1])/length,0,1);
            Vector3 Tangent(int vertex) {
                // Geometry/progress is never altered. Only exclude the proven
                // rounding connector from orientation averaging: its tiny
                // backward vector is not a real opposite driving direction.
                var before=vertex>0&&!numericalSeams.Contains(vertex)?points[vertex]-points[vertex-1]:Vector3.Zero;
                var after=vertex<points.Length-1&&!numericalSeams.Contains(vertex+1)?points[vertex+1]-points[vertex]:Vector3.Zero;
                if(before.LengthSquared()>1e-8)before=Vector3.Normalize(before);
                if(after.LengthSquared()>1e-8)after=Vector3.Normalize(after);
                var sum=before+after;return sum.LengthSquared()>1e-8?Vector3.Normalize(sum):Vector3.Zero;
            }
            var direction=Vector3.Lerp(Tangent(end-1),Tangent(end),factor);if(direction.LengthSquared()<.1)return false;
            sample=new(Vector3.Lerp(points[end-1],points[end],factor),Vector3.Normalize(direction),along,
                segmentKeys[end-1],connectorEnds.Contains(end));return true;
        }
        var cuts=new[]{from}.Concat(distances.Where(d=>d>from+.0001&&d<to-.0001)).Append(to).Distinct().Order().ToArray();
        var result=new List<TraceSlicePoint>();
        for(int i=1;i<cuts.Length;i++) {
            int count=(int)Math.Ceiling((cuts[i]-cuts[i-1])/maxStep);
            if(count>4095-result.Count){issue="Standard trace sampling budget exhausted";return false;}
            for(int n=i==1?0:1;n<=count;n++) {
                double along=cuts[i-1]+(cuts[i]-cuts[i-1])*n/count;
                if(!StandardPoint(along,out var point)){issue="Standard tangent is ambiguous";return false;}
                result.Add(point);
            }
        }
        slice=result.AsReadOnly();return true;
    }
    static double StandardSeamPrecision(Vector3 a,Vector3 b)
    {
        static double Ulp(float value)=>Math.Max(Math.Abs((double)MathF.BitIncrement(value)-value),Math.Abs((double)value-MathF.BitDecrement(value)));
        double x=Math.Max(Ulp(a.X),Ulp(b.X)),y=Math.Max(Ulp(a.Y),Ulp(b.Y)),z=Math.Max(Ulp(a.Z),Ulp(b.Z));
        // Four float rounding steps cover the two endpoint interpolations and
        // transforms, per coordinate. Never turn coarse coordinates into a
        // physical lane-change allowance: the absolute ceiling is 25mm.
        return Math.Min(.025,4*Math.Sqrt(x*x+y*y+z*z));
    }
    public bool TrySlice(double from,double to,double maxStep,out IReadOnlyList<TraceSlicePoint> slice,out string? issue)
    {
        slice=[];issue=null;
        static bool Reject(string why,out string? error){error=why;return false;}
        if(!IdentityKnown||segmentKeys==null)return Reject("Directed trace identities are missing",out issue);
        if(!double.IsFinite(from)||!double.IsFinite(to)||!double.IsFinite(maxStep)||from<0||to<=from||to>Length||
            to-from>300||maxStep is <.05 or >.5)return Reject("Prediction horizon is absent, outside the trace or unbounded",out issue);
        for(int i=1;i<points.Length;i++) {
            if(distances[i]<=from||distances[i-1]>=to)continue;
            var delta=points[i]-points[i-1];double horizontal=Math.Sqrt((double)delta.X*delta.X+(double)delta.Z*delta.Z);
            if(horizontal<1e-6||Math.Abs(delta.Y)>horizontal*.03)return Reject("Trace is degenerate or outside near-flat scope",out issue);
            if(!connectorEnds.Contains(i))continue;
            if(i<2||i>=points.Length-1||connectorEnds.Contains(i-1)||connectorEnds.Contains(i+1))
                return Reject("Item seam lacks directed neighbours",out issue);
            var a=points[i-1]-points[i-2];var b=points[i+1]-points[i];
            if(a.LengthSquared()<1e-8||b.LengthSquared()<1e-8||
                Vector3.Dot(Vector3.Normalize(delta),Vector3.Normalize(a))<.995f||
                Vector3.Dot(Vector3.Normalize(delta),Vector3.Normalize(b))<.995f)
                return Reject("Item seam is not a continuous directed near-flat connection",out issue);
        }
        // A numerically projected start can lie micrometres before a stored
        // vertex. Do not manufacture a near-zero progress/orientation cell.
        var cuts=new[]{from}.Concat(distances.Where(d=>d>from+.0001&&d<to-.0001)).Append(to).Distinct().Order().ToArray();
        var result=new List<TraceSlicePoint>();
        for(int i=1;i<cuts.Length;i++) {
            int count=(int)Math.Ceiling((cuts[i]-cuts[i-1])/maxStep);
            if(count>4095-result.Count)return Reject("Trace sampling budget exhausted",out issue);
            for(int n=i==1?0:1;n<=count;n++) {
                double along=cuts[i-1]+(cuts[i]-cuts[i-1])*n/count;
                if(!TryAtAlong(along,out var point))return Reject("Trace tangent is ambiguous",out issue);
                result.Add(point);
            }
        }
        slice=result.AsReadOnly();return true;
    }
    // Interpolation stays inside the verified trace. Item seams are geometric
    // continuity, not evidence of a bend, so curvature windows exclude them.
    public bool TryCurvature(double center,double span,out double curvature)
    {
        curvature=0;
        if(!double.IsFinite(center)||!double.IsFinite(span)||span<=0||center-span<0||center+span>Length)return false;
        foreach(int i in connectorEnds)if(distances[i]>=center-span&&distances[i-1]<=center+span)return false;
        Vector3 At(double along) {
            int end=Array.BinarySearch(distances,along);
            if(end<0)end=~end;
            end=Math.Clamp(end,1,points.Length-1);
            while(end<points.Length-1&&distances[end]-distances[end-1]<.000001)end++;
            double length=distances[end]-distances[end-1];
            return Vector3.Lerp(points[end-1],points[end],length>0?(float)((along-distances[end-1])/length):0);
        }
        var a=At(center-span);var b=At(center);var c=At(center+span);
        double abX=(double)b.X-a.X,abZ=(double)b.Z-a.Z,bcX=(double)c.X-b.X,bcZ=(double)c.Z-b.Z;
        double ab=Math.Sqrt(abX*abX+abZ*abZ),bc=Math.Sqrt(bcX*bcX+bcZ*bcZ);
        double ac=Math.Sqrt((abX+bcX)*(abX+bcX)+(abZ+bcZ)*(abZ+bcZ));
        if(Math.Min(Math.Min(ab,bc),ac)<.25)return false;
        curvature=2*(abX*bcZ-abZ*bcX)/(ab*bc*ac);
        return double.IsFinite(curvature);
    }
    public object Diagnose(Vector3 position)
    {
        var p=Project(position);var direction=DirectionAt(p.Along);
        int first=Math.Max(0,p.SegmentEndIndex-12),last=Math.Min(points.Length-1,p.SegmentEndIndex+12);
        return new {Length,EndsAtGap,EndReason,projection=p,trackingProjection=ProjectForTracking(position),
            direction=new{x=direction.X,y=direction.Y,z=direction.Z},
            samples=Enumerable.Range(first,last-first+1).Select(i=>new{index=i,along=distances[i],
                x=points[i].X,y=points[i].Y,z=points[i].Z,connectorFromPrevious=connectorEnds.Contains(i)}).ToArray()};
    }
}
