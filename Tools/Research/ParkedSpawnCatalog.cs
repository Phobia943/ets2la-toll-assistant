// Offline catalogue research only; excluded from the driving plugin build.
using System.Collections;
using System.Numerics;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
namespace MautAssistent;

// Static POSSIBLE spawn collision/mesh enclosure, not current vehicle tracking.
// SCS Game_data defines traffic_storage_<type>[.<suffix>].sii as the catalogue.
internal sealed class ParkedSpawnCatalog(IFileSystem files,Func<string,(Model Model,byte[] Pmg)> load,
    IReadOnlyDictionary<string,Unit> hookups,IReadOnlySet<string> nonSolid)
{
    double? carRadius;
    readonly Dictionary<string,double> extras=new(StringComparer.Ordinal);
    internal double Radius(Unit spawn)
    {
        if(spawn.Class!="parked_vehicle"||spawn.Attributes.Keys.Any(k=>k is not ("allowed_vehicle" or "low_poly_only" or "probability_day" or "probability_night"))||
            !Strings(spawn,"allowed_vehicle").SequenceEqual(new[]{"car"})||
            !spawn.Attributes.TryGetValue("low_poly_only",out var low)||(object?)low is not false)
            throw new InvalidDataException("Nicht unterstützte Parkfahrzeug-Auswahl");
        foreach(string key in new[]{"probability_day","probability_night"})if(spawn.Attributes.TryGetValue(key,out var raw)) {
            double value=(object?)raw switch {int x=>x,float x=>x,double x=>x,_=>double.NaN};
            if(!double.IsFinite(value)||value<0||value>1)throw new InvalidDataException("Ungültige Parkwahrscheinlichkeit");
        }
        return carRadius??=ReadCars(); // p<1 is never an absence proof.
    }
    double ReadCars()
    {
        var type=Units("/def/vehicle/traffic_vehicle_type.sii").Single(u=>u.Name=="traffic.vehicle_type.car");
        if(type.Class!="traffic_vehicle_type"||type.Attributes.Keys.Any(k=>k is not ("speed_class" or "license_plate_type" or "tags" or "validation_groups"))||
            Text(type,"speed_class")!="car"||Text(type,"license_plate_type")!="car")throw new InvalidDataException("Unbekannter Pkw-Typvertrag");
        var paths=files.GetFiles("/def/vehicle/").Where(p=>p=="/def/vehicle/traffic_storage_car.sii"||
            p.StartsWith("/def/vehicle/traffic_storage_car.",StringComparison.Ordinal)&&p.EndsWith(".sii",StringComparison.Ordinal)).Distinct().ToArray();
        if(paths.Length is <1 or >64)throw new InvalidDataException("Unvollständiger Pkw-Katalog");
        var units=paths.SelectMany(Units).ToArray();
        if(units.Length>8192||units.GroupBy(u=>u.Name).Any(g=>g.Count()!=1))throw new InvalidDataException("Mehrdeutiger Pkw-Katalog");
        var lookup=units.ToDictionary(u=>u.Name,StringComparer.Ordinal);
        var vehicles=units.Where(u=>u.Class=="traffic_vehicle").ToArray();
        if(vehicles.Length is <1 or >512)throw new InvalidDataException("Unvollständige Pkw-Auswahl");
        var eligibleNames=new HashSet<string>(StringComparer.Ordinal);
        void Include(Unit vehicle,HashSet<string> stack,int depth) {
            if(depth>32||!stack.Add(vehicle.Name))throw new InvalidDataException("Zyklische Fahrzeugvarianten");
            if(!eligibleNames.Add(vehicle.Name)){stack.Remove(vehicle.Name);return;}
            foreach(string name in Strings(vehicle,"variant")) {
                if(!lookup.TryGetValue(name,out var target)||target.Class!="traffic_vehicle")throw new InvalidDataException("Nicht aufgelöste Fahrzeugvariante");
                Include(target,stack,depth+1);
            }
            stack.Remove(vehicle.Name);
        }
        foreach(var vehicle in vehicles) {
            if(vehicle.Attributes.TryGetValue("allow_parked",out var allowed)&&(object?)allowed is not bool)throw new InvalidDataException("Unbekannte Parkzulassung");
            if(!vehicle.Attributes.TryGetValue("allow_parked",out allowed)||(object?)allowed is not false)Include(vehicle,new(),0);
        }
        double maximum=0;int eligible=0;
        foreach(var vehicle in vehicles) {
            if(!eligibleNames.Contains(vehicle.Name))continue;
            if(vehicle.Attributes.Keys.Any(k=>k is not ("accessories" or "allow_parked" or "color" or "driver_model" or "license_plate_type" or "max_speed" or
                "sound_horn" or "sound_move" or "spawn_condition" or "spawn_ratio" or "tags" or "variant" or "vehicle_mass")))
                throw new InvalidDataException("Ungeprüfter Fahrzeugaufbau: "+vehicle.Name);
            var references=Strings(vehicle,"accessories");
            if(references.Length is <1 or >32||references.Distinct().Count()!=references.Length)throw new InvalidDataException("Unvollständiges Pkw-Zubehör");
            double radius=0,wheelOrigin=0,wheelScale=0,wheelSize=0,driverOrigin=0,driverScale=0;int chassisCount=0,wheels=0;
            foreach(string reference in references) {
                if(!lookup.TryGetValue(reference,out var part))throw new InvalidDataException("Nicht aufgelöstes Pkw-Zubehör");
                if(part.Class is not ("vehicle_accessory" or "vehicle_wheel_accessory")||
                    part.Attributes.Keys.Any(k=>k!="data_path"&&(part.Class!="vehicle_wheel_accessory"||k!="offset")))
                    throw new InvalidDataException("Unbekannte Pkw-Zubehörtransformation");
                if(part.Class=="vehicle_wheel_accessory"&&part.Attributes.TryGetValue("offset",out var offset)&&
                    ((object?)offset switch {int x=>(double)x,uint x=>x,long x=>x,ulong x=>x,float x=>x,double x=>x,_=>double.NaN})!=0)
                    throw new InvalidDataException("Ungeprüfter Pkw-Radindex: "+(((object?)offset)?.GetType().FullName??"null"));
                var definitions=Units(Text(part,"data_path"));
                if(definitions.Length!=1)throw new InvalidDataException("Mehrdeutiges Pkw-Zubehör");
                var definition=definitions[0];bool wheel=part.Class=="vehicle_wheel_accessory";
                string[] allowed=wheel?["model"]:["model","lods","collision","variant","variant_uk","look","shadow_intensity","shadow_texture"];
                if(definition.Class!=(wheel?"accessory_wheel_data":"accessory_chassis_data")||definition.Attributes.Keys.Any(k=>!allowed.Contains(k)))
                    throw new InvalidDataException("Ungeprüfte Pkw-Geometrie");
                if(wheel)wheels++;else {chassisCount++;radius=Math.Max(radius,PmcEnvelope.Radius(files.ReadAllBytes(PathChecked(Text(definition,"collision"),".pmc"))));}
                var models=new[]{Text(definition,"model")}.Concat(Strings(definition,"lods")).Distinct().ToArray();
                if(models.Length is <1 or >8)throw new InvalidDataException("Unvollständige Pkw-Modelle");
                foreach(string path in models) {
                    var (model,pmg)=load(PathChecked(path,".pmd"));
                    if(model.Skeleton.Count!=0||model.Parts.Count==0||model.Variants.Count==0||model.Variants.Any(v=>
                        v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)))throw new InvalidDataException("Animiertes Pkw-Modell");
                    string[] chosen=wheel?model.Variants.Select(v=>v.Name.ToString()).ToArray():
                        new[]{Text(definition,"variant"),Text(definition,"variant_uk")}.Where(v=>v!="").Distinct().ToArray();
                    if(chosen.Length==0)throw new InvalidDataException("Pkw-Variantenangabe fehlt");
                    // A simplified LOD may not retain the high-detail mask
                    // names. Do not guess its default: enclose EVERY part and
                    // inspect every child, irrespective of visibility.
                    bool knownMasks=chosen.All(name=>model.Variants.Any(v=>v.Name.ToString()==name));
                    var visible=model.Parts.Where((p,i)=>!knownMasks||model.Variants.Any(v=>chosen.Contains(v.Name.ToString())&&v.Attributes[i].Value==1)).ToArray();
                    double mesh=0;
                    foreach(var piece in visible.SelectMany(p=>p.Pieces)) {
                        if(piece.UseBoneIndexes||piece.UseBoneWeights)throw new InvalidDataException("Animiertes Pkw-Teil");
                        foreach(var vertex in piece.Vertices) {double length=vertex.Position.Length();if(!double.IsFinite(length))throw new InvalidDataException("Ungültiges Pkw-Modell");mesh=Math.Max(mesh,length);}
                    }
                    if(wheel)wheelSize=Math.Max(wheelSize,mesh);else radius=Math.Max(radius,mesh);
                    foreach(var locator in visible.SelectMany(p=>p.Locators)) {
                        _=StaticHookupGeometry.Transform(locator);
                        string? hook=locator.HookupOffset==-1?"":TollModelEvidence.ReadHookup(pmg,locator.HookupOffset);
                        if(hook==null)throw new InvalidDataException("Ungültiger Pkw-Hookup");
                        if(hook!="")radius=Math.Max(radius,locator.Position.Length()+locator.Scale*1.02*Extra(hook,new HashSet<string>(),0));
                        if(!wheel&&locator.Name.ToString().StartsWith("wheel",StringComparison.Ordinal)) {
                            wheelOrigin=Math.Max(wheelOrigin,locator.Position.Length());wheelScale=Math.Max(wheelScale,locator.Scale);
                        }
                        // Enclose every possible reserved driver placement,
                        // including driver_eu/uk/lod/numeric metadata locators.
                        if(!wheel){driverOrigin=Math.Max(driverOrigin,locator.Position.Length());driverScale=Math.Max(driverScale,locator.Scale);}
                        if(wheel) {
                            if(locator.Name.ToString()!="bb"||locator.Scale!=1||locator.Rotation!=Quaternion.Identity||hook!="")throw new InvalidDataException("Ungeprüfter Rad-Metadatenlocator");
                            wheelSize=Math.Max(wheelSize,locator.Position.Length());
                        }
                    }
                }
            }
            if(chassisCount!=1||wheels<2||wheelScale<=0||wheelSize<=0)throw new InvalidDataException("Unvollständige Pkw-Hülle");
            var drivers=Strings(vehicle,"driver_model");
            if(drivers.Length>0) {
                if(driverScale<=0)throw new InvalidDataException("Fahrer-Befestigung fehlt");
                foreach(string driver in drivers)radius=Math.Max(radius,driverOrigin+driverScale*1.02*RigidRadius(driver));
            }
            maximum=Math.Max(maximum,Math.Max(radius,wheelOrigin+wheelScale*1.02*wheelSize)+.1);eligible++;
        }
        if(eligible==0||!double.IsFinite(maximum)||maximum<=0||maximum>100)throw new InvalidDataException("Pkw-Hülle nicht begrenzbar");
        return maximum;
    }
    double Extra(string hook,HashSet<string> stack,int depth)
    {
        if(nonSolid.Contains(hook))return 0;
        if(extras.TryGetValue(hook,out double found))return found;
        if(depth>4||!stack.Add(hook)||!hookups.TryGetValue(hook,out var unit))throw new InvalidDataException("Nicht auflösbares Pkw-Untermodell: "+hook);
        try {
            if(IsGeneratedVisual(unit))return extras[hook]=0;
            if(unit.Class is "flare_lamp" or "flare_vehicle" or "flare_blink") {
                if(unit.Class=="flare_lamp") {
                    string lamp=StaticHookupGeometry.Paths(unit).Single();
                    if(!FlareEvidence.Validate(unit,load(lamp).Model,lamp,files))throw new InvalidDataException("Ungeprüftes Lampenmodell");
                    return extras[hook]=0;
                }
                if(!VehicleFlareFields(unit))throw new InvalidDataException("Unbekannte Fahrzeug-Flaredefinition");
                if(unit.Class=="flare_vehicle"&&!unit.Attributes.ContainsKey("model")) {
                    if(!unit.Attributes.ContainsKey("light_type")||!unit.Attributes.ContainsKey("type")||!unit.Attributes.ContainsKey("setup"))
                        throw new InvalidDataException("Unvollständige modelllose Fahrzeuglichtdefinition");
                    return extras[hook]=0;
                }
                string path=StaticHookupGeometry.Paths(unit).Single();
                // Same actual shader/model/collision proof as the lamp reader;
                // the vehicle/blink fields only govern rendering of that flare.
                var normalized=new Unit("flare_lamp","normalized.flare");normalized.Attributes["model"]=path;
                normalized.Attributes["dir_type"]=new Token("all");normalized.Attributes["scale_factor"]=1;
                if(!FlareEvidence.Validate(normalized,load(path).Model,path,files))throw new InvalidDataException("Ungeprüftes Fahrzeug-Flaremodell: "+path);
                return extras[hook]=0;
            }
            if(unit.Class!="passenger_hookup"||unit.Attributes.Keys.Any(k=>k is not ("model" or "hookup_variant" or "probability_day" or "probability_night")))
                throw new InvalidDataException("Ungeprüftes Pkw-Untermodell: "+hook);
            double radius=0;
            foreach(string variant in Strings(unit,"hookup_variant"))radius=Math.Max(radius,Extra(variant,stack,depth+1));
            if(unit.Attributes.ContainsKey("model"))foreach(string model in StaticHookupGeometry.Paths(unit))radius=Math.Max(radius,RigidRadius(model));
            if(radius<=0)throw new InvalidDataException("Passagiermodell fehlt");
            return extras[hook]=radius;
        }finally{stack.Remove(hook);}
    }
    internal static bool IsGeneratedVisual(Unit unit)=>unit.Class switch {
        "license_plate"=>unit.Attributes.Keys.All(k=>k=="rear_plate")&&
            (!unit.Attributes.TryGetValue("rear_plate",out var rear)||(object?)rear is bool),
        "wheel_particles"=>unit.Attributes.Count==0,
        _=>false
    };
    internal static bool VehicleFlareFields(Unit unit)
    {
        if(unit.Class is not ("flare_vehicle" or "flare_blink")||unit.Attributes.Count==0)return false;
        static double Number(object? raw)=>raw switch {int x=>x,uint x=>x,long x=>x,ulong x=>x,float x=>x,double x=>x,_=>double.NaN};
        static bool Color(object? raw)=>raw switch {
            Vector3 v=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z),
            System.Runtime.CompilerServices.ITuple t when t.Length==3=>Enumerable.Range(0,3).All(i=>double.IsFinite(Number(t[i]))&&Math.Abs(Number(t[i]))<=1000000),
            _=>false};
        foreach(var attribute in unit.Attributes) {
            string key=attribute.Key;object? value=attribute.Value;
            if(key=="model")continue; // Actual path/list is checked by Paths.
            if(key=="forward_distance"){if(value is not bool)return false;continue;}
            if(key is "ambient_color" or "diffuse_color" or "specular_color"){if(!Color(value))return false;continue;}
            if(key is "dir_type" or "light_type" or "type" or "setup") {
                string? text=value switch {string s=>s,Token t=>t.ToString(),LinkPointer p=>p.Value,_=>null};
                if(text==null||!System.Text.RegularExpressions.Regex.IsMatch(text,@"\A[a-zA-Z0-9_.:-]{1,128}\z"))return false;continue;
            }
            if(key=="blink_pattern"){if(value is not string pattern||!System.Text.RegularExpressions.Regex.IsMatch(pattern,@"\A[-X]{1,128}\z"))return false;continue;}
            if(key is not ("scale_factor" or "default_scale" or "range" or "inner_angle" or "outer_angle" or "fade_distance" or "fade_span" or "visual_offset" or
                "scaling_start_distance" or "scaling_end_distance" or "state_change_duration" or "flare_inner_angle" or "blink_step_length"))return false;
            double number=Number(value);
            if(!double.IsFinite(number)||Math.Abs(number)>100000||key!="visual_offset"&&number<0||
                key is "scale_factor" or "default_scale"&&(number<=0||number>100)||
                key is "inner_angle" or "outer_angle" or "flare_inner_angle"&&number>360||
                key=="blink_step_length"&&(number<=0||number>60))return false;
        }
        return true;
    }
    double RigidRadius(string path)
    {
        PathChecked(path,".pmd");var model=load(path).Model;
        if(model.Skeleton.Count!=0||model.Parts.Count==0||model.Parts.Any(p=>p.Locators.Count!=0)||
            model.Parts.SelectMany(p=>p.Pieces).Any(p=>p.UseBoneIndexes||p.UseBoneWeights))throw new InvalidDataException("Zusammengesetztes oder animiertes Passagiermodell");
        var positions=model.Parts.SelectMany(p=>p.Pieces).SelectMany(p=>p.Vertices).Select(v=>(double)v.Position.Length()).ToArray();
        if(positions.Length==0||positions.Any(p=>!double.IsFinite(p)))throw new InvalidDataException("Ungültige Passagiergeometrie");
        double radius=positions.Max();
        try {using var animation=files.Open(Path.ChangeExtension(path,".pma"));throw new InvalidDataException("Animiertes Passagiermodell");}catch(FileNotFoundException){ }
        try {radius=Math.Max(radius,PmcEnvelope.Radius(files.ReadAllBytes(Path.ChangeExtension(path,".pmc"))));}catch(FileNotFoundException){ }
        return radius;
    }
    Unit[] Units(string path)=>SiiFile.Open(PathChecked(path,".sii"),files).Units.ToArray();
    static string PathChecked(string path,string extension){if(!path.StartsWith('/')||!path.EndsWith(extension,StringComparison.Ordinal)||path.Contains("..")||path.Contains('\\'))throw new InvalidDataException("Ungültiger Pkw-Dateipfad");return path;}
    static string Text(Unit unit,string name)=>unit.Attributes.TryGetValue(name,out var value)?(object?)value switch {
        string s=>s,Token t=>t.ToString(),LinkPointer p=>p.Value,_=>throw new InvalidDataException("Unbekannter Pkw-Wert: "+name)}:"";
    static string[] Strings(Unit unit,string name)=>!unit.Attributes.TryGetValue(name,out var value)?[]:
        (object?)value is IEnumerable list&&(object?)value is not string?list.Cast<object>().Select(v=>v switch {
            string s=>s,Token t=>t.ToString(),OwnerPointer p=>p.Value,LinkPointer p=>p.Value,_=>throw new InvalidDataException("Unbekannte Pkw-Liste")}).ToArray():throw new InvalidDataException("Ungültige Pkw-Liste");
}
