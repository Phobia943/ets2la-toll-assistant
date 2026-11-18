using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.Models;
using TruckLib.ScsMap;
using Model=TruckLib.Models.Model;

internal static class StaticRoadSignReviewTests
{
    static string Unit(string id="pole",string extra="",string path="/pole.pmd")=>
        "sign_model : sign."+id+" { model_desc: \""+path+"\" model_coll: \"/pole.pmc\" dynamic: false "+extra+" }";
    internal static void Registry(Action<bool,string> check)
    {
        var parsed=StaticRoadSignRegistry.Parse("SiiNunit { # sign_model: sign.fake{}\n"+Unit(extra:"category: \"{ model_desc: fake }\" variant_name: default")+" }");
        check(parsed["pole"]==new StaticRoadSignRegistry.Definition("/pole.pmd","/pole.pmc","default"),"Static sign registry retains exact model/collision/default-variant binding");
        foreach(string extra in new[]{"dynamic: true","dynamic: false","model_coll: \"/other.pmc\"","editable: true","model_anim: \"/animated.pma\"","future_physics: true","variant_name: Default"})
            check(StaticRoadSignRegistry.Parse("SiiNunit {"+Unit(extra:extra)+" }")["pole"]==null,"Duplicate, dynamic, editable or unknown physics attributes cannot authorize a fixed sign: "+extra);
        check(StaticRoadSignRegistry.Parse("SiiNunit {"+Unit()+Unit(path:"/different.pmd")+Unit()+" }")["pole"]==null,"Conflicting duplicate sign definition remains unknown after a matching third unit");
        check(StaticRoadSignRegistry.Parse("SiiNunit {"+Unit(path:"/../pole.pmd")+" }")["pole"]==null,"Noncanonical sign asset path rejected");
        foreach(string malformed in new[]{"SiiNunit { @include \"absent.sui\" }","SiiNunit {"+Unit(),"SiiNunit { sign_model : sign.a { dynamic: } }"}){
            bool rejected=false;try{StaticRoadSignRegistry.Parse(malformed);}catch(InvalidDataException){rejected=true;}
            check(rejected,"Incomplete/directive sign registry cannot silently publish a partial catalog");
        }
        var files=new CatalogFiles();files.Text["/def/world/sign.sii"]="SiiNunit {"+Unit()+"}";
        files.Text["/def/world/sign.custom.sii"]="SiiNunit {\n# Backward compatibility only\n@include \"/custom/sign.sii\"\n}";
        check(StaticRoadSignRegistry.ReadCatalog(files)["pole"]!=null,"Only the exact stock legacy wrapper permits its explicitly absent optional target");
        files.Text["/custom/sign.sii"]=Unit(path:"/changed.pmd");
        check(StaticRoadSignRegistry.ReadCatalog(files)["pole"]==null,"Existing custom include is read completely and its conflicting override invalidates the base sign");
        files.Fail="/custom/sign.sii";bool failed=false;try{StaticRoadSignRegistry.ReadCatalog(files);}catch(IOException){failed=true;}
        check(failed,"Transient legacy include IO cannot silently return the otherwise valid base catalog");files.Fail=null;
        files.Text.Remove("/custom/sign.sii");files.Text["/def/world/sign.extra.sii"]="SiiNunit {"+Unit(path:"/other.pmd")+"}";
        files.Text["/def/world/sign.last.sii"]="SiiNunit {"+Unit()+"}";
        check(StaticRoadSignRegistry.ReadCatalog(files)["pole"]==null,"Conflicting cross-file duplicate remains unknown after a later matching definition");
        files.Fail="/def/world/sign.extra.sii";failed=false;try{StaticRoadSignRegistry.ReadCatalog(files);}catch(IOException){failed=true;}
        check(failed,"Unreadable sign file prevents partial-catalog publication");
    }
    internal sealed record Shape(int Type,Vector3 Position,Quaternion Rotation,Vector3 Dimensions);
    static Model Model(params string[] variants)=>new(){Parts=[new Part("vis")],Variants=variants.Select(v=>new Variant(v){Attributes=[new(){Type=0,Tag="visible",Value=1}]}).ToList()};
    internal static byte[] Pmc(params (string Name,Shape[] Shapes)[] variants)
    {
        bool convex=variants.Any(v=>v.Shapes.Any(s=>s.Type==8));using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(new byte[40]);int look=(int)stream.Position;writer.Write((Token)"default");int names=(int)stream.Position;foreach(var v in variants)writer.Write((Token)v.Name);
        int materials=(int)stream.Position;writer.Write(0);int pieces=(int)stream.Position;if(convex)writer.Write(new byte[16]);
        int defs=(int)stream.Position;writer.Write(new byte[variants.Length*4]);
        if(convex){
            int vertices=(int)stream.Position;foreach(var v in new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ}){writer.Write(v.X);writer.Write(v.Y);writer.Write(v.Z);}
            int faces=(int)stream.Position;foreach(ushort i in new ushort[]{0,2,1,0,1,3,0,3,2,1,2,3})writer.Write(i);
            long restore=stream.Position;stream.Position=pieces;writer.Write(12);writer.Write(4);writer.Write(vertices);writer.Write(faces);stream.Position=restore;
        }
        var offsets=new int[variants.Length];
        for(int i=0;i<variants.Length;i++){
            offsets[i]=(int)stream.Position;
            foreach(var s in variants[i].Shapes){writer.Write(s.Type);writer.Write(s.Type==8?56:64);writer.Write(0);writer.Write(.1f);writer.Write((Token)"collider");
                writer.Write(s.Position.X);writer.Write(s.Position.Y);writer.Write(s.Position.Z);writer.Write(s.Rotation.W);writer.Write(s.Rotation.X);writer.Write(s.Rotation.Y);writer.Write(s.Rotation.Z);
                if(s.Type==8)writer.Write(0);else {writer.Write(s.Dimensions.X);writer.Write(s.Dimensions.Y);writer.Write(s.Dimensions.Z);}}
            writer.Write(0);writer.Write(-1);
        }
        stream.Position=0;foreach(int i in new[]{6,1,variants.Length,1,convex?1:0,look,names,materials,pieces,defs})writer.Write(i);
        stream.Position=defs;foreach(int offset in offsets)writer.Write(offset);writer.Flush();return stream.ToArray();
    }
    static Shape Box(Vector3 at)=>new(1,at,Quaternion.Identity,new(.2f,4,.4f));
    internal static void Geometry(Action<bool,string> check)
    {
        var bytes=Pmc(("default",[Box(new(2,2,3))]));var model=Model("default");
        var q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);var position=new Vector3(100,10,200);
        var result=StaticRoadSignGeometry.Capture(model,bytes,"default",position,q);
        check(result.Issue==null&&result.Geometry?.Count==12,"PMC box is exact12-face geometry, independent of visible PMG mesh");
        var expected=Vector3.Transform(new Vector3(1.9f,0,2.8f),q)+position;
        check(result.Geometry!.Triangles.SelectMany(t=>new[]{t.A,t.B,t.C}).Any(p=>Vector3.Distance(p,expected)<.0001f),"Full PMC dimensions are halved and sign-node rotation/translation applied exactly");
        var localRotated=Pmc(("default",[new Shape(1,new(2,2,3),q,new(.2f,4,.4f))]));
        var local=StaticRoadSignGeometry.Capture(model,localRotated,"default",Vector3.Zero,Quaternion.Identity);
        var localExpected=Vector3.Transform(new Vector3(-.1f,-2,-.2f),q)+new Vector3(2,2,3);
        check(local.Geometry!.Triangles.SelectMany(t=>new[]{t.A,t.B,t.C}).Any(p=>Vector3.Distance(p,localExpected)<.0001f),"PMC WXYZ quaternion maps to Numerics without rotating around the wrong axis");
        check(StaticRoadSignGeometry.Capture(model,bytes,"",Vector3.Zero,Quaternion.Identity).Geometry!=null,"Empty map variant is unambiguous only for exactly one matching PMD/PMC variant");
        var two=Pmc(("left",[Box(new(-5,2,0))]),("right",[Box(new(5,2,0))]));
        var chosen=StaticRoadSignGeometry.Capture(Model("left","right"),two,"right",Vector3.Zero,Quaternion.Identity);
        check(chosen.Geometry!.Triangles.All(t=>t.A.X>4&&t.B.X>4&&t.C.X>4),"Only selected collision variant contributes; other variant never creates a phantom pole");
        check(StaticRoadSignGeometry.Capture(Model("left","right"),two,"",Vector3.Zero,Quaternion.Identity).Geometry==null&&
            StaticRoadSignGeometry.Capture(Model("right","left"),two,"right",Vector3.Zero,Quaternion.Identity).Geometry==null,"Missing or mismatched variant ordering stays unknown");
        var convex=Pmc(("default",[new Shape(8,new(5,1,7),q,default)]));
        check(StaticRoadSignGeometry.Capture(model,convex,"default",Vector3.Zero,Quaternion.Identity).Geometry?.Count==4,"Exact convex vertex/triangle layout transforms without an AABB replacement");
        foreach(byte[] bad in new[]{bytes[..^1],bytes[..30],Mutate(bytes,0,7),Mutate(bytes,8,300),Mutate(bytes,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(36)),4),
            Mutate(bytes,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(36)))),2)})
            check(StaticRoadSignGeometry.Capture(model,bad,"default",Vector3.Zero,Quaternion.Identity).Geometry==null,"Malformed, unsupported or out-of-bounds PMC never creates collision evidence");
        check(StaticRoadSignGeometry.Capture(model,Pmc(("default",[new Shape(1,Vector3.Zero,Quaternion.Identity,new(0,1,1))])),"default",Vector3.Zero,Quaternion.Identity).Geometry==null,"Zero-volume box is unknown");
        check(StaticRoadSignGeometry.Capture(model,bytes,"default",Vector3.Zero,new Quaternion(0,0,0,2)).Geometry==null,"Unnormalized sign-node rotation is unknown");
    }
    static byte[] Mutate(byte[] bytes,int offset,int value){var result=bytes.ToArray();BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset),value);return result;}
    internal static void InstanceAndLayer(Action<bool,string> check)
    {
        string parent=Path.GetFullPath(Path.GetTempPath()),folder=Path.Combine(parent,"maut-sign-layer-"+Guid.NewGuid().ToString("N"));
        try {
            var map=new Map();var sign=Sign.Add(map,new(100,0,100),"pole");sign.Node.Rotation=Quaternion.Identity;sign.Variant="default";
            check(StaticRoadSceneSource.EligibleSign(sign),"Plain static aux Sign has its own supported instance flag layout");
            var def=new StaticRoadSignRegistry.Definition("/pole.pmd","/pole.pmc","");
            check(StaticRoadSceneSource.CaptureSign(sign,def,Model("default"),Pmc(("default",[Box(new(0,2,0))]))).Geometry!=null,"Actual sign branch captures registered collision at its resolved world node");
            sign.Collision=false;check(!StaticRoadSceneSource.EligibleSign(sign),"Sign-specific disabled collision bit30 blocks authority");sign.Collision=true;
            sign.FollowRoadDir=true;check(!StaticRoadSceneSource.EligibleSign(sign),"Road-relative sign rotation is not guessed from its editor node");sign.FollowRoadDir=false;
            sign.SignTemplate="sign_templ.unknown";check(!StaticRoadSceneSource.EligibleSign(sign),"Unresolved sign template is not a fixed body");sign.SignTemplate="";
            sign.SignOverrides=[new()];check(!StaticRoadSceneSource.EligibleSign(sign),"Sign override cannot change a bound collider silently");sign.SignOverrides=[];
            var kdop=typeof(MapItem).GetProperty("Kdop",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(sign)!;
            var flags=kdop.GetType().GetField("Flags",BindingFlags.NonPublic|BindingFlags.Instance)!;flags.SetValue(kdop,new FlagField(1u<<13));
            check(!StaticRoadSceneSource.EligibleSign(sign),"Unknown sign flags never borrow Model's whitelist");flags.SetValue(kdop,new FlagField(0));
            sign.Layer=1;map.Save(Path.Combine(folder,"map"),"review");
            var files=new Files(folder);var loaded=new Map();StaticRoadAuxMapReader.Read(loaded,files,"review",new(0,0));var actual=(Sign)loaded.MapItems[sign.Uid];
            check(actual.Layer==1&&!StaticRoadSceneSource.EligibleSign(actual)&&actual.Node.Position==sign.Node.Position,"Actual aux/layer read resolves Sign placement and excludes conditional layer");
            files.FailLayer=true;bool rejected=false;var partial=new Map();try{StaticRoadAuxMapReader.Read(partial,files,"review",new(0,0));}catch(IOException){rejected=true;}
            check(rejected&&partial.MapItems[sign.Uid].Layer==0,"Failed layer read cannot publish the already parsed default-layer Sign");
        } finally {string target=Path.GetFullPath(folder);if(target.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(target).StartsWith("maut-sign-layer-",StringComparison.Ordinal)&&Directory.Exists(target))Directory.Delete(target,true);}
    }
    sealed class Files(string root):IFileSystem
    {
        public bool FailLayer;public char DirectorySeparator=>'/';string Local(string p)=>Path.Combine(root,p.TrimStart('/').Replace('/',Path.DirectorySeparatorChar));
        public bool FileExists(string p)=>File.Exists(Local(p));public bool DirectoryExists(string p)=>Directory.Exists(Local(p));public IList<string> GetFiles(string p)=>throw new NotSupportedException();
        public byte[] ReadAllBytes(string p)=>File.ReadAllBytes(Local(p));public string ReadAllText(string p)=>File.ReadAllText(Local(p));public string ReadAllText(string p,Encoding e)=>File.ReadAllText(Local(p),e);
        public Stream Open(string p){if(FailLayer&&p.EndsWith(".layer"))throw new IOException("Layer unavailable");return File.OpenRead(Local(p));}public string GetParent(string p)=>p[..p.LastIndexOf('/')];
    }
    sealed class CatalogFiles:IFileSystem
    {
        internal readonly Dictionary<string,string> Text=new(StringComparer.Ordinal);internal string? Fail;
        public char DirectorySeparator=>'/';public bool FileExists(string p){if(p==Fail)throw new IOException("Unavailable source");return Text.ContainsKey(p);}
        public bool DirectoryExists(string p)=>true;public IList<string> GetFiles(string p)=>Text.Keys.Where(k=>k.StartsWith(p,StringComparison.Ordinal)).ToArray();
        public byte[] ReadAllBytes(string p)=>Encoding.UTF8.GetBytes(ReadAllText(p));public string ReadAllText(string p){if(p==Fail)throw new IOException("Unavailable source");return Text[p];}
        public string ReadAllText(string p,Encoding e)=>ReadAllText(p);public Stream Open(string p)=>new MemoryStream(ReadAllBytes(p));public string GetParent(string p)=>p[..p.LastIndexOf('/')];
    }
}
