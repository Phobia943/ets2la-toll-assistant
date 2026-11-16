using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib.Models;
internal static class MoverEnvelopeIntegration
{
    static Model RigModel()
    {
        var bones=new[]{new Bone{Name="root",Parent=255,Translation=Vector3.UnitY,Scale=Vector3.One,Rotation=Quaternion.Identity,Stretch=Quaternion.Identity},
            new Bone{Name="child",Parent=0,Translation=Vector3.UnitX,Scale=Vector3.One,Rotation=Quaternion.Identity,Stretch=Quaternion.Identity}};
        var globals=new Matrix4x4[bones.Length];
        for(int i=0;i<bones.Length;i++) {
            globals[i]=Matrix4x4.CreateTranslation(bones[i].Translation)*(i==0?Matrix4x4.Identity:globals[0]);
            Matrix4x4.Invert(globals[i],out var inverse);bones[i].Transformation=Matrix4x4.Transpose(globals[i]);bones[i].TransformationReserved=Matrix4x4.Transpose(inverse);
        }
        return new(){Skeleton=[..bones],Parts=[new("body"){Pieces=[new(){Vertices=[new(new Vector3(1.5f,1,0)){BoneIndexes=[1],BoneWeights=[255]}]}]}],
            Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
    }
    static byte[] Animation()
    {
        const int frames=2,bones=2,times=84,indexes=92,poses=94;var data=new byte[poses+frames*bones*56];
        void I(int p,int v)=>BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p),v);
        void F(int p,float v)=>I(p,BitConverter.SingleToInt32Bits(v));
        I(0,5);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4),frames);I(8,bones);F(60,1);I(64,times);I(68,indexes);I(72,poses);F(times,0);F(times+4,1);data[indexes]=0;data[indexes+1]=1;
        for(int frame=0;frame<frames;frame++)for(int bone=0;bone<bones;bone++) {
            int p=poses+(frame*bones+bone)*56;F(p,1);F(p+16,1);F(p+44,1);F(p+48,1);F(p+52,1);
            F(p+32,bone==1?1+frame:0);F(p+36,bone==0?1+frame*2:0);
            if(frame==1&&bone==1){F(p+44,2);F(p+48,2);F(p+52,2);}
        }
        return data;
    }
    static bool Rejected(Action action){try{action();return false;}catch(Exception ex)when(ex is InvalidDataException or FileNotFoundException){return true;}}
    internal static void Bounds(Action<bool,string> assert)
    {
        var model=RigModel();var rig=MoverEnvelopeEvidence.Rig(model);var local=MoverEnvelopeEvidence.ReadClip(Animation(),model);
        var global=MoverEnvelopeEvidence.Propagate(model,[local]);double radius=MoverEnvelopeEvidence.Enclose(model,rig.Inverse,global);
        assert(radius>=6&&radius<6.1,"The full chain encloses child translation, scale and inverse-bind vertex radius");
        // Independently compose many intermediate transforms. The proof above
        // allows all rotations, including those never stored as keyframes.
        for(int i=0;i<=30;i++)for(int axis=0;axis<3;axis++) {
            float t=i/30f;var rotation=Quaternion.CreateFromAxisAngle(axis==0?Vector3.UnitX:axis==1?Vector3.UnitY:Vector3.UnitZ,t*6.2f);
            var parent=Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(0,1+2*t,0);
            var child=Matrix4x4.CreateScale(1+t)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(1+t,0,0);
            Vector3 point=Vector3.Transform(new(.5f,0,0),child*parent);
            assert(point.Length()<=radius,"Every between-keyframe independently composed pose remains inside the enclosure");
        }
        var cross=MoverEnvelopeEvidence.Propagate(model,[new([0,0],[10,1]),new([0,10],[.1,1])]);
        assert(cross.Position[1]>100,"Cross-clip maxima merge before chaining: parent scale and another clip's child movement cannot hide");
        var siblings=RigModel();siblings.Skeleton[1].Parent=255;
        var siblingClip=MoverEnvelopeEvidence.Propagate(siblings,[new([1,1],[1,1])]);
        var chainClip=MoverEnvelopeEvidence.Propagate(model,[new([1,1],[1,1])]);
        assert(chainClip.Position[1]>siblingClip.Position[1]+.9,"Each actual LOD hierarchy must propagate its own clip, rather than borrowing main global maxima");
        var invalid=Animation();BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(6),2);assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(invalid,model)),"Root movement cannot receive a fixed-anchor enclosure");
        invalid=Animation();BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(72),84);assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(invalid,model)),"Overlapping binary sections are rejected");
        invalid=Animation();invalid[93]=0;assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(invalid,model)),"A repeated bone index cannot conceal an omitted track");
        invalid=Animation();BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(94+32),BitConverter.SingleToInt32Bits(float.NaN));assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(invalid,model)),"Invalid animated translations cannot collapse a bound");
        invalid=Animation()[..^1];assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(invalid,model)),"Truncated pose payloads are rejected");
        var weighted=RigModel();weighted.Parts[0].Pieces[0].Vertices[0].BoneWeights=[254];assert(Rejected(()=>MoverEnvelopeEvidence.Enclose(weighted,rig.Inverse,global)),"Nonconvex or missing skin weights cannot shrink the enclosure");
        weighted=RigModel();var mixed=weighted.Parts[0].Pieces[0].Vertices[0];mixed.BoneIndexes=[0,1];mixed.BoneWeights=[128,127];
        double mixedRadius=MoverEnvelopeEvidence.Enclose(weighted,rig.Inverse,global);
        for(int i=0;i<=20;i++) {
            float t=i/20f;var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,t*6);
            var parent=Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(0,1+2*t,0);
            var child=Matrix4x4.CreateScale(1+t)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(1+t,0,0)*parent;
            var blended=(Vector3.Transform(Vector3.Transform(mixed.Position,rig.Inverse[0]),parent)*128+
                Vector3.Transform(Vector3.Transform(mixed.Position,rig.Inverse[1]),child)*127)/255;
            assert(blended.Length()<=mixedRadius,"Multiple influences retain their convex weights and nonzero inverse-bind translations");
        }
        invalid=Animation();int last=94+3*56;foreach(int p in new[]{last+44,last+48,last+52})BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(p),BitConverter.SingleToInt32Bits(-2));
        BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(last),BitConverter.SingleToInt32Bits(.70710677f));BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(last+4),BitConverter.SingleToInt32Bits(.70710677f));
        var reflected=MoverEnvelopeEvidence.Propagate(RigModel(),[MoverEnvelopeEvidence.ReadClip(invalid,RigModel())]);
        assert(reflected.Scale[1]>=2,"Negative scales and nontrivial stretch orientation cannot shrink operator norms");
        model.Skeleton[1].Parent=1;assert(Rejected(()=>MoverEnvelopeEvidence.Rig(model)),"Cyclic skeletons cannot have a bounded ordered chain");
        CenteredBounds(assert);
        Family(assert);
    }
    static void CenteredBounds(Action<bool,string> assert)
    {
        var model=RigModel();var inverse=MoverEnvelopeEvidence.Rig(model).Inverse;Vector3 center=new(0,1,0);
        var clip=MoverEnvelopeEvidence.Propagate(model,[MoverEnvelopeEvidence.ReadClip(Animation(),model,center)]);
        double radius=MoverEnvelopeEvidence.Enclose(model,inverse,clip,center);
        assert(radius>=5&&radius<5.1,"A fixed bind-root centre removes height-origin inflation without dropping any root motion");
        assert(clip.Position[0]>=2&&clip.Position[0]<2.01,"Root animation and baseline are measured relative to the single centre");
        assert(clip.Position[1]>4,"Child-local translations retain their original coordinates");
        var weighted=model.Parts[0].Pieces[0].Vertices[0];weighted.BoneIndexes=[0,1];weighted.BoneWeights=[128,127];
        radius=MoverEnvelopeEvidence.Enclose(model,inverse,clip,center);
        for(int i=0;i<=30;i++)for(int axis=0;axis<3;axis++) {
            float t=i/30f;var q=Quaternion.CreateFromAxisAngle(axis==0?Vector3.UnitX:axis==1?Vector3.UnitY:Vector3.UnitZ,t*6.2f);
            var parent=Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(0,1+2*t,0);
            var child=Matrix4x4.CreateScale(new Vector3(1+t,-1-t,1+t))*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(1+t,0,0)*parent;
            var point=(Vector3.Transform(Vector3.Transform(weighted.Position,inverse[0]),parent)*128+
                Vector3.Transform(Vector3.Transform(weighted.Position,inverse[1]),child)*127)/255;
            assert(Vector3.Distance(point,center)<=radius,"Recentered convex skin encloses between-frame rotations, negative scales and unchanged inverse-bind offsets");
        }
        var roots=RigModel();roots.Skeleton[1].Parent=255;
        var rootLocal=MoverEnvelopeEvidence.ReadClip(Animation(),roots,center);
        assert(rootLocal.Translation[1]>=Math.Sqrt(5),"Every disconnected root subtracts the same centre, including roots other than bone zero");
        var omitted=Animation();BinaryPrimitives.WriteInt32LittleEndian(omitted.AsSpan(94+3*56+36),BitConverter.SingleToInt32Bits(0));
        var shifted=ShiftedModel(new(0,10,0));var shiftedRig=MoverEnvelopeEvidence.Rig(shifted);
        var shiftedClip=MoverEnvelopeEvidence.Propagate(shifted,[MoverEnvelopeEvidence.ReadClip(omitted,shifted,center)]);
        double shiftedRadius=MoverEnvelopeEvidence.Enclose(shifted,shiftedRig.Inverse,shiftedClip,center);
        assert(shiftedRadius>=10,"A different LOD's baseline remains relative to the common centre, never its own convenient centre");
        assert(Rejected(()=>MoverEnvelopeEvidence.ReadClip(Animation(),model,new(float.NaN,0,0))),"A nonfinite centre cannot shrink a bound");
    }
    static Model ShiftedModel(Vector3 offset)
    {
        var model=RigModel();model.Skeleton[0].Translation+=offset;
        foreach(var v in model.Parts.SelectMany(p=>p.Pieces).SelectMany(p=>p.Vertices))v.Position+=offset;
        var globals=new Matrix4x4[model.Skeleton.Count];
        for(int i=0;i<globals.Length;i++) {
            var b=model.Skeleton[i];globals[i]=Matrix4x4.CreateTranslation(b.Translation)*(b.Parent==255?Matrix4x4.Identity:globals[b.Parent]);
            Matrix4x4.Invert(globals[i],out var inverse);b.Transformation=Matrix4x4.Transpose(globals[i]);b.TransformationReserved=Matrix4x4.Transpose(inverse);
        }
        return model;
    }
    static void Family(Action<bool,string> assert)
    {
        var files=new ChildGeometryIntegration.Files();void Text(string p,string t)=>files.Data[p]=Encoding.UTF8.GetBytes(t);
        Text("/def/world/mover.group.sii","SiiNunit { mover_model_group : mover.test { mover_name: \"fixture\" pedestrian: true desc_tags[]: human anim_tags[]: idle } }");
        Text("/def/world/mover_desc.sii","SiiNunit { mover_desc : .test { model_desc: \"/main.pmd\" lods[]: \"/lod.pmd\" group_tags[]: human } }");
        byte[] FarClip(ulong hash) {
            var bytes=Clip(hash);int poses=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(72));
            int frames=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)),bones=(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
            for(int frame=0;frame<frames;frame++)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(poses+frame*bones*56+32),BitConverter.SingleToInt32Bits(60));
            return bytes;
        }
        Text("/def/world/mover_anim.sii","SiiNunit { mover_anim : .test { model_anim: \"/clip.pma\" group_tags[]: idle } }");
        byte[] Header(ulong hash){var b=new byte[112];b[0]=21;b[1]=(byte)'g';b[2]=(byte)'m';b[3]=(byte)'P';BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(24),hash);return b;}
        byte[] Clip(ulong hash){var b=Animation();BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(12),hash);return b;}
        byte[] ShiftedClip(ulong hash,Vector3 shift) {
            var b=Clip(hash);
            for(int frame=0;frame<2;frame++)for(int axis=0;axis<3;axis++) {
                int p=94+frame*2*56+32+axis*4;float value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p)));
                float offset=axis==0?shift.X:axis==1?shift.Y:shift.Z;
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p),BitConverter.SingleToInt32Bits(value+offset));
            }
            return b;
        }
        files.Data["/clip.pma"]=Clip(1);files.Data["/clip_l1.pma"]=Clip(2);
        var hookup=new TruckLib.Sii.Unit("mover_hookup","arbitrary");hookup.Attributes["mover_suffix"]=(TruckLib.Token)"test";
        var farModel=ShiftedModel(new(60,0,0));var farCenter=farModel.Skeleton[0].Translation;
        double farRadius=MoverEnvelopeEvidence.Enclose(farModel,MoverEnvelopeEvidence.Rig(farModel).Inverse,
            MoverEnvelopeEvidence.Propagate(farModel,[MoverEnvelopeEvidence.ReadClip(FarClip(1),farModel,farCenter)]),farCenter);
        assert(farRadius<50&&farCenter.Length()>50,"The shifted fixture has a small valid recentered sphere but exceeds the origin-distance contract");
        files.Data["/clip.pma"]=FarClip(1);files.Data["/clip_l1.pma"]=FarClip(2);
        assert(Rejected(()=>new MoverEnvelopeEvidence(files,p=>(ShiftedModel(new(60,0,0)),Header(p=="/main.pmd"?1ul:2ul))).Envelope(hookup)),
            "Recentering cannot evade the original bounded distance from the attachment origin");
        files.Data["/clip.pma"]=Clip(1);files.Data["/clip_l1.pma"]=Clip(2);
        double Read()=>new MoverEnvelopeEvidence(files,p=>(RigModel(),Header(p=="/main.pmd"?1ul:2ul))).Radius(hookup);
        double baseRadius=Read();assert(baseRadius>6,"Every declared LOD binds to its own real animation family and exact skeleton hash");
        var centered=new MoverEnvelopeEvidence(files,p=>(RigModel(),Header(p=="/main.pmd"?1ul:2ul))).Envelope(hookup);
        assert(centered.Center==Vector3.UnitY&&centered.Radius<5.2,"The actual family API retains a fixed bind-derived nonzero centre");
