using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.ComponentModel;

namespace MautAssistent;

internal sealed class VehicleGeometryReader:IDisposable
{
    MemoryMappedFile? map;
    MemoryMappedViewAccessor? view;
    readonly byte[] buffer=new byte[VehicleGeometryProtocol.Size];
    readonly Func<uint,bool> isGame;
    readonly string mapName;
    readonly object sync=new();
    bool suspended;
    uint checkedPid;ulong checkedSession;
    public string Status {get;private set;}="Fahrzeugmaße: Messmodul noch nicht verbunden.";
    internal VehicleGeometryReader(Func<uint,bool>? isGame=null,string mapName="Local\\ETS2LA-MautGeometry-v1") {
        this.mapName=mapName;
        this.isGame=isGame??(pid=>{
        using var process=Process.GetProcessById(checked((int)pid));
        return process.ProcessName.Equals("eurotrucks2",StringComparison.OrdinalIgnoreCase)||
            process.ProcessName.Equals("amtrucks",StringComparison.OrdinalIgnoreCase);
        });
    }
    public VehicleGeometry? Read()
    {
        lock(sync)return ReadLocked();
    }
    VehicleGeometry? ReadLocked()
    {
        if(suspended)return null;
        if(!OperatingSystem.IsWindows()){Status="Fahrzeugmaße: Messmodul benötigt Windows.";return null;}
        try {
            if(view==null) {
                map=MemoryMappedFile.OpenExisting(mapName,MemoryMappedFileRights.Read);
                view=map.CreateViewAccessor(0,VehicleGeometryProtocol.Size,MemoryMappedFileAccess.Read);
            }
            int before=view.ReadInt32(12);if((before&1)!=0){Status="Fahrzeugmaße: Probe wird gerade geschrieben.";return null;}
            Thread.MemoryBarrier();view.ReadArray(0,buffer,0,buffer.Length);Thread.MemoryBarrier();
            if(before!=view.ReadInt32(12)||before!=BitConverter.ToInt32(buffer,12)) {Status="Fahrzeugmaße: Probe wurde während des Lesens geändert.";return null;}
            var result=VehicleGeometryProtocol.Decode(buffer,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if(result==null){Status="Fahrzeugmaße: keine frische gültige Fahrzeugprobe.";CloseLocked();return null;}
            if(result.ProcessId!=checkedPid||result.Session!=checkedSession) {
                if(!isGame(result.ProcessId)){Status="Fahrzeugmaße: Quelle ist kein bestätigter Spielprozess.";return null;}
                checkedPid=result.ProcessId;checkedSession=result.Session;
            }
            Status="Fahrzeugmaße verbunden – nur Messung, keine automatische Freigabe.";
            return result;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException or Win32Exception) {
            Status="Fahrzeugmaße nicht verfügbar: "+ex.GetType().Name;CloseLocked();return null;
        }
    }
    void CloseLocked(){view?.Dispose();map?.Dispose();view=null;map=null;checkedPid=0;checkedSession=0;}
    public void Resume(){lock(sync){suspended=false;Status="Fahrzeugmaße: Messmodul noch nicht verbunden.";}}
    public void Dispose(){lock(sync){suspended=true;CloseLocked();Status="Fahrzeugmessung ausgeschaltet.";}}
}
