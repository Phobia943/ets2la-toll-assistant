using System.Numerics;
using System.Reflection;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.ScsMap;

internal static class StaticRoadSourceReviewTests
{
    internal static void SourceAndRegion(Action<bool,string> check)
    {
        IFileSystem? files=new NoReads();string? map="europe";var queued=new Queue<Action>();
        StaticRoadSceneSource? source=null;Action<StaticRoadSceneSource.Request>? duringLoad=null;
        var mesh=new RigStaticGeometry([new(new(0,0,0),new(0,1,0),new(0,0,1))]);
        int loads=0;
        source=new(()=>files,()=>map==null?null:new StaticRoadSceneSource.MapSelection(map,true),request=>{
            loads++;duringLoad?.Invoke(request);return new(request.Generation,request.Selection.BaseMapName,request.Center,300,
                Array.AsReadOnly(new[]{new StaticRoadSceneSource.Body((ulong)loads,mesh,Vector3.Zero,Vector3.One)}),0,null);
        },queued.Enqueue,_=>["fake-archive-no-host-read"]);
        check(source.Read(Vector3.Zero)==null&&queued.Count==1,"Initial load must be asynchronous and queue one job");
        for(int i=0;i<100;i++)check(source.Read(Vector3.Zero)==null,"Pending source is unknown without exposing phantom free space");
        check(queued.Count==1&&loads==0,"Repeated ticks must not perform worker work or pile up jobs");
        queued.Dequeue()();var first=source.Read(Vector3.Zero)!;
        check(first.Bodies.Count==1&&loads==1,"Completed source publishes immutable positive mesh evidence");
        check(source.IsCurrent(first,Vector3.Zero),"Final source recheck accepts current immutable snapshot");
        check(ReferenceEquals(first,source.Read(new(120,0,0)))&&queued.Count==1,"Refresh must retain the old source only while its region still covers the caller");
        check(source.Read(new(151,0,0))==null,"Beyond the reuse region old bodies must disappear immediately");
        queued.Dequeue()();check(source.Read(new(151,0,0))!=null,"A completed nearby rebuild restores only its own valid region");
        var previous=files;files=new NoReads();check(source.Read(new(151,0,0))==null,"File-system identity change must revoke old geometry before the worker runs");
        queued.Dequeue()();var changed=source.Read(new(151,0,0))!;check(changed.Generation!=first.Generation,"New source must have a new generation");
        map="usa";check(source.Read(new(151,0,0))==null,"Map name change must revoke geometry even for the same filesystem");
        queued.Dequeue()();check(source.Read(new(151,0,0))!.MapName=="usa","Only the matching map can be published");
        source.Reset();map="europe";files=previous;check(source.Read(Vector3.Zero)==null,"Returning to an earlier source after reset must not revive its snapshot");
        duringLoad=_=>{duringLoad=null;files=new NoReads();check(source!.Read(Vector3.Zero)==null,"Source replacement during worker load must invalidate the in-flight source");};
        queued.Dequeue()();var fresh=source.Read(Vector3.Zero)!;
        check(fresh.Generation!=first.Generation&&fresh.Generation!=changed.Generation,"An old worker must not publish after a newer source request");
        files=null;check(source.Read(Vector3.Zero)==null,"Null source must immediately remove all prior bodies");
        files=previous;map="../europe";check(source.Read(Vector3.Zero)==null&&queued.Count==0,"Unsafe or missing map names cannot create an archive job");
        map="europe";source.Read(Vector3.Zero);duringLoad=_=>{duringLoad=null;source!.Reset();};queued.Dequeue()();
        check(source.Read(Vector3.Zero)==null,"Reset while loading must prevent stale publication");
    }

