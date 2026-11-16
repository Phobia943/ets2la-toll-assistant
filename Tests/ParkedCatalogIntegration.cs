using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib.Models;
using TruckLib.Sii;
public static class ParkedCatalogIntegration
{
    sealed class Fixture
    {
        internal readonly ChildGeometryIntegration.Files Files=new();
        internal readonly Dictionary<string,(Model Model,byte[] Pmg)> Models=new();
        internal readonly Dictionary<string,Unit> Hooks=new();
        internal void Text(string p,string text)=>Files.Data[p]=Encoding.UTF8.GetBytes(text);
        internal Fixture()
        {
            Text("/def/vehicle/traffic_vehicle_type.sii","SiiNunit { traffic_vehicle_type : traffic.vehicle_type.car { speed_class: car license_plate_type: car } }");
            Text("/def/vehicle/traffic_storage_car.sii","""
              SiiNunit {
                traffic_vehicle : traffic.test { accessories[]: .chassis accessories[]: .front accessories[]: .rear driver_model[]: "/driver.pmd" }
                vehicle_accessory : .chassis { data_path: "/chassis.sii" }
                vehicle_wheel_accessory : .front {
                  data_path: "/wheel.sii"
                  offset: 0
                }
                vehicle_wheel_accessory : .rear {
                  data_path: "/wheel.sii"
                  offset: 0
                }
              }
              """);
            Text("/chassis.sii","SiiNunit { accessory_chassis_data : .chassis { model: \"/car.pmd\" collision: \"/car.pmc\" variant: default variant_uk: uk } }");
            Text("/wheel.sii","SiiNunit { accessory_wheel_data : .w { model: \"/wheel.pmd\" } }");
            Files.Data["/car.pmc"]=Box();
            var part=Mesh("body",1);part.Locators=[Place("wheel_f",new(0,0,-1)),Place("wheel_r",new(0,0,1)),Place("driver",new(0,1,0))];
            Models["/car.pmd"]=(Model(part),Hook(""));Models["/wheel.pmd"]=(Model(Mesh("wheel",.4f)),Hook(""));Models["/driver.pmd"]=(Model(Mesh("driver",.8f)),Hook(""));
            Text("/def/vehicle/traffic_storage_car_custom.sii","unparseable different category must never be read");
        }
        internal double Radius()
        {
            var spawn=new Unit("parked_vehicle","arbitrary.spawn");spawn.Attributes["allowed_vehicle"]=new List<string>{"car"};spawn.Attributes["low_poly_only"]=false;
            spawn.Attributes["probability_day"]=.25f;
            return new ParkedSpawnCatalog(Files,p=>Models.TryGetValue(p,out var m)?m:throw new InvalidDataException("Missing fixture model"),Hooks,new HashSet<string>()).Radius(spawn);
        }
    }
    static Part Mesh(string name,float radius)=>new(name){Pieces=[new(){Vertices=[new(new Vector3(radius,0,0)),new(new Vector3(0,radius,0)),new(new Vector3(0,0,radius))],Triangles=[new(0,1,2)]}]};
    static Model Model(Part part)=>new(){Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]},new("uk"){Attributes=[new(){Tag="visible",Value=1}]}]};
    static Locator Place(string name,Vector3 point,int hook=-1)=>new(){Name=name,Position=point,Scale=1,Rotation=Quaternion.Identity,HookupOffset=hook};
    static byte[] Hook(string text) {var bytes=Encoding.ASCII.GetBytes(text+"\0");var pmg=new byte[96+bytes.Length];pmg[0]=21;BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),bytes.Length);bytes.CopyTo(pmg,96);return pmg;}
    static byte[] Box() {var b=new byte[120];void I(int p,int v)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p),v);void F(int p,float v)=>I(p,BitConverter.SingleToInt32Bits(v));I(0,6);I(8,1);I(36,40);I(40,48);I(48,1);I(52,64);F(96,1);F(100,2);F(104,2);F(108,2);I(116,-1);return b;}
    static bool Rejected(Action action){try{action();return false;}catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException or FileNotFoundException){return true;}}
    public static void Selection(Action<bool,string> assert)
    {
        var f=new Fixture();double baseline=f.Radius();assert(baseline>Math.Sqrt(12),"All physical body shapes contribute; a custom-category prefix does not pollute car selection");
        f.Models["/big.pmd"]=(Model(Mesh("big",12)),Hook(""));
        f.Models["/big.pmd"].Model.Parts[0].Locators=[Place("wheel_f",new(0,0,-1)),Place("wheel_r",new(0,0,1))];
        f.Text("/big.sii","SiiNunit { accessory_chassis_data : .b { model: \"/big.pmd\" collision: \"/car.pmc\" variant: default variant_uk: uk } }");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { accessories[]: .big accessories[]: .front accessories[]: .rear } vehicle_accessory : .big { data_path: \"/big.sii\" } }");
        assert(f.Radius()>12,"Same-category suffix catalogue contributes its larger possible vehicle");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { accessories[]: .missing } }");
        assert(Rejected(()=>f.Radius()),"An unresolved extension accessory cannot disappear");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { accessories[]: .missing trailer_chains[]: arbitrary } }");
        assert(Rejected(()=>f.Radius()),"Unaccounted trailers do not receive car-only bounds");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { allow_parked: false accessories[]: .missing trailer_chains[]: arbitrary } }");
        assert(f.Radius()==baseline,"An explicit engine parking exclusion removes that candidate");
        f.Text("/def/vehicle/traffic_storage_car.sii",f.Files.ReadAllText("/def/vehicle/traffic_storage_car.sii").Replace("driver_model[]:","variant[]: traffic.big driver_model[]:"));
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { allow_parked: false accessories[]: .big accessories[]: .front accessories[]: .rear } vehicle_accessory : .big { data_path: \"/big.sii\" } }");
        assert(f.Radius()>12,"A vehicle variant reference resolves a whole configuration, including a conservatively included parking-excluded target");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.big { variant[]: traffic.test } }");
        assert(Rejected(()=>f.Radius()),"Vehicle variant cycles cannot yield a catalogue bound");
        f.Text("/def/vehicle/traffic_storage_car.extra.sii","SiiNunit { traffic_vehicle : traffic.other {} }");
        assert(Rejected(()=>f.Radius()),"Missing vehicle variant targets are not PMD mask names");
        var wheelTransform=new Fixture();var loc=wheelTransform.Models["/car.pmd"].Model.Parts[0].Locators[0];
        wheelTransform.Text("/def/vehicle/traffic_storage_car.sii",wheelTransform.Files.ReadAllText("/def/vehicle/traffic_storage_car.sii").Replace("driver_model[]: \"/driver.pmd\"",""));
        loc.Scale=50;loc.Rotation=new(0,1.0049875f,0,0);
        assert(wheelTransform.Radius()>21.4,"The allowed small quaternion error cannot shrink a scaled wheel enclosure");
    }
    public static void Children(Action<bool,string> assert)
    {
        var f=new Fixture();var car=f.Models["/car.pmd"].Model;car.Parts[0].Locators.Add(Place("passenger",new(0,1,0),0));f.Models["/car.pmd"]=(car,Hook("pass.root"));
        var root=new Unit("passenger_hookup","pass.root");root.Attributes["hookup_variant"]=new List<LinkPointer>{new("pass.leaf")};f.Hooks[root.Name]=root;
        var leaf=new Unit("passenger_hookup","pass.leaf");leaf.Attributes["model"]=new List<string>{"/pass.pmd"};f.Hooks[leaf.Name]=leaf;
        f.Models["/pass.pmd"]=(Model(Mesh("pass",8)),Hook(""));
        assert(f.Radius()>9,"Every possible passenger variant and its actual attachment are enclosed");
        root.Attributes["hookup_variant"]=new List<LinkPointer>{new("pass.root")};assert(Rejected(()=>f.Radius()),"Cyclic passengers cannot yield a finite bound");
        root.Attributes["hookup_variant"]=new List<LinkPointer>{new("pass.leaf")};f.Models["/pass.pmd"].Model.Parts[0].Pieces[0].UseBoneWeights=true;
        assert(Rejected(()=>f.Radius()),"Unbounded animation cannot masquerade as a rigid passenger");
        f=new Fixture();car=f.Models["/car.pmd"].Model;
        var hidden=Mesh("hidden",1);hidden.Locators.Add(Place("other",Vector3.Zero,0));car.Parts.Add(hidden);
        foreach(var variant in car.Variants)variant.Attributes.Add(new(){Tag="visible",Value=0});
        f.Models["/car.pmd"]=(car,Hook("unknown.behavior"));
        assert(f.Radius()>0,"A child in an actually unselected variant cannot generate a false whole-catalogue rejection");
        car.Variants[0].Attributes[1].Value=1;assert(Rejected(()=>f.Radius()),"Visible unknown children remain unresolved");
    }
    public static void GeneratedLeaves(Action<bool,string> assert)
    {
        var camera=new Unit("speed_camera","new.camera");camera.Attributes["trigger_distance"]=8f;camera.Attributes["measurement_distance"]=0f;
        assert(TollModelEvidence.IsNonSolidHookup(camera),"A typed measurement trigger has no geometric child model");
        camera.Attributes["model"]="/hidden.pmd";assert(!TollModelEvidence.IsNonSolidHookup(camera),"Unknown camera geometry cannot be erased");camera.Attributes.Remove("model");
        camera.Attributes["measurement_distance"]=float.NaN;assert(!TollModelEvidence.IsNonSolidHookup(camera),"Malformed passive trigger metadata remains unresolved");
        var audio=new Unit("sound_hookup","new.audio");audio.Attributes["trigger_distance"]=700f;audio.Attributes["sound_path"]="/sound/world/warehouse.soundref";
        assert(TollModelEvidence.IsNonSolidHookup(audio),"A bounded typed audio trigger contains only an audio reference");
        foreach(string invalid in new[]{"/sound/../geometry.soundref","/sound//warehouse.soundref","/other/world/warehouse.soundref","/sound/world/warehouse.pmd","https://host/sound.soundref"}) {
            audio.Attributes["sound_path"]=invalid;assert(!TollModelEvidence.IsNonSolidHookup(audio),"Passive audio paths cannot name geometry, external URIs or traversal");}
        var flareFiles=new ChildGeometryIntegration.Files();const string renderedModel="/fixture/flare.pmd",renderedMaterial="/fixture/flare.mat";
        flareFiles.Data[renderedMaterial]=Encoding.UTF8.GetBytes("effect : \"eut2.flare.rfx\" { diffuse : { 1, 1, 1 } luminance_night : 10 luminance_output : 1000 texture : \"texture_base\" { source : \"/fixture/flare.tobj\" sampler : default } }");
        var renderer=Model(Mesh("render",.4f));renderer.Variants.RemoveAt(1);renderer.Looks=[new("default"){Materials=[renderedMaterial]}];
        var lamp=new Unit("flare_lamp","new.lamp");lamp.Attributes["model"]=renderedModel;lamp.Attributes["dir_type"]=(TruckLib.Token)"wide";
        assert(FlareEvidence.Validate(lamp,renderer,renderedModel,flareFiles),"Omitted scale and a typed wide angle retain the complete renderer/collision proof");
        lamp.Attributes["scale_factor"]="1";assert(!FlareEvidence.Validate(lamp,renderer,renderedModel,flareFiles),"An omitted default is distinct from a wrong typed scale");lamp.Attributes.Remove("scale_factor");
        var blink=new Unit("flare_blink","new.blink");blink.Attributes["model"]=renderedModel;blink.Attributes["dir_type"]=(TruckLib.Token)"all";
        blink.Attributes["blink_pattern"]="X--";blink.Attributes["blink_step_length"]=.5f;
        assert(FlareEvidence.Validate(blink,renderer,renderedModel,flareFiles),"Typed blinking changes brightness while the same full renderer proof is required");
        foreach(object invalid in new object[]{"",new string('X',65),"X;model","X\u007f",1}){blink.Attributes["blink_pattern"]=invalid;assert(!FlareEvidence.Validate(blink,renderer,renderedModel,flareFiles),"Blink grammar is bounded and cannot contain substituted fields or types");}
        blink.Attributes["blink_pattern"]="X--";blink.Attributes["blink_step_length"]=float.PositiveInfinity;assert(!FlareEvidence.Validate(blink,renderer,renderedModel,flareFiles),"Blink timing must remain finite");
        blink.Attributes["blink_step_length"]=.5f;flareFiles.Data["/fixture/flare.pmc"]=[1];assert(!FlareEvidence.Validate(blink,renderer,renderedModel,flareFiles),"Blink classes cannot bypass an actual collision sidecar");flareFiles.Data.Remove("/fixture/flare.pmc");
        lamp.Attributes["model"]="/uninspected.pmd";assert(!FlareEvidence.Validate(lamp,renderer,renderedModel,flareFiles),"A validated model instance cannot authorize a different model path");
        const string template="optical.source#height:4.93;target:4.93";
        var source=new Unit("light_source","optical.source");source.Attributes["range"]=33;
        source.Attributes["cut_range"]=11;source.Attributes["cut_direction"]=(TruckLib.Token)"vertical";
        source.Attributes["fade_distance"]=40;source.Attributes["fade_span"]=60;
        source.Attributes["specular_color"]=(6500,46,22);source.Attributes["inner_angle"]=5f;source.Attributes["outer_angle"]=130f;
        var lightUnits=new Dictionary<string,Unit>{{source.Name,source}};
        assert(TollModelEvidence.IsNonSolidReference(template,new HashSet<string>(),lightUnits),"Long parameterized lamps are classified by exact optical schema, not a truncation or a name prefix");
        foreach(string invalid in new[]{"#height:4;model:4","#height:4;height:4","#height:NaN;target:4","#height:1e99;target:4","#height:4,93;target:4","#height:4;target:4;extra:4","#height:4#target:4"})
            assert(!TollModelEvidence.IsNonSolidReference("optical.source"+invalid,new HashSet<string>(),lightUnits),"Unknown, repeated, nonfinite or malformed optical parameters remain unresolved");
        source.Attributes["model"]="/hidden.pmd";assert(!TollModelEvidence.IsNonSolidReference(template,new HashSet<string>(),lightUnits),"A geometric field prevents treating the full template as a light source");source.Attributes.Remove("model");
        source.Attributes["range"]="33";assert(!TollModelEvidence.IsNonSolidReference(template,new HashSet<string>(),lightUnits),"Optical values require real field types");source.Attributes["range"]=33;
        string longReference="template.light.lamp.n.normal.fadelong.deg130#height:4.93;target:4.93";
        assert(TollModelEvidence.ReadHookup(Hook(longReference),0)==longReference,"The actual 68-byte French lamp reference is preserved intact");
        assert(TollModelEvidence.ReadHookup(Hook(new string('a',257)),0)==null&&TollModelEvidence.ReadHookup(Hook("text\u007f"),0)==null,"Overlong or nonprintable hookup tables cannot hide arbitrary data");
        var transformed=Place("sweep",Vector3.Zero);transformed.Rotation=Quaternion.CreateFromYawPitchRoll(.7f,.5f,.3f);
        assert(Math.Abs(StaticHookupGeometry.RadialScale(transformed)-1)<.00001,"A unit rotation cannot inflate an animated sphere by a generic matrix norm");
        transformed.Scale=2;double firstScale=StaticHookupGeometry.RadialScale(transformed);
        var nested=Place("nested",Vector3.Zero);nested.Rotation=Quaternion.CreateFromYawPitchRoll(-.3f,.8f,-.6f);nested.Scale=3;
        double combinedScale=firstScale*StaticHookupGeometry.RadialScale(nested);
        assert(combinedScale<6.0001,"Nested unit rotations retain only their actual uniform scale product");
        var matrix=StaticHookupGeometry.Transform(nested)*StaticHookupGeometry.Transform(transformed);
        foreach(var point in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,Vector3.Normalize(new Vector3(.2f,.7f,.4f))})
            assert(Vector3.TransformNormal(point,matrix).Length()<=combinedScale,"The scalar chain encloses every independently transformed sampled unit vector");
        transformed.Scale=1;transformed.Rotation=new(0,1.0049875f,0,0);
        double padded=StaticHookupGeometry.RadialScale(transformed);
        foreach(var point in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ})assert(Vector3.TransformNormal(point,StaticHookupGeometry.Transform(transformed)).Length()<=padded,"Accepted nonunit quaternion roundoff remains enclosed");
        foreach(var rotation in new[]{Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),new Quaternion(0,MathF.Sqrt(.990001f),0,0),new Quaternion(0,MathF.Sqrt(1.009999f),0,0)}) {
            transformed.Rotation=rotation;double bound=StaticHookupGeometry.RadialScale(transformed);
            for(int i=0;i<80;i++) {var point=Vector3.Normalize(new Vector3(MathF.Cos(i*.7f),MathF.Sin(i*.7f),MathF.Sin(i*.3f)));
                assert(Vector3.TransformNormal(point,StaticHookupGeometry.Transform(transformed)).Length()<=bound,"Both quaternion norm tolerance boundaries and quarter turns retain every sampled sphere point");}
        }
        var physical=new Unit("parked_vehicle","unfamiliar.physical.spawn");
        physical.Attributes["allowed_vehicle"]=new List<TruckLib.Token>{"truck"};
        physical.Attributes["low_poly_only"]=false;physical.Attributes["probability_day"]=.25f;physical.Attributes["probability_night"]=0;
        assert(StaticHookupGeometry.IsDynamicParked(physical),"Typed physical truck spawns require current scene evidence, not a car-only catalogue");
        var reader=new StaticHookupGeometry(new ChildGeometryIntegration.Files(),new Dictionary<string,Unit>{{physical.Name,physical}},new HashSet<string>(),_=>throw new Exception("Spawn must not resolve a static candidate model"));
        reader.BeginInspection();assert(reader.Read(physical.Name,Place("spawn",Vector3.Zero)).Count==0&&reader.HasDynamicParked,
            "The static result preserves an explicit dynamic requirement instead of pretending the spawn is solid or empty");
        reader.BeginInspection();assert(!reader.HasDynamicParked,"Dynamic flags are local to each inspected model");
        physical.Attributes.Remove("probability_day");physical.Attributes.Remove("probability_night");
        physical.Attributes["always_visible"]=true;
        assert(StaticHookupGeometry.IsDynamicParked(physical)&&reader.Read(physical.Name,Place("spawn",Vector3.Zero)).Count==0&&reader.HasDynamicParked,
            "Omitted optional spawn probability retains the dynamic scene requirement and cannot stand for an absent vehicle");
        physical.Attributes["always_visible"]="true";assert(!StaticHookupGeometry.IsDynamicParked(physical),"Visibility metadata cannot contain a substituted value type");physical.Attributes["always_visible"]=true;
        physical.Attributes["probability_day"]=.25f;physical.Attributes["probability_night"]=0;
        foreach(object invalid in new object[]{true,"false",0}) {physical.Attributes["low_poly_only"]=invalid;assert(!StaticHookupGeometry.IsDynamicParked(physical),"Unproved static/low-poly spawns cannot be skipped");}
        physical.Attributes["low_poly_only"]=false;
        foreach(object invalid in new object[]{float.NaN,float.PositiveInfinity,"0.25",-1,2}) {physical.Attributes["probability_day"]=invalid;assert(!StaticHookupGeometry.IsDynamicParked(physical),"Probability fields require finite typed values");}
        physical.Attributes["probability_day"]=.25f;
        foreach(string field in new[]{"model","collision","allowed_trailer","unknown"}) {physical.Attributes[field]="/hidden.pmd";assert(!StaticHookupGeometry.IsDynamicParked(physical),"Unknown fields cannot conceal unobserved physical behavior");physical.Attributes.Remove(field);}
        foreach(object invalid in new object[]{"truck",new List<string>{"bus"},new List<string>{"car","car"},new List<object>{new LinkPointer("truck")}}) {physical.Attributes["allowed_vehicle"]=invalid;assert(!StaticHookupGeometry.IsDynamicParked(physical),"Vehicle filters require supported exact typed categories");}
        physical.Attributes["allowed_vehicle"]=new List<string>{"car","truck"};assert(StaticHookupGeometry.IsDynamicParked(physical),"Both supported physical categories retain the same current-scene requirement");
        var plate=new Unit("license_plate","unfamiliar.name");assert(ParkedSpawnCatalog.IsGeneratedVisual(plate),"Typed generated plate requires no fixed model");
        plate.Attributes["rear_plate"]=true;assert(ParkedSpawnCatalog.IsGeneratedVisual(plate),"Known bool option remains render metadata");
        plate.Attributes["rear_plate"]="true";assert(!ParkedSpawnCatalog.IsGeneratedVisual(plate),"Wrong field type is not silently parsed");
        foreach(string field in new[]{"model","collision","variant","unknown"}) {plate.Attributes.Clear();plate.Attributes[field]="/hidden.pmd";assert(!ParkedSpawnCatalog.IsGeneratedVisual(plate),"An arbitrary geometric field cannot hide in a generated visual");}
        assert(!ParkedSpawnCatalog.IsGeneratedVisual(new Unit("parked_vehicle","license.plate")),"A known hook name cannot authorize a different class");
        var particles=new Unit("wheel_particles","new.particles");assert(ParkedSpawnCatalog.IsGeneratedVisual(particles),"Empty typed wheel effect is supported");particles.Attributes["model"]="/hidden.pmd";assert(!ParkedSpawnCatalog.IsGeneratedVisual(particles),"Wheel effects cannot hide child geometry");
        var flare=new Unit("flare_vehicle","new.flare");flare.Attributes["light_type"]=(TruckLib.Token)"brake";flare.Attributes["type"]=(TruckLib.Token)"spot";
        flare.Attributes["setup"]=new LinkPointer("candela_hue_saturation");flare.Attributes["scale_factor"]=3f;
        assert(ParkedSpawnCatalog.VehicleFlareFields(flare),"Known light-only fields are supported without inventing a model");
        foreach(object invalid in new object[]{float.NaN,float.PositiveInfinity,"3",-1,1000}){flare.Attributes["scale_factor"]=invalid;assert(!ParkedSpawnCatalog.VehicleFlareFields(flare),"Normalization cannot hide invalid original scale fields");}
        flare.Attributes["scale_factor"]=3f;flare.Attributes["forward_distance"]="false";assert(!ParkedSpawnCatalog.VehicleFlareFields(flare),"Render bools require actual bool values");
    }
}
