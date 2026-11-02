using System.Numerics;
using System.Text.Json;
using ETS2LA.Game.Data;
using ETS2LA.State;
using TruckLib.ScsMap;

namespace MautAssistent;

internal static class MapDiagnostics
{
    public static void Write(Vector3 position, string directory)
    {
        var game = ApplicationState.Current.RunningGame;
        var map = game?.GetMapData();
        if (map == null) return;
        var nodes = map.Nodes.Within(position.X-500,position.Z-500,position.X+500,position.Z+500);
        var prefabs = nodes.SelectMany(n=>new[]{n.ForwardItem,n.BackwardItem}).OfType<Prefab>().Distinct().ToArray();
        var result = prefabs.Select(p=> {
            try {
                var parsed = new ParsedPrefab(p);
                var d = parsed.Descriptor ?? throw new InvalidOperationException("Keine Prefab-Geometrie");
                return (object)new {
                    uid=p.Uid.ToString(), model=p.Model.ToString(),
                    nodes=p.Nodes.Select(n=>new{uid=n.Uid.ToString(),position=n.Position,rotation=n.Rotation}),
                    curves=d.NavCurves.Select((c,i)=>new {i,name=c.Name.ToString(), allowed=c.AllowedVehicles.ToString(),
                        start=c.StartPosition,end=c.EndPosition,startRotation=c.StartRotation,endRotation=c.EndRotation,
                        length=c.Length,next=c.NextLines.ToArray(),previous=c.PreviousLines.ToArray(),semaphore=c.SemaphoreId,
                        rule=c.TrafficRule.ToString()}),
                    signs=d.Signs.Select(s=>new{name=s.Name.ToString(),model=s.Model.ToString(),part=s.Part.ToString(),position=s.Position}),
                    semaphores=d.Semaphores.Select(s=>new{id=s.SemaphoreId,type=s.Type.ToString(),position=s.Position,profile=s.Profile.ToString()}),
                    descriptor=d
                };
            } catch(Exception ex) {return (object)new{uid=p.Uid.ToString(),error=ex.Message};}
        }).ToArray();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"map.json"), JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,position,prefabs=result},
            new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
    }
}
