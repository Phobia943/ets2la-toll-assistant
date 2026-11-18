using System.Diagnostics;
using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;
using W=MautAssistent.StaticRoadWorkerProtocol;

internal static class StaticRoadWorkerReviewTests
{
    internal static W.Query Query(string? session=null,long id=1)
    {
        var points=Enumerable.Range(0,21).Select(i=>new W.Point(0,0,i*.5f,0,0,1,i*.5,0,false)).ToArray();
        return new(W.Version,session??Guid.NewGuid().ToString("N"),id,1,1,0,Stopwatch.GetTimestamp(),
            new("europe",true),[Path.GetFullPath("absent-scene-fixture.scs")],0,0,0,0,["fixture-road"],points);
    }
    internal static void FramesAndStamps(Action<bool,string> check)
    {
        var q=Query();long now=q.Timestamp;
        var valid=new W.Reply(W.Version,q.Session,q.Id,q.Generation,q.RouteId,q.Epoch,q.Timestamp,"europe",[new(7,5)],false,null);
        check(W.ValidReply(valid,q,now),"Bounded positive hit with exact original stamps is accepted");
        var bad=new[]{valid with{Version=9},valid with{Session=Guid.NewGuid().ToString("N")},valid with{Id=2},
            valid with{Generation=2},valid with{RouteId=2},valid with{Epoch=1},valid with{Timestamp=now+1},
            valid with{Hits=[new(7,double.NaN)]},valid with{Hits=[new(7,double.PositiveInfinity)]},
            valid with{Hits=[new(7,-1)]},valid with{Hits=[new(7,11)]},valid with{Hits=[new(7,5),new(7,6)]},
            valid with{Hits=Enumerable.Range(1,65).Select(i=>new W.Hit((ulong)i,5)).ToArray()},
            valid with{Hits=[new(0,5)]},valid with{MapName="../other"},valid with{MapName="usa"}};
        foreach(var r in bad)check(!W.ValidReply(r,q,now),"Malformed/stale worker result cannot acquire authority");
        check(!W.ValidReply(valid,q,now+(long)(.201*Stopwatch.Frequency)),"200 ms authority starts at original request, not reply receive");
        check(!W.ValidReply(valid,q,now-1),"Future original timestamp is rejected");
        check(W.Validate(q,q.Session)!=null,"Valid bounded query survives validation");
        check(W.Validate(q with{Archives=["relative.scs"]},q.Session)==null,"Relative archive cannot escape expected source");
        check(W.Validate(q with{Points=[q.Points[0] with{Segment=8},q.Points[1]]},q.Session)==null,"Unknown segment index rejected");
        check(W.Validate(q with{X=float.NaN},q.Session)==null,"Non-finite origin rejected");
        check(W.Validate(q with{Segments=new string[65]},q.Session)==null,"Identity count bounded");
        check(StaticRoadCorridor.CreateFromSlice(W.Validate(q,q.Session)!,0)!=null,"The actual sampled corridor is accepted");
        var malformed=q with{Points=[q.Points[0],q.Points[1] with{Along=-1}]};
        check(StaticRoadCorridor.CreateFromSlice(W.Validate(malformed,q.Session)!,0)==null,"Malformed original progress cannot become a stop distance");
        byte[] raw=W.Encode(valid,W.MaxResponseBytes);
        check(W.Decode<W.Reply>(raw,W.MaxResponseBytes).Hits.Length==1,"Bounded JSON reply roundtrip");
        Throws(()=>W.Decode<W.Reply>(new byte[W.MaxResponseBytes+1],W.MaxResponseBytes),"Oversized JSON rejected");
        Throws(()=>W.ReadFrame(new MemoryStream(Encoding.UTF8.GetBytes("{\"x\":1}")),64,CancellationToken.None).GetAwaiter().GetResult(),"Truncated line rejected");
        Throws(()=>W.ReadFrame(new MemoryStream(Encoding.UTF8.GetBytes(new string('a',65)+"\n")),64,CancellationToken.None).GetAwaiter().GetResult(),"Oversized line rejected before unbounded allocation");
        Throws(()=>W.ReadFrame(new MemoryStream(Encoding.UTF8.GetBytes("{}\n{}\n")),64,CancellationToken.None).GetAwaiter().GetResult(),"Pipelined/unowned frame rejected");
        void Throws(Action action,string message){bool threw=false;try{action();}catch{threw=true;}check(threw,message);}
    }
    internal static void ThreadFailure(Action<bool,string> check)
    {
        int scheduled=0;var route=new RouteTrace([Vector3.Zero,new(0,0,50)],null,["fixture-road"]);
        using var client=new StaticRoadWorkerClient(()=>new Marker(),()=>new("europe",true),
            paths:_=>[Path.GetFullPath("missing.scs")],schedule:_=>{scheduled++;throw new IOException("Thread start refused");});
        check(client.Read(route,Vector3.Zero,40,1)==null,"Worker thread start failure becomes unknown instead of escaping the driving tick");
        check(client.Read(route,Vector3.Zero,40,1)==null&&scheduled==1,"Thread start failures have a bounded retry delay");
        client.Dispose();
        check(client.Read(route,Vector3.Zero,40,1)==null&&scheduled==1,"Disposed client schedules no work");
    }
    sealed class Marker:IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>throw new InvalidOperationException();
        public bool DirectoryExists(string p)=>throw new InvalidOperationException();public IList<string> GetFiles(string p)=>throw new InvalidOperationException();
        public byte[] ReadAllBytes(string p)=>throw new InvalidOperationException();public string ReadAllText(string p)=>throw new InvalidOperationException();
        public string ReadAllText(string p,Encoding e)=>throw new InvalidOperationException();public Stream Open(string p)=>throw new InvalidOperationException();
        public string GetParent(string p)=>throw new InvalidOperationException();
    }
}
