using System.Numerics;
using ETS2LA.Game.Data;
using ETS2LA.Game.SDK;
using Semaphore=ETS2LA.Game.SDK.Semaphore;
using MapType=TruckLib.Models.Ppd.SemaphoreType;

namespace MautAssistent;

internal sealed record RailCrossingDecision(bool Clear,string Reason,Vector3[] Expected,int[] States);

internal static class RailCrossingEvidence
{
    internal static RailCrossingDecision? Evaluate(PrefabPath input,TruckLib.Models.Ppd.Semaphore controller,
        IReadOnlyDictionary<string,RailSignalProfile>? profiles,Semaphore[] live)
    {
        var path=PrefabRefinement.OriginalFor(input);var descriptor=path.Prefab.Descriptor;
        if(descriptor==null||profiles==null||controller.Type!=MapType.UseProfile)return null;
        string Profile(TruckLib.Models.Ppd.Semaphore s)=>path.Prefab.Prefab?.SemaphoreProfile.ToString() is {Length:>0} custom?custom:s.Profile.ToString();
        var profileName=Profile(controller);
        if(!profiles.TryGetValue(profileName,out var profile)||profile.Type(controller.SemaphoreId)!="barrier_distance")return null;
        RailCrossingDecision Stop(string why)=>new(false,why,[],[]);
        if(controller.SemaphoreId>255||profile.Model(controller.SemaphoreId)!=""||profile.MappedId(controller.SemaphoreId)!=controller.SemaphoreId)
            return Stop("Bahnübergang: Controllerzuordnung nicht bestätigt");
        if(descriptor.Semaphores.Count(s=>s.SemaphoreId==controller.SemaphoreId&&Profile(s)==profileName)!=1||
            !PrefabModelTransform.TryRead(path,out var transform))return Stop("Bahnübergang: mehrdeutige Kartenanordnung");
        if(descriptor.Semaphores.Any(s=>Profile(s)==profileName&&(s.SemaphoreId>255||
            profile.MappedId(s.SemaphoreId)==controller.SemaphoreId&&s.Type!=MapType.UseProfile)))
            return Stop("Bahnübergang: ungeprüfter expliziter Signaltyp");
        if(descriptor.Semaphores.Any(s=>s!=controller&&s.Type==MapType.UseProfile&&Profile(s)==profileName&&
            profile.MappedId(s.SemaphoreId)==controller.SemaphoreId&&profile.Type(s.SemaphoreId)!="model_only"))
            return Stop("Bahnübergang: zusätzliche Signalrolle nicht aufgelöst");
        var members=descriptor.Semaphores.Where(s=>s.Type==MapType.UseProfile&&Profile(s)==profileName&&
            s.SemaphoreId<=255&&profile.MappedId(s.SemaphoreId)==controller.SemaphoreId&&
            profile.Type(s.SemaphoreId)=="model_only"&&profile.Model(s.SemaphoreId).Length>0).ToArray();
        if(members.Length is <1 or >16)return Stop("Bahnübergang: Schrankenanordnung fehlt");
        var expected=members.Select(s=>Vector3.Transform(s.Position,transform)).ToArray();
        if(expected.Any(p=>!float.IsFinite(p.LengthSquared()))||expected.Where((p,i)=>expected.Take(i).Any(q=>Vector3.Distance(p,q)<=2)).Any())
            return Stop("Bahnübergang: Schrankenpositionen nicht eindeutig");
        if(descriptor.Semaphores.Except(members).Any(s=>s!=controller&&
            expected.Any(p=>Vector3.Distance(p,Vector3.Transform(s.Position,transform))<=2)))
            return Stop("Bahnübergang: konkurrierende Signalposition");
        var center=Vector3.Transform(controller.Position,transform);
        if(!float.IsFinite(center.LengthSquared()))return Stop("Bahnübergang: ungültiger Controllerpunkt");
        var states=new List<int>();
        foreach(var point in expected) {
            var matches=live.Where(s=>s.id==controller.SemaphoreId&&Vector3.Distance(s.GetWorldCoordinates(),point)<=1).ToArray();
            states.Add(matches.Length>0&&matches.All(s=>s.type==SemaphoreType.GATE&&s.state==3)?3:-1);
        }
        // A separately reported warning light is never overridden by open arms.
        bool warning=live.Any(s=>s.id==controller.SemaphoreId&&s.type==SemaphoreType.TRAFFICLIGHT&&
            (Vector3.Distance(s.GetWorldCoordinates(),center)<=1||expected.Any(p=>Vector3.Distance(s.GetWorldCoordinates(),p)<=1))&&s.state is not (0 or 8));
        bool clear=!warning&&states.All(s=>s==3);
        return new(clear,clear?"Bahnübergang: alle zugeordneten Schranken offen":warning?
            "Bahnübergang: Warnsignal aktiv":"Bahnübergang: Schranke geschlossen, bewegt oder unbestätigt",expected,states.ToArray());
    }
}
