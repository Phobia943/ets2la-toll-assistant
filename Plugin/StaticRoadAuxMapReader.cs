using System.Reflection;
using System.Runtime.ExceptionServices;
using TruckLib;
using TruckLib.ScsMap;

namespace MautAssistent;

// Map.Model, ordinary Sign and standalone Gate instances use .aux with their own nodes. Avoid terrain/road .base and
// .data deserialization before PostProcessItem could discard them. These are
// the same pinned TruckLib parser methods used
// by Map.Read, including the layer file and final node reference resolution.
internal static class StaticRoadAuxMapReader
{
    static readonly MethodInfo? aux=typeof(Map).GetMethod("ReadAux",BindingFlags.Instance|BindingFlags.NonPublic),
        layer=typeof(Map).GetMethod("ReadLayer",BindingFlags.Instance|BindingFlags.NonPublic),
        references=typeof(Map).GetMethod("UpdateReferences",BindingFlags.Instance|BindingFlags.NonPublic);
    internal static void Read(Map map,IFileSystem files,string mapName,SectorCoordinate sector)
    {
        if(aux==null||layer==null||references==null||map.MapItems.Count!=0||map.Nodes.Count!=0)
            throw new InvalidDataException("Static auxiliary map reader is incompatible or not empty");
        string prefix="/map/"+mapName+"/"+Sector.SectorFileNameFromSectorCoords(sector);
        if(!files.FileExists(prefix+".aux"))return;
        try {
            aux.Invoke(map,[prefix+".aux",files]);
            layer.Invoke(map,[prefix+".layer",files]);
            references.Invoke(map,null);
        } catch(TargetInvocationException e) when(e.InnerException!=null) {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;
        }
    }
}