    internal static void RetryAndFinalRecheck(Action<bool,string> check)
    {
        bool propagated=false;
        try{StaticRoadSceneSource.ReadModelAsset(new TransientFiles(),"/test.pmd");}catch(IOException){propagated=true;}
        check(propagated,"Transient model archive failure must reach bounded source retry rather than a cached null");
        IFileSystem files=new NoReads();var original=files;double now=0;bool pathsReady=false,throwLoad=true;
        var queued=new Queue<Action>();int loads=0;
        var source=new StaticRoadSceneSource(()=>files,()=>new("europe",false),request=>{
            loads++;if(throwLoad)throw new IOException("Transient source failure");
            return new(request.Generation,"chosen-mod-map",request.Center,300,Array.Empty<StaticRoadSceneSource.Body>(),0,null);
        },queued.Enqueue,_=>pathsReady?["archive"]:null,()=>now);
        check(source.Read(Vector3.Zero)==null&&queued.Count==0,"Unavailable archive paths must not queue IO");
        pathsReady=true;source.Read(Vector3.Zero);check(queued.Count==0,"Failed metadata must not retry every tick");
        now=5;source.Read(Vector3.Zero);check(queued.Count==1,"Same stationary source must retry after bounded delay");
        queued.Dequeue()();var failed=source.Read(Vector3.Zero)!;
        check(failed.Bodies.Count==0&&failed.Issue!=null&&loads==1,"Transient worker exception must remain unknown");
        throwLoad=false;now=14;source.Read(Vector3.Zero);check(queued.Count==0,"Failure retry must respect backoff");
        now=15;source.Read(Vector3.Zero);check(queued.Count==1,"Worker failure must recover without moving or restarting");
        queued.Dequeue()();var ready=source.Read(Vector3.Zero)!;
        check(ready.MapName=="chosen-mod-map"&&source.IsCurrent(ready,Vector3.Zero),"Actual worker-selected mod map must survive publication");
        files=new NoReads();check(!source.IsCurrent(ready,Vector3.Zero)&&queued.Count==0,"Final recheck must revoke changed source without scheduling any work");
        files=original;check(!source.IsCurrent(ready,Vector3.Zero),"Returning to an old source after final recheck must not revive old geometry");
    }

    internal static void InstanceEligibility(Action<bool,string> check)
    {
        var item=new Model();
        check(StaticRoadSceneSource.EligibleInstance(item),"Plain main-layer, LOD0, collision-enabled model can be inspected");
        item.Layer=1;check(!StaticRoadSceneSource.EligibleInstance(item),"Conditional/editor layer must not become a physical obstacle");item.Layer=0;
        item.Lod=1;check(!StaticRoadSceneSource.EligibleInstance(item),"LOD substitute must not duplicate physical models");item.Lod=0;
        item.Collision=false;check(!StaticRoadSceneSource.EligibleInstance(item),"Disabled model collision cannot block a road");item.Collision=true;
        var metadata=typeof(MapItem).GetProperty("Kdop",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(item)!;
        var field=metadata.GetType().GetField("Flags",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var flags=(FlagField)field.GetValue(metadata)!;
        foreach(int bit in new[]{0,1,2,3,6,7,8,9,10,11,12,16,18,19,20,31}) {
            flags[bit]=true;field.SetValue(metadata,flags);
            check(!StaticRoadSceneSource.EligibleInstance(item),"Unknown raw flag "+bit+" cannot establish active static collision");
            flags[bit]=false;field.SetValue(metadata,flags);
        }
        check(StaticRoadSceneSource.EligibleInstance(item),"Clearing only the test flags must restore the original static instance eligibility");
    }
    class NoReads:IFileSystem
    {
        public char DirectorySeparator=>'/';
        public virtual bool FileExists(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public bool DirectoryExists(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public IList<string> GetFiles(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public byte[] ReadAllBytes(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public string ReadAllText(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public string ReadAllText(string p,Encoding e)=>throw new InvalidOperationException("Host stream used by static worker");
        public virtual Stream Open(string p)=>throw new InvalidOperationException("Host stream used by static worker");
        public string GetParent(string p)=>throw new InvalidOperationException("Host stream used by static worker");
    }
    sealed class TransientFiles:NoReads
    {
        public override bool FileExists(string p)=>true;
        public override Stream Open(string p)=>throw new IOException("Temporary archive read failure");
    }
}