#if INTEGRATION
        Placement(assert,files,hookup,Header,centered);
#endif
        files.Data["/clip_l1.pma"]=ShiftedClip(2,new(0,10,0));
        var shiftedLod=new MoverEnvelopeEvidence(files,p=>(p=="/lod.pmd"?ShiftedModel(new(0,10,0)):RigModel(),Header(p=="/main.pmd"?1ul:2ul))).Envelope(hookup);
        assert(shiftedLod.Center==centered.Center&&shiftedLod.Radius>=10,"A displaced declared LOD and matching shifted animation cannot reset the common centre and hide their complete envelope");
        files.Data["/clip_l1.pma"]=Clip(2);
        Text("/def/world/mover_desc.sii","SiiNunit { mover_desc : .a { model_desc: \"/main.pmd\" group_tags[]: human } mover_desc : .b { model_desc: \"/second.pmd\" group_tags[]: human } }");
        Text("/def/world/mover_anim.sii","SiiNunit { mover_anim : .a { model_anim: \"/clip.pma\" group_tags[]: idle } mover_anim : .b { model_anim: \"/second.pma\" group_tags[]: idle } }");
        files.Data["/second.pma"]=ShiftedClip(3,new(10,0,0));
        var descriptions=new MoverEnvelopeEvidence(files,p=>(p=="/second.pmd"?ShiftedModel(new(10,0,0)):RigModel(),Header(p=="/second.pmd"?3ul:1ul))).Envelope(hookup);
        assert(descriptions.Center==centered.Center&&descriptions.Radius>=10,"Different selected descriptions share one centre for the union of rest and animated geometry");
        Text("/def/world/mover_desc.sii","SiiNunit { mover_desc : .test { model_desc: \"/main.pmd\" lods[]: \"/lod.pmd\" group_tags[]: human } }");
        Text("/def/world/mover_anim.sii","SiiNunit { mover_anim : .test { model_anim: \"/clip.pma\" group_tags[]: idle } }");
        hookup.Attributes["active_during_night"]=false;assert(Read()==baseRadius,"A hidden-at-night flag never establishes an absent mover");
        hookup.Attributes["active_during_night"]=true;assert(Read()==baseRadius,"Both activation states retain the full envelope");
        hookup.Attributes["active_during_night"]="true";assert(Rejected(()=>Read()),"Activation metadata requires an actual bool");hookup.Attributes["active_during_night"]=false;
        foreach(string field in new[]{"model","collision","animation"}) {hookup.Attributes[field]="/uninspected.pmd";assert(Rejected(()=>Read()),"Time metadata cannot conceal another physical model or animation");hookup.Attributes.Remove(field);}
        files.Data.Remove("/clip_l1.pma");assert(Rejected(()=>Read()),"Missing LOD animation cannot borrow the main skeleton's global bounds");
        files.Data["/clip_l1.pma"]=Clip(3);assert(Rejected(()=>Read()),"A different embedded LOD skeleton hash remains unknown");
        var moving=Clip(2);BinaryPrimitives.WriteUInt16LittleEndian(moving.AsSpan(6),2);files.Data["/clip_l1.pma"]=moving;
        assert(Rejected(()=>Read()),"Root movement in a lower LOD cannot disappear behind a stationary main clip");
    }
