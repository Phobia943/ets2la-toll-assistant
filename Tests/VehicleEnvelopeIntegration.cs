using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.Models;

public static class VehicleEnvelopeIntegration
{
    sealed class Files: IFileSystem
    {
        internal readonly Dictionary<string,string> Text=new();
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>Text.ContainsKey(p);public bool DirectoryExists(string p)=>true;
        public IList<string> GetFiles(string p)=>Text.Keys.Where(k=>k.StartsWith(p)).ToList();
        public byte[] ReadAllBytes(string p)=>Encoding.UTF8.GetBytes(ReadAllText(p));
        public string ReadAllText(string p)=>Text.TryGetValue(p,out var s)?s:throw new FileNotFoundException(p);
        public string ReadAllText(string p,Encoding e)=>ReadAllText(p);
        public Stream Open(string p)=>new MemoryStream(ReadAllBytes(p));public string GetParent(string p)=>"/";
    }
    static VehicleBody Body(Vector3? position=null,Quaternion? rotation=null)=>new(position??Vector3.Zero,rotation??Quaternion.Identity,
        new(-1.54f,.25f,-3.4f),new(1.51f,3.87f,4.26f));
    static VehicleGeometry Shape(long now=10000)=>new(1,2,3,now,Body(),true,Body(new(0,0,8)));
    static EnvelopeContext Context(long now=10000)=>new("truck/trailer/cargo/job",true,false,
        [new("tank",".def.vehicle.cargo.fluid.aux","tank","single","plate",new(0,0,8))]){UtcMs=now};
    static RouteTrace Road(float y=0)=>new(new[]{new Vector3(0,y,-30),new Vector3(0,y,30)});
    static bool Rejected(Action action){try{action();return false;}catch(InvalidDataException){return true;}}
    public static void Frames(Action<bool,string> assert)
    {
        var shape=Shape();var context=Context();
        var proof=new VehicleEnvelopeEvidence.Proof(4.4f,3.4f,"fixture local bounds",null)
            {TrailerLow=shape.Trailer!.Minimum,TrailerHigh=shape.Trailer.Maximum};
        var flat=VehicleEnvelopeEvidence.ProfileFor(proof,shape,Road());
        assert(flat==(4.4f,3.4f),"Regular local boxes must retain explicit reserves");
        assert(Rejected(()=>VehicleEnvelopeEvidence.ProfileFor(proof,shape with{Truck=Body(new(0,1,0))},Road())),
            "A raised model origin is not assumed to sit on the road");
        assert(Rejected(()=>VehicleEnvelopeEvidence.ProfileFor(proof,shape with{Trailer=Body(new(0,1,8))},Road())),
            "Trailer ground height is checked separately from the tractor");
        var rolled=shape with{Truck=Body(rotation:Quaternion.CreateFromAxisAngle(Vector3.UnitZ,.12f))};
        assert(VehicleEnvelopeEvidence.ProfileFor(proof,rolled,Road()).Width>flat.Width,"Roll expands true world width");
        var pitched=shape with{Trailer=Body(new(0,0,8),Quaternion.CreateFromAxisAngle(Vector3.UnitX,.12f))};
        assert(VehicleEnvelopeEvidence.ProfileFor(proof,pitched,Road()).Height>flat.Height,"Trailer pitch raises transformed corners");
        var crossing=new RouteTrace(new[]{new Vector3(0,0,-30),new Vector3(0,0,30),new Vector3(20,4,30),new Vector3(20,4,-30),new Vector3(0,4,-30),new Vector3(0,4,30)});
        assert(VehicleEnvelopeEvidence.ProfileFor(proof,pitched,crossing).Height==VehicleEnvelopeEvidence.ProfileFor(proof,pitched,Road()).Height,
            "An upper deck near a roof cannot replace the local directed ground below the body");
        var yawed=shape with{Trailer=Body(new(0,0,8),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.13f))};
        assert(VehicleEnvelopeEvidence.ProfileFor(proof,yawed,Road()).Width>flat.Width,"Trailer articulation expands the corridor relative to the road, not its own axes");
        assert(Rejected(()=>VehicleEnvelopeEvidence.ProfileFor(proof,shape with{Trailer=Body(new(0,0,8),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.26f))},Road())),
            "A strongly articulated rig cannot retain a narrow-road permission");
        assert(VehicleEnvelopeEvidence.ProfileFor(proof,shape with{Truck=Body(new(.5f,0,0))},Road()).Width>flat.Width,
            "Lateral root displacement consumes road-centred clearance");
        assert(Rejected(()=>VehicleEnvelopeEvidence.ProfileFor(proof,shape,null)),"No road reference means no smaller clearance permission");
        var oldPoints=Enumerable.Range(-30,91).Select(z=>new Vector3(0,0,z)).ToArray();
        var oldRoad=new RouteTrace(oldPoints,[],Enumerable.Repeat("verified directed lane",oldPoints.Length-1));
        var newPoints=oldPoints.Where(p=>p.Z>=-2).ToArray();
        var pruned=new RouteTrace(newPoints,[],Enumerable.Repeat("verified directed lane",newPoints.Length-1));
        var behindShape=shape with{Trailer=shape.Trailer! with{Position=new(0,0,-8)}};
        assert(Rejected(()=>VehicleEnvelopeEvidence.ProfileFor(proof,behindShape,pruned)),"A pruned route alone does not cover the rear of the rig");
        var withTail=pruned.WithVerifiedTail(oldRoad,Vector3.Zero);
        assert(withTail!=null&&VehicleEnvelopeEvidence.ProfileFor(proof,behindShape,withTail)==VehicleEnvelopeEvidence.ProfileFor(proof,behindShape,oldRoad),
            "Exactly compatible pruning retains bounded verified road geometry, not the last profile number");
        var different=new RouteTrace(newPoints,[],Enumerable.Repeat("different lane",newPoints.Length-1));
        assert(different.WithVerifiedTail(oldRoad,Vector3.Zero)==null,"Another lane identity cannot borrow an old tail");
        var otherLevel=new RouteTrace(newPoints.Select(p=>p+Vector3.UnitY*4),[],Enumerable.Repeat("verified directed lane",newPoints.Length-1));
        assert(otherLevel.WithVerifiedTail(oldRoad,new(0,4,0))==null,"Another road level cannot borrow an old tail");
        assert(VehicleEnvelopeEvidence.Coherent(context,shape,Vector3.Zero,0,0,10000),"Same-pose measurement is coherent");
        assert(!VehicleEnvelopeEvidence.Coherent(context,shape,Vector3.Zero,0,.5,10000),"Opposite heading cannot reuse dimensions");
        assert(!VehicleEnvelopeEvidence.Coherent(context,shape with{Truck=Body(new(2,0,0))},Vector3.Zero,0,0,10000),"Position mismatches are rejected");
        assert(!VehicleEnvelopeEvidence.Coherent(context,shape with{Trailer=Body(new(0,0,10))},Vector3.Zero,0,0,10000),"Trailer pose mismatches are rejected");
        assert(!VehicleEnvelopeEvidence.Coherent(context,shape,Vector3.Zero,0,0,10301),"Cache permission does not extend source freshness");
        assert(VehicleEnvelopeEvidence.Key(context,shape)!=VehicleEnvelopeEvidence.Key(context with{Identity="new cargo"},shape),"Cargo change invalidates proof identity");
        assert(VehicleEnvelopeEvidence.Key(context,shape)!=VehicleEnvelopeEvidence.Key(context,shape with{Session=3}),"Process/session change invalidates proof identity");
        assert(VehicleEnvelopeEvidence.Key(context,shape)!=VehicleEnvelopeEvidence.Key(context,shape with{Truck=shape.Truck with{Maximum=new(2,5,5)}}),"Larger live body invalidates proof identity");
    }
    public static void CacheAuthority(Action<bool,string> assert)
    {
        long now=10000;int inspected=0;IFileSystem files=new Files();
        var evidence=new VehicleEnvelopeEvidence(()=>files,()=>now,(f,c,s)=>{
            Interlocked.Increment(ref inspected);return new(4.4f,3.4f,"fixture",null){TrailerLow=s.Trailer!.Minimum,TrailerHigh=s.Trailer.Maximum};});
        (float Height,float Width) Read(EnvelopeContext? c=null,VehicleGeometry? s=null,double speed=0)=>
            evidence.Read(c??Context(now),s??Shape(now),Vector3.Zero,speed,0,Road());
        assert(float.IsNaN(Read().Height),"No immediate assumed proof");now+=800;
        Read();assert(SpinWait.SpinUntil(()=>Volatile.Read(ref inspected)==1,2000),"One stable stopped proof is inspected");
        assert(SpinWait.SpinUntil(()=>float.IsFinite(Read().Height),2000),"Completed proof becomes usable with current pose");
        assert(float.IsNaN(Read(s:Shape(now) with{Truck=Body(new(3,0,0))}).Height),"Cache hits recheck live pose");
        assert(float.IsNaN(Read(c:Context(now) with{Identity="changed"},speed:20).Height),"Changed cargo cannot acquire a smaller proof while driving");
        assert(float.IsNaN(Read(s:Shape(now) with{Session=99},speed:20).Height),"New game session needs a new stable proof");
        files=new Files();assert(float.IsNaN(Read().Height),"Source archive replacement invalidates old proof");
    }
    public static void Auxiliary(Action<bool,string> assert)
    {
        var files=new Files();
        files.Text["/def/vehicle/cargo/fluid/aux.sii"]="SiiNunit { accessory_cargo_data : .aux { model: \"\" aux_models[]: \"/cargo/sign.pmd\" aux_models_locators[]: \"sign\" } }";
        files.Text["/def/vehicle/trailer/tank.sui"]="trailer : trailer.tank { accessories[]: .chassis accessories[]: .body } vehicle_accessory : .chassis { data_path: \"/chassis.sii\" } vehicle_accessory : .body { data_path: \"/body.sii\" }";
        files.Text["/chassis.sii"]="SiiNunit { accessory_chassis_data : .c { model: \"/chassis.pmd\" variant: default } }";
        files.Text["/body.sii"]="SiiNunit { accessory_trailer_body_data : .b { cargo_loading_methods[]: auxiliary_only } }";
        var anchor=new Locator{Name="sign",Position=new(0,2,0),Rotation=Quaternion.Identity,Scale=1,HookupOffset=-1};
        var basePart=new Part("base"){Locators=[anchor]};
        var signPart=new Part("sign"){Pieces=[new(){Vertices=[new(new Vector3(0,0,0)),new(new Vector3(0,3,0)),new(new Vector3(.2f,0,0))],Triangles=[new(0,1,2)]}]};
        Model ModelOf(Part part)=>new(){Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        Model Load(string path)=>ModelOf(path=="/chassis.pmd"?basePart:signPart);
        var native=Body();
        (Vector3 Minimum,Vector3 Maximum) Expand()=>AuxiliaryCargoEnvelope.Expand(files,"tank",".def.vehicle.cargo.fluid.aux",native,Load);
        assert(Expand().Maximum.Y==5,"Actual transformed auxiliary cargo expands beyond the base trailer box");
        anchor.Position=new(0,8,0);assert(Rejected(()=>Expand()),"Unmatched attachment frame cannot grant clearance");anchor.Position=new(0,2,0);
        string original=files.Text["/def/vehicle/trailer/tank.sui"];
        files.Text["/def/vehicle/trailer/tank.sui"]=original.Replace("accessories[]: .body","accessories[]: .body accessories[]: .missing");
        assert(Rejected(()=>Expand()),"Unresolved accessory cannot silently disappear");
        files.Text["/def/vehicle/trailer/tank.sui"]=original.Replace("accessories[]: .body","accessories[]: .body accessories[]: .extra")+" unknown_accessory : .extra { model: \"/huge.pmd\" }";
        assert(Rejected(()=>Expand()),"Unknown attached model cannot silently disappear");
        files.Text["/def/vehicle/trailer/tank.sui"]=original;
        files.Text["/def/vehicle/cargo/fluid/aux.sii"]=files.Text["/def/vehicle/cargo/fluid/aux.sii"].Replace("model: \"\"","model: \"/load.pmd\"");
        assert(Rejected(()=>Expand()),"A main cargo mesh needs its own complete proof");
    }
}
