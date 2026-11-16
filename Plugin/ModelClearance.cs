using System.Numerics;
using ETS2LA.Game.Utils;
using TruckLib.Models;
using TruckLib.Models.Ppd;

namespace MautAssistent;

/// <summary>Static model geometry, not a measurement of the current vehicle.</summary>
public static class ModelClearance
{
    // Conservative corridor for the supported regular truck combination.
    // Wider/taller loads need a larger profile and separate validation.
    public const float Height = 6;
    public const float Width = 3;
    readonly record struct Face(Vector3 A,Vector3 B,Vector3 C,Vector3 Min,Vector3 Max);

    public static RigStaticGeometry CaptureGeometry(IEnumerable<Part> parts,IEnumerable<(Part Part,Matrix4x4 Transform)>? children=null)
    {
        var triangles=new List<StaticTriangle>();
        foreach(var instance in parts.Select(p=>(Part:p,Transform:Matrix4x4.Identity)).Concat(children??[]))
        foreach(var piece in instance.Part.Pieces) {
            if(piece.UseBoneIndexes||piece.UseBoneWeights)
                throw new InvalidDataException("Animierte Mautgeometrie ist nicht statisch freigebbar.");
            foreach(var triangle in piece.Triangles) {
                if(triangle.A>=piece.Vertices.Count||triangle.B>=piece.Vertices.Count||triangle.C>=piece.Vertices.Count)
                    throw new InvalidDataException("Ungültige Maut-Modellindizes.");
                var a=Vector3.Transform(piece.Vertices[triangle.A].Position,instance.Transform);
                var b=Vector3.Transform(piece.Vertices[triangle.B].Position,instance.Transform);
                var c=Vector3.Transform(piece.Vertices[triangle.C].Position,instance.Transform);
                if(!Finite(a)||!Finite(b)||!Finite(c))throw new InvalidDataException("Ungültige Maut-Modellkoordinaten.");
                triangles.Add(new(a,b,c));
                if(triangles.Count>500000)throw new InvalidDataException("Maut-Geometrie überschreitet die Prüfgrenze.");
            }
        }
        return new(triangles);
    }
    public static bool[] BlockedCurves(PrefabDescriptor descriptor,IEnumerable<Part> parts,
        float height=Height,float width=Width,IEnumerable<(Part Part,Matrix4x4 Transform)>? children=null,RigStaticGeometry? prepared=null)
    {
        if(!float.IsFinite(height)||!float.IsFinite(width)||height<=.3f||width<=0)
            throw new InvalidDataException("Ungültiger Maut-Prüfkorridor.");
        var faces=(prepared??CaptureGeometry(parts,children)).Triangles.Select(t=>new Face(t.A,t.B,t.C,
            Vector3.Min(t.A,Vector3.Min(t.B,t.C)),Vector3.Max(t.A,Vector3.Max(t.B,t.C)))).ToArray();
        var blocked=new bool[descriptor.NavCurves.Count];
        for(int index=0;index<blocked.Length;index++) {
            var curve=descriptor.NavCurves[index];
            if(!float.IsFinite(curve.Length)||curve.Length<=0)throw new InvalidDataException("Ungültige Maut-Spurkurve.");
            int count=(int)Math.Ceiling(curve.Length*2);
            if(count>4000)throw new InvalidDataException("Maut-Spurkurve überschreitet die Geometriegrenze.");
            count=Math.Max(4,count);
            var previous=PrefabUtils.InterpolateNavCurveOriented(curve,0).Position;
            for(int step=1;step<=count;step++) {
                var next=PrefabUtils.InterpolateNavCurveOriented(curve,(float)step/count).Position;
                if(!Finite(previous)||!Finite(next))throw new InvalidDataException("Maut-Spurkurve nicht auswertbar.");
                var heading=new Vector3(next.X-previous.X,0,next.Z-previous.Z);float length=heading.Length();
                if(length<.00001f){previous=next;continue;}
                var forward=heading/length;var right=Vector3.Cross(Vector3.UnitY,forward);
                var center=(previous+next)/2+Vector3.UnitY*((height+.3f)/2);
                var half=new Vector3(width/2,(height-.3f)/2+Math.Abs(next.Y-previous.Y)/2,length/2+.05f);
                var bounds=new Vector3(Math.Abs(right.X)*half.X+Math.Abs(forward.X)*half.Z,half.Y,
                    Math.Abs(right.Z)*half.X+Math.Abs(forward.Z)*half.Z);
                Vector3 Local(Vector3 p){p-=center;return new(Vector3.Dot(p,right),p.Y,Vector3.Dot(p,forward));}
                foreach(var face in faces) {
                    if(face.Max.X<center.X-bounds.X||face.Min.X>center.X+bounds.X||
                       face.Max.Y<center.Y-bounds.Y||face.Min.Y>center.Y+bounds.Y||
                       face.Max.Z<center.Z-bounds.Z||face.Min.Z>center.Z+bounds.Z)continue;
                    if(!TriangleBox(Local(face.A),Local(face.B),Local(face.C),half))continue;
                    blocked[index]=true;break;
                }
                if(blocked[index])break;
                previous=next;
            }
        }
        return blocked;
    }

    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
    internal static bool TriangleBox(Vector3 a,Vector3 b,Vector3 c,Vector3 half)
    {
        var edges=new[]{b-a,c-b,a-c};
        bool Separated(Vector3 axis) {
            if(axis.LengthSquared()<1e-12f)return false;
            float pa=Vector3.Dot(a,axis),pb=Vector3.Dot(b,axis),pc=Vector3.Dot(c,axis);
            float radius=half.X*Math.Abs(axis.X)+half.Y*Math.Abs(axis.Y)+half.Z*Math.Abs(axis.Z);
            return Math.Min(pa,Math.Min(pb,pc))>radius||Math.Max(pa,Math.Max(pb,pc))<-radius;
        }
        var axes=new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ};
        if(axes.Any(Separated)||Separated(Vector3.Cross(edges[0],edges[1])))return false;
        return !edges.Any(edge=>axes.Any(axis=>Separated(Vector3.Cross(edge,axis))));
    }
}
