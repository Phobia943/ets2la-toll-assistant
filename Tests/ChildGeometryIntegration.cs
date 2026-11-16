using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.Models;
using TruckLib.Models.Ppd;
using TruckLib.Sii;

public static class ChildGeometryIntegration
{
    const string MaterialPath="/fixtures/glow.mat";
    const string FlarePath="/fixtures/flare.pmd";
    const string FlareMaterial="""
    effect : "eut2.flare.rfx" {
      diffuse : { 1.000000, 1.000000, 1.000000 }
      luminance_night : 10.000000
      luminance_output : 1000.000000
      texture : "texture_base" { source : "/fixtures/glow.tobj" sampler : default }
    }
    """;

    static Part Beam(string name,float height,float halfWidth=2)=>new(name) {Pieces=[new() {
        Vertices=[new(new Vector3(-halfWidth,height,-.1f)),new(new Vector3(halfWidth,height,-.1f)),
            new(new Vector3(halfWidth,height,.1f)),new(new Vector3(-halfWidth,height,.1f))],
        Triangles=[new(0,1,2),new(0,2,3)]}]};
    static Model ModelOf(params Part[] parts)=>new() {Parts=[..parts],
        Variants=[new("default") {Attributes=parts.Select(_=>new PartAttribute {Tag="visible",Value=1}).ToList()}],
        Looks=[new("default") {Materials=[MaterialPath]}]};
    static Locator Place(Vector3 position,float scale=1,Quaternion? rotation=null,int hook=-1)=>new() {
        Position=position,Scale=scale,Rotation=rotation??Quaternion.Identity,HookupOffset=hook};
    static Unit Lod(string name,Files files,params string[] paths)=>SiiFile.Load(
        "SiiNunit { lod_model_hookup : "+name+" { "+string.Join(" ",paths.Select(p=>"model[]: \""+p+"\""))+" } }","/",files).Units.Single();
    static byte[] Hook(string name)
    {
        byte[] text=Encoding.ASCII.GetBytes(name+"\0"),bytes=new byte[96+text.Length];bytes[0]=21;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(88),96);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(92),text.Length);text.CopyTo(bytes,96);return bytes;
    }
    static PrefabDescriptor Lanes()=>new() {NavCurves=new[]{0f,6f}.Select(x=>new NavCurve {
        StartPosition=new(x,0,0),EndPosition=new(x,0,20),Length=20,
        StartRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
        EndRotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),SemaphoreId=-1}).ToList()};
    static bool Rejected(Action action)
    {
        try {action();return false;}catch(InvalidDataException){return true;}
    }

    public static void LodsAndTransforms(Action<bool,string> assert)
    {
        var files=new Files();var high=Beam("far_high",7.5f);var low=Beam("optional_low",2.5f);
        var far=ModelOf(high,low);far.Variants[0].Attributes[1].Value=0;
        far.Variants.Add(new("other") {Attributes=[new(){Tag="visible",Value=0},new(){Tag="visible",Value=1}]});
        var near=ModelOf(Beam("near_high",7.5f));
        var units=new Dictionary<string,Unit>{{"fx.root",Lod("fx.root",files,"/fixtures/near.pmd","/fixtures/far.pmd")}};
        var loaded=new List<string>();
        var reader=new StaticHookupGeometry(files,units,new HashSet<string>(),path=> {
            loaded.Add(path);return(path.EndsWith("near.pmd",StringComparison.Ordinal)?near:far,Hook(""));
        });
        var children=reader.Read("fx.root",Place(new(0,0,10)));
        assert(loaded.SequenceEqual(new[]{"/fixtures/near.pmd","/fixtures/far.pmd"})&&children.Any(p=>ReferenceEquals(p.Part,low)),
            "A different LOD and a non-default potentially visible variant cannot conceal a low child beam");
        assert(ModelClearance.BlockedCurves(Lanes(),[],children:children).SequenceEqual(new[]{true,false}),
            "The actually resolved low child blocks its intersecting lane rather than being treated as a harmless hookup");

        var rootPart=Beam("root_floor",-.1f,.5f);
        rootPart.Locators.Add(Place(new(-5,0,0),rotation:Quaternion.CreateFromAxisAngle(Vector3.UnitY,.31f),hook:0));
        var childPart=Beam("nested_low",1.25f,.5f);
        var rootModel=ModelOf(rootPart);var childModel=ModelOf(childPart);
        units=new(){{"fx.root",Lod("fx.root",files,"/fixtures/root.pmd")},{"fx.child",Lod("fx.child",files,"/fixtures/child.pmd")}};
        var nested=new StaticHookupGeometry(files,units,new HashSet<string>(),path=>
            path=="/fixtures/root.pmd"?(rootModel,Hook("fx.child")):(childModel,Hook("")));
        var parent=Place(new(6,0,0),2,Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2));
        children=nested.Read("fx.root",parent);
        var result=children.Single(p=>ReferenceEquals(p.Part,childPart));
        Vector3 transformed=Vector3.Transform(new(0,1.25f,0),result.Transform);
        assert(Vector3.Distance(transformed,new(6,2.5f,10))<.0001f,
            "Nested local translation and quaternion rotation compose before parent scale, rotation and translation");
        assert(ModelClearance.BlockedCurves(Lanes(),[],children:children).SequenceEqual(new[]{false,true}),
            "A scaled and rotated nested beam blocks the world-space lane it actually occupies");
    }

    public static void UnsupportedChildren(Action<bool,string> assert)
    {
        var files=new Files();var part=Beam("cycle",2);part.Locators.Add(Place(Vector3.Zero,hook:0));
        var model=ModelOf(part);var unit=Lod("fx.loop",files,"/fixtures/loop.pmd");
        var reader=new StaticHookupGeometry(files,new Dictionary<string,Unit>{{unit.Name,unit}},new HashSet<string>(),_ =>(model,Hook("fx.loop")));
        assert(Rejected(()=>reader.Read(unit.Name,Place(Vector3.Zero))),"A cyclic child model must fail closed rather than grant clearance");
        int loads=0;var parked=new Unit("parked_vehicle","fx.car");
        reader=new(files,new Dictionary<string,Unit>{{parked.Name,parked}},new HashSet<string>(),_=>{loads++;return(model,Hook(""));});
        assert(Rejected(()=>reader.Read(parked.Name,Place(Vector3.Zero)))&&loads==0,
            "An unknown child class such as parked_vehicle cannot be reinterpreted as a static harmless attachment");
        var unsupported=Lod("fx.unknown",files,"/fixtures/low.pmd");unsupported.Attributes["collision"]="/fixtures/hidden.pmc";
        reader=new(files,new Dictionary<string,Unit>{{unsupported.Name,unsupported}},new HashSet<string>(),_=>(model,Hook("")));
        assert(Rejected(()=>reader.Read(unsupported.Name,Place(Vector3.Zero))),"An unrecognized child-definition field cannot hide additional geometry");
        var valid=Lod("fx.valid",files,"/fixtures/valid.pmd");
        reader=new(files,new Dictionary<string,Unit>{{valid.Name,valid}},new HashSet<string>(),_=>(ModelOf(Beam("beam",2)),Hook("")));
        assert(Rejected(()=>reader.Read(valid.Name,Place(Vector3.Zero,float.NaN)))&&
            Rejected(()=>reader.Read(valid.Name,Place(Vector3.Zero,rotation:new Quaternion(0,0,0,0)))),
            "Invalid scale or quaternion cannot make a child disappear from the clearance check");
        var traversal=Lod("fx.path",files,"/fixtures/../hidden.pmd");
        reader=new(files,new Dictionary<string,Unit>{{traversal.Name,traversal}},new HashSet<string>(),_=>(model,Hook("")));
        assert(Rejected(()=>reader.Read(traversal.Name,Place(Vector3.Zero))),"Unverified traversal paths are not geometry evidence");
    }

    static Unit Flare()
    {
        var unit=new Unit("flare_lamp","fx.arbitrary_name");unit.Attributes["model"]=FlarePath;
        unit.Attributes["dir_type"]=(TruckLib.Token)"all";unit.Attributes["scale_factor"]=2;return unit;
    }
    static Model FlareModel()=>ModelOf(Beam("renderer",0,.25f));
    public static void FlareValidation(Action<bool,string> assert)
    {
        var files=new Files();files.Data[MaterialPath]=Encoding.UTF8.GetBytes(FlareMaterial);
        assert(FlareEvidence.Validate(Flare(),FlareModel(),FlarePath,files),
            "A fully inspected pure flare renderer is accepted by schema and actual material, not by its arbitrary hookup name");
        files.Data[MaterialPath]=Encoding.UTF8.GetBytes(FlareMaterial.Replace("sampler : default","u_address : clamp v_address : clamp"));
        assert(FlareEvidence.Validate(Flare(),FlareModel(),FlarePath,files),"Verified vehicle flare UV-clamp grammar remains a pure renderer");
        files.Data[MaterialPath]=Encoding.UTF8.GetBytes(FlareMaterial.Replace("sampler : default","u_address : clamp v_address : wrap"));
        assert(!FlareEvidence.Validate(Flare(),FlareModel(),FlarePath,files),"An uninspected texture grammar cannot be silently accepted");
        files.Data[MaterialPath]=Encoding.UTF8.GetBytes(FlareMaterial);
        var flare=Flare();var reader=new StaticHookupGeometry(files,new Dictionary<string,Unit>{{flare.Name,flare}},new HashSet<string>(),_=>(FlareModel(),Hook("")));
        assert(reader.Read(flare.Name,Place(Vector3.Zero)).Count==0,
            "The real child resolver uses the inspected renderer evidence instead of inventing solid billboard geometry");
        var generic=new Unit("flare","renderer.with_an_arbitrary_name");
        foreach(var attribute in flare.Attributes)generic.Attributes[attribute.Key]=attribute.Value;
        assert(FlareEvidence.Validate(generic,FlareModel(),FlarePath,files),
            "The generic flare class requires the same complete renderer evidence as flare_lamp");
        var genericReader=new StaticHookupGeometry(files,new Dictionary<string,Unit>{{generic.Name,generic}},new HashSet<string>(),_=>(FlareModel(),Hook("")));
        assert(genericReader.Read(generic.Name,Place(Vector3.Zero)).Count==0,
            "Generic flare dispatch uses inspected geometry and material without a hookup-name whitelist");
        generic.Attributes["blink_pattern"]="X-";
        assert(!FlareEvidence.Validate(generic,FlareModel(),FlarePath,files),"A generic flare cannot silently accept blink-only or unknown semantics");
        generic.Attributes.Remove("blink_pattern");
        generic.Attributes["collision"]="/fixtures/hidden.pmc";
        assert(!FlareEvidence.Validate(generic,FlareModel(),FlarePath,files),"An unknown physical-model field prevents the generic renderer exemption");
        generic.Attributes.Remove("collision");
        var fake=Flare();fake.Attributes["collision"]="/fixtures/hidden.pmc";
        assert(!FlareEvidence.Validate(fake,FlareModel(),FlarePath,files),"Unknown flare schema fields prohibit a renderer exemption");
        fake=Flare();fake.Attributes["dir_type"]=(TruckLib.Token)"angle";
        assert(!FlareEvidence.Validate(fake,FlareModel(),FlarePath,files),"Unsupported direction semantics cannot pass a pure-renderer proof");
        fake=Flare();fake.Attributes["scale_factor"]=double.PositiveInfinity;
        assert(!FlareEvidence.Validate(fake,FlareModel(),FlarePath,files),"Non-finite flare scale is rejected");
        foreach(string extension in new[]{".pmc",".pma"}) {
            string path=Path.ChangeExtension(FlarePath,extension);files.Data[path]=[1];
            assert(!FlareEvidence.Validate(Flare(),FlareModel(),FlarePath,files),"An actual collision or animation sidecar prohibits a renderer exemption: "+extension);
            assert(!FlareEvidence.Validate(generic,FlareModel(),FlarePath,files),"The generic flare class cannot hide a collision or animation sidecar: "+extension);
            files.Data.Remove(path);
        }
        foreach(string material in new[]{FlareMaterial.Replace("eut2.flare.rfx","eut2.dif",StringComparison.Ordinal),
            FlareMaterial+" effect : \"eut2.dif\" {}",FlareMaterial.Replace("1000.000000","1e999",StringComparison.Ordinal),
            FlareMaterial.Replace("/fixtures/glow.tobj","/fixtures/../hidden.tobj",StringComparison.Ordinal)}) {
            files.Data[MaterialPath]=Encoding.UTF8.GetBytes(material);
            assert(!FlareEvidence.Validate(Flare(),FlareModel(),FlarePath,files),
                "A solid/extra effect, non-finite material number or traversal texture path cannot hide in a flare-looking material");
            assert(!FlareEvidence.Validate(generic,FlareModel(),FlarePath,files),"The generic class still rejects a solid or uninspected material");
        }
        files.Data[MaterialPath]=Encoding.UTF8.GetBytes(FlareMaterial);
        var hidden=FlareModel();hidden.Parts[0].Locators.Add(Place(Vector3.Zero));
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"A child locator prevents classifying the entire model as a pure renderer");
        hidden=FlareModel();hidden.Parts[0].Pieces[0].UseBoneWeights=true;
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"Bone-weighted geometry is not a static pure flare model");
        hidden=FlareModel();hidden.Parts[0].Pieces[0].Material=1;
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"Out-of-range material indices cannot grant a renderer exemption");
        hidden=FlareModel();hidden.Parts[0].Pieces[0].Triangles[0]=new(0,1,99);
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"Malformed flare mesh indices cannot grant a renderer exemption");
        files.Data["/fixtures/solid.mat"]=Encoding.UTF8.GetBytes(FlareMaterial.Replace("eut2.flare.rfx","eut2.dif",StringComparison.Ordinal));
        hidden=FlareModel();hidden.Looks.Add(new("second") {Materials=["/fixtures/solid.mat"]});
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"A second material look is checked even when the default look is pure flare");
        hidden=FlareModel();hidden.Variants.Add(new("second") {Attributes=[new(){Tag="visible",Value=0}]});
        assert(!FlareEvidence.Validate(Flare(),hidden,FlarePath,files),"Unknown alternate flare visibility is not silently assumed harmless");
    }

    internal sealed class Files:IFileSystem
    {
        internal readonly Dictionary<string,byte[]> Data=new(StringComparer.Ordinal);
        public char DirectorySeparator=>'/';
        public bool FileExists(string path)=>Data.ContainsKey(path);
        public bool DirectoryExists(string path)=>false;
        public IList<string> GetFiles(string path)=>Data.Keys.Where(p=>p.StartsWith(path,StringComparison.Ordinal)).ToList();
        public byte[] ReadAllBytes(string path)=>Data.TryGetValue(path,out var data)?data:throw new FileNotFoundException(path);
        public string ReadAllText(string path)=>Encoding.UTF8.GetString(ReadAllBytes(path));
        public string ReadAllText(string path,Encoding encoding)=>encoding.GetString(ReadAllBytes(path));
        public Stream Open(string path)=>new MemoryStream(ReadAllBytes(path),false);
        public string GetParent(string path)=>Path.GetDirectoryName(path)!.Replace('\\','/');
    }
}
