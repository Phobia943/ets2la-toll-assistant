using System.Numerics;
using TruckLib.Models;

namespace MautAssistent;

// Positive evidence only: this captures the explicitly declared collision part
// of one static map model. It cannot declare a road/scene free of other objects.
// SCS map-object part semantics:
// https://modding.scssoft.com/wiki/Documentation/Tools/SCS_Blender_Tools/Part_system
internal static class StaticRoadModelGeometry
{
    internal sealed record Result(RigStaticGeometry? Geometry,string? Issue);

    internal static Result Capture(Model model,string variant,IReadOnlyList<string> extraParts,
        bool collision,Vector3 position,Quaternion rotation,Vector3 scale)
    {
        if(!collision)return new(null,"Map object collision disabled");
        if(!Finite(position)||!Finite(scale)||scale.X<=0||scale.Y<=0||scale.Z<=0||
            scale.X>100||scale.Y>100||scale.Z>100||!float.IsFinite(rotation.LengthSquared())||
            Math.Abs(rotation.LengthSquared()-1)>.00001f)return new(null,"Invalid map object transform");
        var selected=model.Variants.Where(v=>v.Name.ToString()==variant).ToArray();
        if(selected.Length!=1||selected[0].Attributes.Count!=model.Parts.Count||
            selected[0].Attributes.Any(a=>a.Type!=0||a.Tag.ToString()!="visible"||a.Value>1)||
            model.Parts.GroupBy(p=>p.Name).Any(g=>g.Count()!=1)||
            extraParts.Any(n=>!model.Parts.Any(p=>p.Name.ToString()==n)))
            return new(null,"Unknown map model variant or additional part");
        // Do not guess from coll_* names, visibility, material names or a model
        // bounding box. The documented exact `coll` part is the supported source.
        var index=model.Parts.FindIndex(p=>p.Name.ToString()=="coll");
        if(index<0)return new(null,"No explicit static collision part");
        var part=model.Parts[index];
        if(selected[0].Attributes[index].Value!=1&&!extraParts.Contains("coll",StringComparer.Ordinal))
            return new(null,"Static collision part inactive in this variant");
        if(model.Skeleton.Count>0||part.Locators.Count>0||part.Pieces.Count==0||
            part.Pieces.Any(p=>p.UseBoneIndexes||p.UseBoneWeights))
            return new(null,"Collision part is animated, attached or empty");
        try {
            var transform=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(position);
            // Keep this reader independent of the game/output assembly so it
            // can run in the isolated read-only scene process.
            var triangles=new List<StaticTriangle>();
            foreach(var piece in part.Pieces)foreach(var triangle in piece.Triangles) {
                if(triangle.A>=piece.Vertices.Count||triangle.B>=piece.Vertices.Count||triangle.C>=piece.Vertices.Count)
                    throw new InvalidDataException("Invalid model indices");
                var a=Vector3.Transform(piece.Vertices[triangle.A].Position,transform);
                var b=Vector3.Transform(piece.Vertices[triangle.B].Position,transform);
                var c=Vector3.Transform(piece.Vertices[triangle.C].Position,transform);
                if(!Finite(a)||!Finite(b)||!Finite(c))throw new InvalidDataException("Invalid model coordinates");
                triangles.Add(new(a,b,c));
                if(triangles.Count>20000)throw new InvalidDataException("Static mesh budget exceeded");
            }
            return new(new RigStaticGeometry(triangles),null);
        } catch(Exception e) when(e is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException) {
            return new(null,"Invalid static collision mesh: "+e.Message);
        }
    }
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
}
