using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MautAssistent;

// A bounded, read-only process boundary. No gameplay command is representable.
internal static class StaticRoadWorkerProtocol
{
    internal const int Version=1,MaxRequestBytes=131072,MaxResponseBytes=8192;
    internal const double AuthoritySeconds=.2;
    internal sealed record Point(float X,float Y,float Z,float Dx,float Dy,float Dz,double Along,int Segment,bool Connector);
    internal sealed record Query(int Version,string Session,long Id,long Generation,long RouteId,long Epoch,long Timestamp,
        StaticRoadSceneSource.MapSelection Selection,string[] Archives,float X,float Y,float Z,double CurrentAlong,
        string[] Segments,Point[] Points);
    internal sealed record Hit(ulong Uid,double Along);
    internal sealed record Reply(int Version,string Session,long Id,long Generation,long RouteId,long Epoch,long Timestamp,
        string MapName,Hit[] Hits,bool Incomplete,string? Issue);
    internal static bool Fresh(long timestamp,long now)=>timestamp>0&&timestamp<=now&&
        (now-timestamp)/(double)Stopwatch.Frequency<=AuthoritySeconds;
    internal static byte[] Encode<T>(T value,int max)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        if(bytes.Length==0||bytes.Length>max)throw new InvalidDataException("Worker frame budget exceeded");
        return bytes;
    }
    internal static T Decode<T>(byte[] bytes,int max)
    {
        if(bytes.Length==0||bytes.Length>max)throw new InvalidDataException("Worker frame budget exceeded");
        return JsonSerializer.Deserialize<T>(bytes)??throw new InvalidDataException("Empty worker frame");
    }
    // Unlike ReadLineAsync, allocation is bounded even for a malformed child.
    internal static async Task<byte[]?> ReadFrame(Stream stream,int max,CancellationToken token)
    {
        var bytes=new byte[max+1];int used=0;
        // There is exactly one outstanding request. Pipelined extra frames are
        // rejected, so bounded chunks need no unbounded shared line buffer.
        while(used<=max) {
            int n=await stream.ReadAsync(bytes.AsMemory(used,Math.Min(4096,max+1-used)),token).ConfigureAwait(false);
            if(n==0) {if(used==0)return null;throw new InvalidDataException("Truncated worker frame");}
            int end=Array.IndexOf(bytes,(byte)'\n',used,n);
            if(end>=0) {
                if(end!=used+n-1)throw new InvalidDataException("Pipelined worker frames");
                return bytes.AsSpan(0,end).ToArray();
            }
            used+=n;
        }
        throw new InvalidDataException("Worker frame too large");
    }
    internal static async Task WriteFrame(Stream stream,byte[] bytes,CancellationToken token)
    {
        await stream.WriteAsync(bytes,token).ConfigureAwait(false);
        await stream.WriteAsync(new byte[]{10},token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static IReadOnlyList<TraceSlicePoint>? Validate(Query q,string session)
    {
        if(q.Version!=Version||q.Session!=session||!Guid.TryParseExact(session,"N",out _)||
            q.Id<=0||q.Generation<=0||q.RouteId<=0||q.Epoch<0||q.Timestamp<=0||
            q.Selection==null||!MapName(q.Selection.BaseMapName)||
            (q.Selection.ExplicitMapName!=null&&!MapName(q.Selection.ExplicitMapName))||
            q.Archives==null||q.Archives.Length is <1 or >256||q.Archives.Any(p=>
                string.IsNullOrEmpty(p)||p.Length>1024||!Path.IsPathFullyQualified(p)||p.Any(char.IsControl))||
            q.Archives.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=q.Archives.Length||
            q.Points==null||q.Points.Length is <2 or >1000||q.Segments==null||q.Segments.Length is <1 or >64||
            q.Segments.Any(s=>string.IsNullOrEmpty(s)||s.Length>512)||
            !Finite(new(q.X,q.Y,q.Z))||!double.IsFinite(q.CurrentAlong)||q.CurrentAlong<0)return null;
        var points=new TraceSlicePoint[q.Points.Length];
        for(int i=0;i<points.Length;i++) {
            var p=q.Points[i];
            if(p==null||p.Segment<0||p.Segment>=q.Segments.Length)return null;
            points[i]=new(new(p.X,p.Y,p.Z),new(p.Dx,p.Dy,p.Dz),p.Along,q.Segments[p.Segment],p.Connector);
        }
        // Full geometric validation is done by CreateFromSlice in the helper.
        return points;
    }
    internal static bool ValidReply(Reply r,Query q,long now)
    {
        if(r.Version!=Version||r.Session!=q.Session||r.Id!=q.Id||r.Generation!=q.Generation||
            r.RouteId!=q.RouteId||r.Epoch!=q.Epoch||r.Timestamp!=q.Timestamp||!Fresh(q.Timestamp,now)||
            !MapName(r.MapName)||r.Hits==null||r.Hits.Length>64||r.Issue?.Length>512)return false;
        string? expected=q.Selection.ExplicitMapName??(q.Selection.ForceBaseMapName?q.Selection.BaseMapName:null);
        if(expected!=null&&r.MapName!=expected&&(r.MapName!="unknown"||r.Hits.Length!=0))return false;
        var ids=new HashSet<ulong>();
        return r.Hits.All(h=>h!=null&&h.Uid!=0&&ids.Add(h.Uid)&&double.IsFinite(h.Along)&&
            h.Along>=q.CurrentAlong&&h.Along<=q.Points[^1].Along);
    }
    internal static bool MapName(string? value)=>value is {Length:>0 and <=64}&&
        value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
    static bool Finite(Vector3 p)=>SceneSnapshotProtocol.Finite(p)&&Math.Abs(p.X)<=262144&&Math.Abs(p.Y)<=10000&&Math.Abs(p.Z)<=262144;
}
