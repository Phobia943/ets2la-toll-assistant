using System.IO.MemoryMappedFiles;
namespace MautAssistent;

internal sealed class ParkedSceneReader:IDisposable
{
    MemoryMappedFile? map;MemoryMappedViewAccessor? view;
    readonly byte[] buffer=new byte[SceneSnapshotProtocol.Size];
    readonly object sync=new();
    uint pid;readonly SceneTimeline timeline=new();
    public string Status {get;private set;}="Parkfahrzeuge: ergänzende Erfassung noch nicht verbunden.";
    public SceneSnapshot? Read(uint expectedPid)
    {
        lock(sync) {
            if(!OperatingSystem.IsWindows()||expectedPid==0)return null;
            try {
                if(expectedPid!=pid){Close();pid=expectedPid;}
                if(view==null) {
                    map=MemoryMappedFile.OpenExisting("Local\\ETS2LA-MautScene-v1-"+pid,MemoryMappedFileRights.Read);
                    view=map.CreateViewAccessor(0,SceneSnapshotProtocol.Size,MemoryMappedFileAccess.Read);
                }
                uint before=view.ReadUInt32(12);if((before&1)!=0)return null;
                Thread.MemoryBarrier();view.ReadArray(0,buffer,0,buffer.Length);Thread.MemoryBarrier();
                if(before!=view.ReadUInt32(12)||before!=BitConverter.ToUInt32(buffer,12))return null;
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var frame=SceneSnapshotProtocol.Decode(buffer,now,pid,allowIncompleteForSafety:true);
                if(frame==null){Status="Parkfahrzeuge: keine aktuelle vollständige Erfassung.";return null;}
                if(!timeline.Accept(frame,now)){Status="Parkfahrzeuge: Simulationsuhr oder Erfassungsfolge ungültig.";return null;}
                Status=$"Parkfahrzeuge: {frame.Bodies.Count} Körper, {frame.Scopes.Count} geladene Anlagen erfasst.";
                return frame;
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) {
                Status="Parkfahrzeuge: Erfassung nicht verfügbar ("+ex.GetType().Name+").";Close();return null;
            }
        }
    }
    void Close(){view?.Dispose();map?.Dispose();view=null;map=null;timeline.Reset();}
    public void Dispose(){lock(sync){Close();pid=0;}}
}
