using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text;
using MautAssistent;
using TruckLib;
using W=MautAssistent.StaticRoadWorkerProtocol;

internal static class RigRoadWorkerReviewTests
{
    internal static void Protocol(Action<bool,string> check)
    {
        var f=RigRoadPredictionReviewTests.Create();
        W.Point Point(TraceSlicePoint p)=>new(p.Position.X,p.Position.Y,p.Position.Z,p.Direction.X,p.Direction.Y,p.Direction.Z,p.Along,0,p.Connector);
        var q=new W.Query(W.Version,Guid.NewGuid().ToString("N"),1,1,1,0,Stopwatch.GetTimestamp(),new("europe",true),
            [Path.GetFullPath("fixture.scs")],0,0,0,f.Ahead[0].Along,["same-road"],f.Ahead.Select(Point).ToArray()){
                Rig=new(f.Observation,f.Tail.Select(Point).ToArray())};
        var roundtrip=W.Decode<W.Query>(W.Encode(q,W.MaxRequestBytes),W.MaxRequestBytes);
        check(roundtrip.Rig?.Observation==q.Rig.Observation,"JSONL preserves measured vectors/quaternions, original timestamps and axle evidence");
        check(W.RigTail(roundtrip)?.Count==f.Tail.Count,"The bounded rear road samples survive the actual protocol");
        var reply=new W.Reply(W.Version,q.Session,1,1,1,0,q.Timestamp,"europe",[new(8,61){Rig=true}],false,null);
        check(W.ValidReply(reply,q,q.Timestamp),"A rig hit is bound to the same original query and progress domain");
        check(!W.ValidReply(reply,q with{Rig=null},q.Timestamp),"A child cannot invent a rig result when no body query was supplied");
        check(W.RigTail(q with{Rig=q.Rig with{Tail=q.Rig.Tail.Select((p,i)=>i==0?p with{Segment=9}:p).ToArray()}})==null,
            "A rear sample cannot borrow an unknown route identity");
        check(W.RigTail(q with{Rig=q.Rig with{Tail=Enumerable.Repeat(q.Rig.Tail[0],161).ToArray()}})==null,"Rear-route bounds are enforced before geometry work");
    }
    internal static void ClientRevocation(Action<bool,string> check)
    {
        var f=RigRoadPredictionReviewTests.Create();IFileSystem? source=new Files();
        using var client=new StaticRoadWorkerClient(()=>source,()=>new("europe",true),paths:_=>[Path.GetFullPath("fixture.scs")],schedule:_=>{});
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var original=f.Observation with{TelemetryUtcMs=now-30,Shape=f.Observation.Shape with{UtcMs=now-30}};
        object? Field(string name)=>typeof(StaticRoadWorkerClient).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client);
        void Store(StaticRoadWorkerClient.Result result)=>typeof(StaticRoadWorkerClient).GetField("completed",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(client,result);
        StaticRoadWorkerClient.Result Seed(RigRoadObservation observation,long epoch=3) {
            _=client.Read(f.Route,Vector3.Zero,80,epoch,observation);
            var result=new StaticRoadWorkerClient.Result((long)Field("generation")!,(long)Field("routeId")!,epoch,Stopwatch.GetTimestamp(),f.Route,"europe",[new(8,61){Rig=true}],false,null){
                QueryHorizon=80,RouteEpoch=(long)Field("routeEpoch")!,RigEpoch=(long)Field("rigEpoch")!,OriginalRig=observation};
            Store(result);return result;
        }
        var r=Seed(original);
        check(client.Read(f.Route,Vector3.Zero,80,3,original)==r&&client.IsRigCurrent(r,original),"A fresh original supported rig result is consumable");
        var next=original with{Shape=original.Shape with{Sequence=4,UtcMs=now-20},TelemetryUtcMs=now-20};
        check(client.IsRigCurrent(r,next),"A newer matching observation does not restamp but may retain an unexpired original result");
        var progress=new Vector3(0,0,-.25f);
        var moving=next with{TelemetryUtcMs=now-10,Shape=next.Shape with{Sequence=6,UtcMs=now-10,Truck=next.Shape.Truck with{Position=next.Shape.Truck.Position+progress},
            Trailer=next.Shape.Trailer! with{Position=next.Shape.Trailer.Position+progress}},
            TelemetryTruck=next.TelemetryTruck+progress,TelemetryTrailer=next.TelemetryTrailer+progress};
        check(client.IsRigCurrent(r,moving),"A bounded real forward advance along the original rig prediction remains usable");
        r=Seed(original);
        var lateral=next with{Shape=next.Shape with{Truck=next.Shape.Truck with{Position=next.Shape.Truck.Position+Vector3.UnitX},
            Trailer=next.Shape.Trailer! with{Position=next.Shape.Trailer.Position+Vector3.UnitX}},
            TelemetryTruck=next.TelemetryTruck+Vector3.UnitX,TelemetryTrailer=next.TelemetryTrailer+Vector3.UnitX};
        check(lateral.Valid(now)&&!client.IsRigCurrent(r,lateral),"Coherent but laterally displaced newer bodies cannot retain a hit from a different actual movement");
        check(!client.IsRigCurrent(r,moving),"A later return to the original movement cannot resurrect a revoked positive response");
        r=Seed(original);
        check(client.Read(f.Route,Vector3.UnitX,80,3,lateral)==null&&client.Read(f.Route,progress,80,3,moving)==null,
            "The ordinary Read rejection also permanently revokes a mismatched result before Control can return early");
        r=Seed(original);
        var changedYaw=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.2f);
        var changedTrailer=next.Shape.Trailer! with{Rotation=changedYaw,Position=next.Shape.Truck.Position+next.TruckHook-Vector3.Transform(next.TrailerHook,changedYaw)};
        var articulated=next with{Shape=next.Shape with{Trailer=changedTrailer},TelemetryTrailer=changedTrailer.Position,TrailerHeading=.2/(2*Math.PI)};
        check(articulated.Valid(now)&&!client.IsRigCurrent(r,articulated),"A newly articulated trailer revokes the original predicted sweep even if the tractor did not move");
        r=Seed(original);
        check(!client.IsRigCurrent(r,next with{Speed=20d/3.6+.001}),"Acceleration beyond actual 20 km/h revokes the final rig commit");
        check(!client.IsRigCurrent(r,original),"Returning to supported speed does not resurrect a result revoked in between");
        r=Seed(original);
        check(!client.IsRigCurrent(r,original with{Configuration="changed-trailer"}),"Trailer/configuration changes invalidate prior positive body hits");
        r=Seed(original);
        check(!client.IsRigCurrent(r,original with{Shape=original.Shape with{Truck=original.Shape.Truck with{Position=Vector3.UnitX*.05f}}}),
            "A native capture mutated at the same sequence revokes authority even with unchanged dimensions");
        r=Seed(original);
        check(client.Read(f.Route,Vector3.Zero,80,4,original)==null&&!client.IsCurrent(r,f.Route,4),"F9 operation epoch revokes old helper output");
        r=Seed(original);
        source=new Files();check(!client.IsCurrent(r,f.Route,3),"Archive source replacement revokes rig evidence independently of geometry");
        r=Seed(original);
        Store(r with{OriginalRig=original with{Shape=original.Shape with{UtcMs=now-200},TelemetryUtcMs=now-200}});
        check(client.Read(f.Route,Vector3.Zero,80,3,original)==null,"A fresh response timestamp cannot renew expired body measurement authority");
        client.Reset();check(!client.IsRigCurrent(r,original),"Reset never revives the previous body source");
    }
    internal static void TailPruning(Action<bool,string> check)
    {
        var f=RigRoadPredictionReviewTests.Create();var files=new Files();
        using var client=new StaticRoadWorkerClient(()=>files,()=>new("europe",true),paths:_=>[Path.GetFullPath("fixture.scs")],schedule:_=>{});
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var observation=f.Observation with{TelemetryUtcMs=now-10,Shape=f.Observation.Shape with{UtcMs=now-10}};
        W.Query Make(RouteTrace route,long epoch=3) {
            client.Read(route,Vector3.Zero,80,epoch,observation);
            var job=typeof(StaticRoadWorkerClient).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client);
            return (W.Query)typeof(StaticRoadWorkerClient).GetMethod("MakeQuery",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(client,[job])!;
        }
        var first=Make(f.Route);check(first.Rig!=null,"Initial owned query records an observed route tail");
        var original=(Vector3[])typeof(RouteTrace).GetField("points",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Route)!;
        var trimmed=original.Skip(100).ToArray();var pruned=new RouteTrace(trimmed,[],Enumerable.Repeat("same-road",trimmed.Length-1));
        var second=Make(pruned);
        check(second.Rig!=null&&second.Rig.Tail[0].Along<0,"Exact route pruning retains rear ground in original query progress coordinates, including negative tail progress");
        var front=second.Points.Select(p=>new TraceSlicePoint(new(p.X,p.Y,p.Z),new(p.Dx,p.Dy,p.Dz),p.Along,second.Segments[p.Segment],p.Connector)).ToArray();
        check(RigRoadPrediction.Build(observation,front,W.RigTail(second)!,now,out _ )!=null,
            "The retained tail reconstructs the same physical road instead of borrowing a different route's Along origin");
        var cancelled=Make(pruned,4);
        check(cancelled.Rig!=null&&cancelled.Rig.Tail[0].Along>=0,"A new F9 operation cannot revive a previous operation's retained tail");
    }
    sealed class Files:IFileSystem
    {
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>false;public bool DirectoryExists(string p)=>false;
        public IList<string> GetFiles(string p)=>[];public byte[] ReadAllBytes(string p)=>throw new NotSupportedException();
        public string ReadAllText(string p)=>throw new NotSupportedException();public string ReadAllText(string p,Encoding e)=>throw new NotSupportedException();
        public Stream Open(string p)=>throw new NotSupportedException();public string GetParent(string p)=>"/";
    }
}
