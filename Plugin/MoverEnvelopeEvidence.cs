using System.Buffers.Binary;
using System.Collections;
using System.Numerics;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
namespace MautAssistent;

// A fixed prefab attachment with a bounded skeletal animation. This is NOT
// support for movers following arbitrary world paths or animations with root
// movement. It encloses every selected model/LOD and every compatible clip.
internal sealed class MoverEnvelopeEvidence(IFileSystem files,Func<string,(Model Model,byte[] Pmg)> load)
{
    internal sealed record Sphere(Vector3 Center,double Radius);
    readonly Dictionary<string,Sphere> cache=new(StringComparer.Ordinal);
    // Origin-centred callers retain a conservative compatibility bound. Static
    // placement uses Envelope so a human's height does not become extra width.
    internal double Radius(Unit hookup){var sphere=Envelope(hookup);return sphere.Center.Length()+sphere.Radius;}
    internal Sphere Envelope(Unit hookup)
    {
        if(hookup.Class!="mover_hookup"||hookup.Attributes.Keys.Any(k=>k is not ("mover_suffix" or "active_during_night"))||
            hookup.Attributes.TryGetValue("active_during_night",out var atNight)&&(object?)atNight is not bool||
            !hookup.Attributes.TryGetValue("mover_suffix",out var raw)||
            (object?)raw is not Token suffix||suffix.ToString() is not {Length:>0 and <64} name||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))
            throw new InvalidDataException("Ungeprüfte Mover-Zuordnung");
        // Optional time-of-day visibility never establishes absence: inspect
        // the same full model/clip envelope for both true and false.
        if(cache.TryGetValue(name,out var cached))return cached;
        var group=ReadUnits("/def/world/mover.group.sii","mover_model_group").SingleOrDefault(u=>u.Name=="mover."+name)
            ??throw new InvalidDataException("Mover-Gruppe fehlt");
        Schema(group,"mover_name","desc_tags","anim_tags","pedestrian","country_specific");
        if(!group.Attributes.TryGetValue("pedestrian",out var pedestrian)||(object?)pedestrian is not true)
            throw new InvalidDataException("Ungeprüfte Mover-Platzierungsart");
        if(group.Attributes.TryGetValue("country_specific",out var country)&&(object?)country is not bool)throw new InvalidDataException("Mover-Länderfilter ungültig");
        _=Text(group,"mover_name");var descTags=Strings(group,"desc_tags");var animTags=Strings(group,"anim_tags");
        if(descTags.Length==0||animTags.Length==0)throw new InvalidDataException("Mover-Tags fehlen");
        var descriptions=ReadUnits("/def/world/mover_desc.sii","mover_desc").Where(u=>descTags.All(Strings(u,"group_tags").Contains)).ToArray();
        var animations=ReadUnits("/def/world/mover_anim.sii","mover_anim").Where(u=>animTags.All(Strings(u,"group_tags").Contains)).ToArray();
        if(descriptions.Length is <1 or >32||animations.Length is <1 or >64)throw new InvalidDataException("Mover-Auswahl nicht vollständig begrenzt");
        double radius=0;Vector3? commonCenter=null;
        foreach(var description in descriptions) {
            Schema(description,"model_desc","model_coll","lods","lod_distances","variants","group_tags","area_tags");
            if(description.Attributes.ContainsKey("model_coll")&&Text(description,"model_coll")!="")throw new InvalidDataException("Mover-Kollision nicht geprüft");
            string mainPath=PathValue(Text(description,"model_desc"));
            var main=load(mainPath);_=Rig(main.Model);ulong skeleton=SkeletonHash(main.Pmg);
            // A SINGLE fixed model-local centre for the entire description/LOD
            // union. Every top-level root, clip and rest vertex is bounded
            // relative to this same point, including differently placed LODs.
            commonCenter??=main.Model.Skeleton.First(b=>b.Parent==255).Translation;
            Vector3 center=commonCenter.Value;
            var clipFamilies=new List<string>();
            foreach(var animation in animations) {
                Schema(animation,"model_anim","group_tags","props");
                if(Strings(animation,"props").Length!=0)throw new InvalidDataException("Mover-Requisiten nicht geprüft");
                string animationPath=PathValue(Text(animation,"model_anim"),".pma");var bytes=files.ReadAllBytes(animationPath);
                if(bytes.Length<84||U32(bytes,0)!=5)throw new InvalidDataException("Ungeprüftes Mover-Animationsformat");
                if(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12))!=skeleton)continue; // engine requires the same skeleton
                // Validate the compatible main clip now; all declared LOD
                // files are validated against their OWN skeleton below.
                _=ReadClip(bytes,main.Model,center);clipFamilies.Add(animationPath);
            }
            if(clipFamilies.Count==0)throw new InvalidDataException("Keine kompatible geprüfte Mover-Animation");
            var lods=Strings(description,"lods");
            if(lods.Length>7||lods.Select((p,i)=>(p,i)).Any(x=>x.p==""&&lods.Skip(x.i+1).Any(p=>p!="")))throw new InvalidDataException("Mover-LOD-Liste unbeschränkt oder mehrdeutig");
            var paths=new[]{mainPath}.Concat(lods).ToArray();
            for(int level=0;level<paths.Length;level++) {
                string path=paths[level];if(path=="")continue;path=PathValue(path);
                var loaded=load(path);var model=loaded.Model;var rig=Rig(model);ulong lodSkeleton=SkeletonHash(loaded.Pmg);
                if(files.FileExists(Path.ChangeExtension(path,".pmc"))||model.Parts.Any(p=>p.Locators.Count!=0))throw new InvalidDataException("Zusätzliche Mover-Geometrie nicht geprüft");
                var clips=new List<LocalClip>();
                foreach(string family in clipFamilies) {
                    // Deliberately narrow SCS mover convention, evidenced by
                    // current source assets: foo.pma / foo_l1.pma / foo_l2.pma.
                    // A missing file or different embedded skeleton is UNKNOWN,
                    // never a licence to rename/remap reduced LOD bones.
                    string clipPath=level==0?family:family[..^4]+"_l"+level+".pma";
                    byte[] bytes=files.ReadAllBytes(clipPath);
                    if(bytes.Length<84||U32(bytes,0)!=5||BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12))!=lodSkeleton)
                        throw new InvalidDataException("Mover-LOD-Animation nicht eindeutig an ihr Skelett gebunden");
                    clips.Add(ReadClip(bytes,model,center));
                }
                var envelope=Propagate(model,clips);
                radius=Math.Max(radius,Enclose(model,rig.Inverse,envelope,center));
            }
        }
        if(!double.IsFinite(radius)||radius<=0||commonCenter is not { } finalCenter||!Finite(finalCenter)||
            !double.IsFinite((double)finalCenter.Length()+radius)||finalCenter.Length()+radius>50)
            throw new InvalidDataException("Mover-Hülle nicht begrenzt");
        if(cache.Count>=32)cache.Clear();return cache[name]=new(commonCenter!.Value,radius*1.005+.05);
    }
    internal sealed record LocalClip(double[] Translation,double[] Scale);
    internal sealed record Clip(double[] Position,double[] Scale);
    internal static LocalClip ReadClip(byte[] bytes,Model model,Vector3 center=default)
    {
        if(!Finite(center))throw new InvalidDataException("Mover-Hüllenzentrum ungültig");
        if(bytes.Length is <84 or >64000000||U32(bytes,0)!=5)throw new InvalidDataException("Mover-Animation ungültig");
        int frames=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)),flags=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
        uint bones=U32(bytes,8);float duration=F(bytes,60);
        if(frames is <1 or >4096||bones==0||bones>model.Skeleton.Count||flags!=0||!float.IsFinite(duration)||duration is <=0 or >1000)
            throw new InvalidDataException("Bewegte oder unbeschränkte Mover-Animation");
        int times=Offset(bytes,64,frames*4),indexes=Offset(bytes,68,checked((int)bones)),poses=Offset(bytes,72,checked(frames*(int)bones*56));
        var ranges=new[]{(times,times+frames*4),(indexes,indexes+(int)bones),(poses,poses+frames*(int)bones*56)};
        if(ranges.Sum(r=>r.Item2-r.Item1)!=bytes.Length-84||ranges.Where((a,i)=>ranges.Skip(i+1).Any(b=>a.Item1<b.Item2&&b.Item1<a.Item2)).Any())
            throw new InvalidDataException("Überlappende oder unbekannte Mover-Animationsbereiche");
        float last=-1;for(int i=0;i<frames;i++){float t=F(bytes,times+i*4);if(!float.IsFinite(t)||t<last||t<0||t>duration+.01f)throw new InvalidDataException("Mover-Zeitachse ungültig");last=t;}
        var trans=model.Skeleton.Select(b=>(double)(b.Translation-(b.Parent==255?center:Vector3.Zero)).Length()).ToArray();
        var scale=model.Skeleton.Select(b=>Max(b.Scale)*RotationBound(b.Rotation)).ToArray();
        var used=new HashSet<int>();
        for(int slot=0;slot<bones;slot++) {
            int bone=bytes[indexes+slot];if(bone>=model.Skeleton.Count||!used.Add(bone))throw new InvalidDataException("Mover-Knochenliste ungültig");
            for(int frame=0;frame<frames;frame++) {
                int p=checked(poses+(frame*(int)bones+slot)*56);
                var stretch=Q(bytes,p);var rotation=Q(bytes,p+16);var translation=V(bytes,p+32);var scaling=V(bytes,p+44);
                double stretchFactor=RotationBound(stretch),factor=RotationBound(rotation)*stretchFactor*stretchFactor;
                double s=Max(scaling);if(s>10||translation.Length()>100)throw new InvalidDataException("Mover-Transformation außerhalb des Vertrags");
                // Only root translation is model-local. Child translations and
                // inverse-bind skin offsets must NEVER have center subtracted.
                trans[bone]=Math.Max(trans[bone],(translation-(model.Skeleton[bone].Parent==255?center:Vector3.Zero)).Length());scale[bone]=Math.Max(scale[bone],s*factor);
            }
        }
        return new(trans,scale);
    }
    internal static Clip Propagate(Model model,IReadOnlyList<LocalClip> clips)
    {
        if(clips.Count is <1 or >64||clips.Any(c=>c.Translation.Length!=model.Skeleton.Count||c.Scale.Length!=model.Skeleton.Count||
            c.Translation.Any(t=>!double.IsFinite(t)||t<0)||c.Scale.Any(s=>!double.IsFinite(s)||s<0)))throw new InvalidDataException("Mover-Animationsgrenzen ungültig");
        // Merge LOCAL extrema across all compatible clips before composing
        // the chain. This also encloses cross-clip interpolation: combining a
        // parent's scale from one clip with child motion from another cannot
        // create an unaccounted larger pose. Arbitrary rotation is enclosed.
        var position=new double[model.Skeleton.Count];var globalScale=new double[position.Length];
        for(int i=0;i<position.Length;i++) {
            int parent=model.Skeleton[i].Parent;
            if(parent!=255&&(parent<0||parent>=i))throw new InvalidDataException("Mover-Skeletthierarchie ungültig");
            double parentScale=parent==255?1:globalScale[parent];
            position[i]=(parent==255?0:position[parent])+parentScale*(clips.Max(c=>c.Translation[i])+.0001);
            globalScale[i]=parentScale*(clips.Max(c=>c.Scale[i])+.0001);
        }
        return new(position,globalScale);
    }
    internal static double Enclose(Model model,Matrix4x4[] inverse,Clip clip,Vector3 center=default)
    {
        if(!Finite(center))throw new InvalidDataException("Mover-Hüllenzentrum ungültig");
        if(clip.Position.Length==0||clip.Position.Length!=clip.Scale.Length||clip.Position.Any(p=>!double.IsFinite(p)||p<0)||clip.Scale.Any(s=>!double.IsFinite(s)||s<0))
            throw new InvalidDataException("Mover-Skelettgrenzen ungültig");
        if(clip.Position.Length!=model.Skeleton.Count)throw new InvalidDataException("Mover-Clip gehört nicht zu diesem Skelett");
        double radius=0;int count=0;
        foreach(var piece in model.Parts.SelectMany(p=>p.Pieces))foreach(var vertex in piece.Vertices) {
            if(++count>1000000||!Finite(vertex.Position)||vertex.BoneIndexes==null||vertex.BoneWeights==null||vertex.BoneIndexes.Length!=vertex.BoneWeights.Length||
                vertex.BoneWeights.Length is <1 or >4||vertex.BoneWeights.Sum(w=>(int)w)!=255)throw new InvalidDataException("Mover-Hautgewichtung ungültig");
            double bound=0;
            for(int k=0;k<vertex.BoneWeights.Length;k++) {
                if(vertex.BoneWeights[k]==0)continue;int bone=vertex.BoneIndexes[k];
                if(bone>=inverse.Length)throw new InvalidDataException("Mover-Hautknochen ungültig");
                double local=Vector3.Transform(vertex.Position,inverse[bone]).Length()+.002;
                bound+=vertex.BoneWeights[k]/255d*(clip.Position[bone]+clip.Scale[bone]*local);
            }
            radius=Math.Max(radius,Math.Max(bound,(vertex.Position-center).Length()));
        }
        if(count==0)throw new InvalidDataException("Mover-Modell ohne Geometrie");return radius;
    }
    internal static (Matrix4x4[] Global,Matrix4x4[] Inverse) Rig(Model model)
    {
        if(model.Skeleton.Count is <1 or >254||model.Skeleton.Select(b=>b.Name).Distinct().Count()!=model.Skeleton.Count||model.Parts.Count==0||
            model.Variants.Count==0||model.Variants.Any(v=>v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value>1)))
            throw new InvalidDataException("Mover-Modellvariante/Skelett ungültig");
        var globals=new Matrix4x4[model.Skeleton.Count];var inverses=new Matrix4x4[globals.Length];
        for(int i=0;i<globals.Length;i++) {
            var bone=model.Skeleton[i];double scale=Max(bone.Scale);_=RotationBound(bone.Rotation);_=RotationBound(bone.Stretch);
            if(!Finite(bone.Translation)||scale>10||bone.SignOfDeterminantOfMatrix!=1||bone.Parent!=255&&(bone.Parent<0||bone.Parent>=i))throw new InvalidDataException("Mover-Bindepose ungültig");
            var local=Matrix4x4.CreateScale(bone.Scale)*Matrix4x4.CreateFromQuaternion(bone.Rotation)*Matrix4x4.CreateTranslation(bone.Translation);
            globals[i]=bone.Parent==255?local:local*globals[bone.Parent];
            if(!Matrix4x4.Invert(globals[i],out inverses[i])||Difference(globals[i],Matrix4x4.Transpose(bone.Transformation))>.001||
                Difference(inverses[i],Matrix4x4.Transpose(bone.TransformationReserved))>.001)
                throw new InvalidDataException("Mover-Bindematrizen passen nicht zur Skeletthierarchie");
        }
        return(globals,inverses);
    }
    static ulong SkeletonHash(byte[] pmg)
    {
        if(pmg.Length<112||pmg[0]!=21||pmg[1]!='g'||pmg[2]!='m'||pmg[3]!='P')throw new InvalidDataException("Mover-PMG-Kennung ungültig");
        ulong hash=BinaryPrimitives.ReadUInt64LittleEndian(pmg.AsSpan(24));return hash!=0?hash:throw new InvalidDataException("Mover-Skelettkennung fehlt");
    }
    Unit[] ReadUnits(string path,string expected)
    {
        var units=SiiFile.Open(path,files).Units.ToArray();
        if(units.Length is <1 or >8192||units.Any(u=>u.Class!=expected)||units.Select(u=>u.Name).Distinct(StringComparer.Ordinal).Count()!=units.Length)
            throw new InvalidDataException("Mover-Katalog ungültig");return units;
    }
    static void Schema(Unit u,params string[] keys){if(u.Attributes.Keys.Any(k=>!keys.Contains(k,StringComparer.Ordinal)))throw new InvalidDataException("Ungeprüftes Mover-Feld: "+u.Name);}
    static string Text(Unit u,string key)=>u.Attributes.TryGetValue(key,out var value)&&(object?)value is string text?text:throw new InvalidDataException("Mover-Textfeld fehlt: "+key);
    static string[] Strings(Unit u,string key)
    {
        if(!u.Attributes.TryGetValue(key,out var raw))return[];
        if((object?)raw is string||(object?)raw is not IEnumerable values)throw new InvalidDataException("Mover-Liste ungültig");
        var list=values.Cast<object>().Select(v=>v switch{string s=>s,Token t=>t.ToString(),LinkPointer p=>p.Value,_=>throw new InvalidDataException("Mover-Listenelement ungültig")}).ToArray();
        if(list.Length>256)throw new InvalidDataException("Mover-Liste zu groß");return list;
    }
    static string PathValue(string path,string suffix=".pmd")=>path.StartsWith('/')&&path.EndsWith(suffix,StringComparison.Ordinal)&&!path.Contains("..")&&!path.Contains('\\')?path:throw new InvalidDataException("Mover-Pfad ungültig");
    static int Offset(byte[] b,int p,int length){int offset=checked((int)U32(b,p));if(offset<84||length<0||offset>b.Length-length)throw new InvalidDataException("Mover-Animationsbereich ungültig");return offset;}
    static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    static float F(byte[] b,int p)=>BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p)));
    static Vector3 V(byte[] b,int p){var v=new Vector3(F(b,p),F(b,p+4),F(b,p+8));return Finite(v)?v:throw new InvalidDataException("Mover-Vektor nicht endlich");}
    static Quaternion Q(byte[] b,int p)=>new(F(b,p+4),F(b,p+8),F(b,p+12),F(b,p)); // binary quaternions are w,x,y,z
    static double RotationBound(Quaternion q){double norm=q.LengthSquared();if(!double.IsFinite(norm)||Math.Abs(norm-1)>.001)throw new InvalidDataException("Mover-Quaternion ungültig");return 1+2*Math.Max(0,norm-1);}
    static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    static double Max(Vector3 v){if(!Finite(v))throw new InvalidDataException("Mover-Skalierung ungültig");return Math.Max(Math.Abs(v.X),Math.Max(Math.Abs(v.Y),Math.Abs(v.Z)));}
    static double Difference(Matrix4x4 a,Matrix4x4 b)
    {
        var d=a-b;double value=d.M11*d.M11+d.M12*d.M12+d.M13*d.M13+d.M14*d.M14+d.M21*d.M21+d.M22*d.M22+d.M23*d.M23+d.M24*d.M24+
            d.M31*d.M31+d.M32*d.M32+d.M33*d.M33+d.M34*d.M34+d.M41*d.M41+d.M42*d.M42+d.M43*d.M43+d.M44*d.M44;
        return double.IsFinite(value)?Math.Sqrt(value):double.PositiveInfinity;
    }
}