#if INTEGRATION
    static void Placement(Action<bool,string> assert,ChildGeometryIntegration.Files files,TruckLib.Sii.Unit hookup,
        Func<ulong,byte[]> header,MoverEnvelopeEvidence.Sphere envelope)
    {
        var child=new Locator {Position=new(4,2,-3),Scale=2,Rotation=Quaternion.CreateFromAxisAngle(Vector3.Normalize(new(1,2,3)),.7f),HookupOffset=0};
        var part=new Part("parent"){Locators=[child]};
        var model=new Model {Parts=[part],Variants=[new("default"){Attributes=[new(){Tag="visible",Value=1}]}]};
        var root=new TruckLib.Sii.Unit("lod_model_hookup","root");root.Attributes["model"]="/root.pmd";
        byte[] text=Encoding.ASCII.GetBytes(hookup.Name+"\0"),pmg=new byte[96+text.Length];pmg[0]=21;
        BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(88),96);BinaryPrimitives.WriteInt32LittleEndian(pmg.AsSpan(92),text.Length);text.CopyTo(pmg,96);
        foreach(float quaternionNorm in new[]{1f,1.005f}) {
            var parent=new Locator {Position=new(-2,3,8),Scale=3,Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,1.1f)*MathF.Sqrt(quaternionNorm)};
            var reader=new StaticHookupGeometry(files,new Dictionary<string,TruckLib.Sii.Unit>{{root.Name,root},{hookup.Name,hookup}},new HashSet<string>(),
                p=>p=="/root.pmd"?(model,pmg):(RigModel(),header(p=="/main.pmd"?1ul:2ul)));
            reader.Read(root.Name,parent);var sphere=reader.Movers.Single();
            var transform=StaticHookupGeometry.Transform(child)*StaticHookupGeometry.Transform(parent);
            assert(Vector3.Distance(sphere.Center,Vector3.Transform(envelope.Center,transform))<.0001,
                "The real nested child resolver transforms the nonzero centre through every rotation, scale and translation");
            assert(sphere.Radius>=envelope.Radius*6&&sphere.Radius<envelope.Radius*6*1.03+.02,
                "Nested unit rotations preserve radius, while accepted raw quaternion roundoff keeps its proved operator bound");
            for(int i=0;i<64;i++) {
                Vector3 direction=Vector3.Normalize(new(MathF.Sin(i*.7f)+.01f,MathF.Cos(i*.31f),MathF.Sin(i*.13f)));
                Vector3 point=Vector3.Transform(envelope.Center+direction*(float)envelope.Radius,transform);
                assert(Vector3.Distance(point,sphere.Center)<=sphere.Radius+.0001,
                    "All sampled surface points of the original complete sphere remain enclosed after actual nested placement");
            }
        }
    }
#endif
}
