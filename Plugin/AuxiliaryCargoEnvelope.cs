using System.Collections;
using System.Numerics;
using System.Text.RegularExpressions;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
namespace MautAssistent;

// Resolve auxiliary cargo geometry from its actual definition and chassis
// attachment points. "auxiliary_only" is never treated as geometry-free.
internal static class AuxiliaryCargoEnvelope
{
    internal static (Vector3 Minimum,Vector3 Maximum) Expand(IFileSystem files,string trailerId,string accessoryId,VehicleBody native,Func<string,Model>? readModel=null)
    {
        if(!Regex.IsMatch(trailerId,@"\A[a-z0-9_]{1,80}\z")||
            !Regex.IsMatch(accessoryId,@"\A\.def\.vehicle\.cargo\.[a-z0-9_]+\.[a-z0-9_]+\z"))
            throw new InvalidDataException("Auflieger-/Ladungsdefinition nicht eindeutig auflösbar.");
        string cargoPath=accessoryId.Replace('.','/')+".sii";
        var cargo=Units(files,cargoPath).Single(u=>u.Class=="accessory_cargo_data");
        if(cargo.Attributes.Keys.Any(k=>k is not ("model" or "aux_models" or "aux_models_locators"))||Text(cargo,"model")!="")
            throw new InvalidDataException("Zusätzliche Hauptladungsgeometrie ist noch nicht vollständig erfasst.");
        var cargoModels=Strings(cargo,"aux_models");var anchors=Strings(cargo,"aux_models_locators");
        if(cargoModels.Length==0||cargoModels.Length!=anchors.Length||cargoModels.Length>16)
            throw new InvalidDataException("Unvollständige Ladungs-Zusatzmodelle.");
        string trailerPath="/def/vehicle/trailer/"+trailerId+".sui";
        var trailerUnits=Units(files,trailerPath);
        var trailer=trailerUnits.Single(u=>u.Class=="trailer"&&u.Name=="trailer."+trailerId);
        var references=Strings(trailer,"accessories").ToHashSet(StringComparer.Ordinal);
        var referenced=references.Select(name=>trailerUnits.SingleOrDefault(u=>u.Name==name)??
            throw new InvalidDataException("Nicht aufgelöstes Auflieger-Zubehör: "+name)).ToArray();
        // Wheels and paint belong to the native vehicle model enclosure. They
        // cannot carry an extra cargo model or an arbitrary transformed part.
        foreach(var part in referenced.Where(u=>u.Class!="vehicle_accessory")) {
            string[] allowed=part.Class switch {"vehicle_wheel_accessory"=>["data_path","offset"],
                "vehicle_paint_job_accessory"=>["data_path"],_=>[]};
            if(allowed.Length==0||part.Attributes.Keys.Any(k=>!allowed.Contains(k))||Text(part,"data_path")=="")
                throw new InvalidDataException("Ungeprüftes Auflieger-Zubehör: "+part.Class);
        }
        var parts=referenced.Where(u=>u.Class=="vehicle_accessory").ToArray();
        if(parts.Any(u=>u.Attributes.Keys.Any(k=>k!="data_path")))throw new InvalidDataException("Unbekannte Transformation eines Aufliegerteils.");
        var definitions=parts.Select(u=>Units(files,Text(u,"data_path"))).SelectMany(u=>u).ToArray();
        var bodies=definitions.Where(u=>u.Class=="accessory_trailer_body_data").ToArray();
        if(bodies.Length!=1||bodies[0].Attributes.Keys.Any(k=>k!="cargo_loading_methods")||
            !Strings(bodies[0],"cargo_loading_methods").SequenceEqual(new[]{"auxiliary_only"})||
            definitions.Any(u=>u.Class is not ("accessory_chassis_data" or "accessory_trailer_body_data")))
            throw new InvalidDataException("Zusätzliche Auflieger-/Ladungsmodelle sind noch nicht vollständig erfasst.");
        var chassis=definitions.Where(u=>u.Class=="accessory_chassis_data").ToArray();
        if(chassis.Length!=1)throw new InvalidDataException("Ladungs-Koordinatenrahmen des Aufliegers ist unbestätigt.");
        var modelPaths=new[]{Text(chassis[0],"detail_model"),Text(chassis[0],"model")}
            .Concat(Strings(chassis[0],"lods")).Where(s=>s!="").Distinct(StringComparer.Ordinal).ToArray();
        if(modelPaths.Length is <1 or >8)throw new InvalidDataException("Unvollständige Chassismodelle.");
        var placements=new List<Locator>();
        foreach(string path in modelPaths) {
            ValidatePath(path,".pmd");var model=readModel?.Invoke(path)??Model.Open(path,files);
            string variant=Text(chassis[0],"variant");
            var selected=model.Variants.SingleOrDefault(v=>v.Name.ToString()==variant)??throw new InvalidDataException("Unbekannte Chassisvariante.");
            if(selected.Attributes.Count!=model.Parts.Count||selected.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1))
                throw new InvalidDataException("Unbekannte Chassissichtbarkeit.");
            placements.AddRange(model.Parts.Where((p,i)=>selected.Attributes[i].Value==1).SelectMany(p=>p.Locators));
        }
        var low=native.Minimum;var high=native.Maximum;
        for(int index=0;index<cargoModels.Length;index++) {
            string path=cargoModels[index];ValidatePath(path,".pmd");
            var model=readModel?.Invoke(path)??Model.Open(path,files);
            try {using var collision=files.Open(Path.ChangeExtension(path,".pmc"));throw new InvalidDataException("Ungeprüfte zusätzliche Ladungskollision.");}
            catch(FileNotFoundException) { }
            if(model.Skeleton.Count!=0||model.Parts.Any(p=>p.Locators.Count!=0)||model.Parts.Count==0||model.Variants.Count==0||
                model.Variants.Any(v=>v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)))
                throw new InvalidDataException("Animierte oder zusammengesetzte Ladung benötigt weitere Hüllprüfung.");
            var matches=placements.Where(l=>l.Name.ToString()==anchors[index]).ToArray();
            if(matches.Length==0)throw new InvalidDataException("Ladungs-Befestigungspunkt fehlt: "+anchors[index]);
            foreach(var locator in matches) {
                // A model root identified by the actual trailer definition is
                // accepted only when all used anchors agree with its live box.
                if(locator.Position.X<native.Minimum.X-.05f||locator.Position.X>native.Maximum.X+.05f||
                    locator.Position.Y<native.Minimum.Y-.05f||locator.Position.Y>native.Maximum.Y+.05f||
                    locator.Position.Z<native.Minimum.Z-.05f||locator.Position.Z>native.Maximum.Z+.05f)
                    throw new InvalidDataException("Ladungsanker passt nicht zur aktuellen Aufliegerbox.");
                var transform=StaticHookupGeometry.Transform(locator);
                foreach(var piece in model.Parts.Where((p,i)=>model.Variants.Any(v=>v.Attributes[i].Value==1)).SelectMany(p=>p.Pieces)) {
                    if(piece.UseBoneIndexes||piece.UseBoneWeights)throw new InvalidDataException("Animierte Zusatzladung.");
                    foreach(var vertex in piece.Vertices) {
                        var point=Vector3.Transform(vertex.Position,transform);
                        if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||!float.IsFinite(point.Z))throw new InvalidDataException("Ungültige Ladungskoordinaten.");
                        low=Vector3.Min(low,point);high=Vector3.Max(high,point);
                    }
                }
            }
        }
        return (low,high);
    }
    static Unit[] Units(IFileSystem files,string path)
    {
        ValidatePath(path,path.EndsWith(".sui",StringComparison.Ordinal)?".sui":".sii");
        string text=files.ReadAllText(path);
        if(!text.TrimStart().StartsWith("SiiNunit",StringComparison.Ordinal))text="SiiNunit {\n"+text+"\n}";
        return SiiFile.Load(text,Path.GetDirectoryName(path)!.Replace('\\','/'),files).Units.ToArray();
    }
    static string Text(Unit unit,string name)=>unit.Attributes.TryGetValue(name,out var value)?(object?)value switch {
        string text=>text,TruckLib.Token token=>token.ToString(),LinkPointer link=>link.Value,_=>throw new InvalidDataException("Unbekannter Definitionswert: "+name)}:"";
    static string[] Strings(Unit unit,string name)
    {
        if(!unit.Attributes.TryGetValue(name,out var value))return [];
        if((object?)value is not IEnumerable list||(object?)value is string)throw new InvalidDataException("Unbekannte Definitionsliste: "+name);
        return list.Cast<object>().Select(v=>v switch {string s=>s,LinkPointer p=>p.Value,OwnerPointer p=>p.Value,TruckLib.Token t=>t.ToString(),_=>throw new InvalidDataException("Unbekannter Listenwert: "+v.GetType().Name)}).ToArray();
    }
    static void ValidatePath(string path,string extension)
    {if(!path.StartsWith('/')||!path.EndsWith(extension,StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\'))throw new InvalidDataException("Ungültiger Definitionspfad.");}
}
