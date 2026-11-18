using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;
using W=MautAssistent.StaticRoadWorkerProtocol;

// No ETS2LA game/provider/output assembly is referenced by this process.
if(args.Length!=1||!Guid.TryParseExact(args[0],"N",out _))return 2;
string session=args[0];
using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
Console.SetOut(Console.Error); // Third-party diagnostics cannot corrupt JSONL.
IFileSystem identity=new Marker();
StaticRoadSceneSource.MapSelection? selection=null;
string[] archives=[];long generation=-1,lastId=0;
var source=new StaticRoadSceneSource(()=>identity,()=>selection,_=>archives);
try {
    for(;;) {
        var frame=await W.ReadFrame(input,W.MaxRequestBytes,CancellationToken.None);
        if(frame==null)break;
        var q=W.Decode<W.Query>(frame,W.MaxRequestBytes);
        var points=W.Validate(q,session);
        if(points==null||q.Id<=lastId)return 3;
        lastId=q.Id;
        if(q.Generation!=generation||q.Selection!=selection||!archives.SequenceEqual(q.Archives,StringComparer.OrdinalIgnoreCase)) {
            source.Reset();identity=new Marker();selection=q.Selection;archives=q.Archives.ToArray();generation=q.Generation;
        }
        var position=new Vector3(q.X,q.Y,q.Z);
        var snapshot=source.Read(position);
        var corridor=StaticRoadCorridor.CreateFromSlice(points,q.CurrentAlong);
        RigRoadPrediction? rig=null;string? rigIssue=null;
        if(q.Rig is { } rigInput) {
            var tail=W.RigTail(q);
            if(tail!=null)rig=RigRoadPrediction.Build(rigInput.Observation,points,tail,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),out rigIssue);
            else rigIssue="Gespann-Straßenbezug fehlt";
        }
        var hits=new List<W.Hit>();bool incomplete=true;
        var gates=new List<MapGateCandidate>();
        string map=snapshot?.MapName??"unknown",issue=source.Status;
        if(snapshot!=null&&corridor!=null) {
            foreach(var gate in snapshot.Gates)if(MapGateApproach.Bind(gate,points,q.CurrentAlong) is { } bound)gates.Add(bound);
            int remaining=60000;incomplete=snapshot.UnknownCount>0||snapshot.Issue!=null;
            foreach(var body in snapshot.Bodies.OrderBy(b=>Vector3.DistanceSquared(Vector3.Clamp(position,b.Minimum,b.Maximum),position))) {
                bool nominalOverlap=Overlap(corridor.Minimum,corridor.Maximum,body.Minimum,body.Maximum);
                bool rigOverlap=rig!=null&&Overlap(rig.Minimum,rig.Maximum,body.Minimum,body.Maximum);
                if(!nominalOverlap&&!rigOverlap)continue;
                if(remaining<=0){incomplete=true;break;}
                W.Hit? hit=null;
                if(nominalOverlap) {
                    var result=body.Geometry.CheckNominal(corridor,Math.Min(30000,remaining));remaining-=result.FaceChecks;
                    if(result.Issue!=null)incomplete=true;
                    else if(!result.Clear&&result.ConflictAlong is double along&&double.IsFinite(along))hit=new(body.Uid,along);
                }
                if(rigOverlap&&remaining>0) {
                    var result=rig!.Check(body.Geometry,Math.Min(30000,remaining));remaining-=result.FaceChecks;
                    if(result.Issue!=null)incomplete=true;
                    else if(!result.Clear&&result.ConflictAlong is double along&&double.IsFinite(along)&&(hit==null||along<hit.Along))
                        hit=new(body.Uid,along){Rig=true};
                }else if(rigOverlap)incomplete=true;
                if(hit!=null)hits.Add(hit);
            }
            if(!source.IsCurrent(snapshot,position)){hits.Clear();gates.Clear();incomplete=true;issue="Source changed during query";}
            else issue=incomplete?"Partial static evidence only":"Static evidence only; no scene-clearance claim";
        }
        if(rigIssue!=null){incomplete=true;issue+="; "+rigIssue;}
        if(q.Rig!=null&&q.Rig.Observation?.Valid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!=true) {
            hits.RemoveAll(h=>h.Rig);incomplete=true;issue+="; Gespannmessung während der Prüfung veraltet";
        }
        var reply=new W.Reply(W.Version,session,q.Id,q.Generation,q.RouteId,q.Epoch,q.Timestamp,
            map,hits.Take(32).ToArray(),incomplete||hits.Count>32||gates.Count>8,issue.Length>512?issue[..512]:issue)
            {Gates=gates.OrderBy(g=>g.PlaneAlong).Take(8).ToArray()};
        await W.WriteFrame(output,W.Encode(reply,W.MaxResponseBytes),CancellationToken.None);
    }
    return 0;
}catch(Exception e){Console.Error.WriteLine("Static worker stopped: "+e.GetType().Name);return 4;}
finally{source.Reset();}

static bool Overlap(Vector3 lo,Vector3 hi,Vector3 b,Vector3 e)=>lo.X<=e.X&&hi.X>=b.X&&lo.Y<=e.Y&&hi.Y>=b.Y&&lo.Z<=e.Z&&hi.Z>=b.Z;
sealed class Marker:IFileSystem
{
    // Identity only. Every file operation belongs to IndependentAssets.
    public char DirectorySeparator=>'/';
    public bool FileExists(string p)=>throw new NotSupportedException();
    public bool DirectoryExists(string p)=>throw new NotSupportedException();
    public IList<string> GetFiles(string p)=>throw new NotSupportedException();
    public byte[] ReadAllBytes(string p)=>throw new NotSupportedException();
    public string ReadAllText(string p)=>throw new NotSupportedException();
    public string ReadAllText(string p,Encoding e)=>throw new NotSupportedException();
    public Stream Open(string p)=>throw new NotSupportedException();
    public string GetParent(string p)=>throw new NotSupportedException();
}
