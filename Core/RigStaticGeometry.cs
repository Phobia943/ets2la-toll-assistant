using System.Numerics;
using System.Runtime.CompilerServices;
namespace MautAssistent;

public readonly record struct StaticTriangle(Vector3 A,Vector3 B,Vector3 C);
public readonly record struct RigGroundPlane(Vector3 Point,Vector3 Normal);
public sealed class RigStaticGeometry
{
    sealed record Face(StaticTriangle Triangle,Vector3 Minimum,Vector3 Maximum);
    sealed record Node(Vector3 Minimum,Vector3 Maximum,int Start,int Count,Node? Left,Node? Right);
    readonly Face[] faces;
    readonly Node tree;
    public IReadOnlyList<StaticTriangle> Triangles {get;}
    public int Count=>faces.Length;
    public RigStaticGeometry(IEnumerable<StaticTriangle> source)
    {
        var triangles=source.Take(500001).ToArray();
        if(triangles.Length is <1 or >500000||triangles.Any(t=>!Finite(t.A)||!Finite(t.B)||!Finite(t.C)))
            throw new InvalidDataException("Ungültige oder zu große statische Mautgeometrie");
        Triangles=Array.AsReadOnly(triangles);
        faces=triangles.Select(t=>new Face(t,Vector3.Min(t.A,Vector3.Min(t.B,t.C)),Vector3.Max(t.A,Vector3.Max(t.B,t.C)))).ToArray();
        tree=Build(0,faces.Length);
    }
    Node Build(int start,int count)
    {
        var min=faces[start].Minimum;var max=faces[start].Maximum;
        for(int i=start+1;i<start+count;i++){min=Vector3.Min(min,faces[i].Minimum);max=Vector3.Max(max,faces[i].Maximum);}
        if(count<=16)return new(min,max,start,count,null,null);
        var size=max-min;int axis=size.X>=size.Y&&size.X>=size.Z?0:size.Y>=size.Z?1:2;
        float Component(Vector3 p)=>axis==0?p.X:axis==1?p.Y:p.Z;
        Array.Sort(faces,start,count,Comparer<Face>.Create((a,b)=>(Component(a.Minimum)+Component(a.Maximum)).CompareTo(Component(b.Minimum)+Component(b.Maximum))));
        int half=count/2;return new(min,max,start,0,Build(start,half),Build(start+half,count-half));
    }
    public sealed record CheckResult(bool Clear,double? ConflictAlong,string? Issue,int FaceChecks);
    // World-space, explicitly active static coll meshes only. A nominal corridor
    // overlap is an ordinary planned stop candidate, never acute hazard proof.
    public CheckResult CheckNominal(StaticRoadCorridor corridor,int budget=30000)
    {
        if(corridor==null||budget is <1 or >30000)return new(false,null,"Statischer Standardkorridor fehlt",0);
        int work=0;
        foreach(var segment in corridor.Segments) {
            bool Visit(Node node) {
                if(++work>budget)throw new WorkLimit();
                if(!Overlap(segment.Minimum,segment.Maximum,node.Minimum,node.Maximum))return false;
                if(node.Left!=null)return Visit(node.Left)||Visit(node.Right!);
                Span<Vector3> vertices=stackalloc Vector3[3];
                Span<Vector3> polygon=stackalloc Vector3[4];
                for(int i=node.Start;i<node.Start+node.Count;i++) {
                    if(++work>budget)throw new WorkLimit();
                    var face=faces[i];
                    if(!Overlap(segment.Minimum,segment.Maximum,face.Minimum,face.Maximum))continue;
                    // A collapsed triangle has no physical collision surface.
                    // The general rig checker retains its existing contract.
                    if(Vector3.Cross(face.Triangle.B-face.Triangle.A,face.Triangle.C-face.Triangle.A).LengthSquared()<=1e-12f)continue;
                    vertices[0]=face.Triangle.A;vertices[1]=face.Triangle.B;vertices[2]=face.Triangle.C;
                    int count=AboveGround(vertices,segment.Ground,polygon);
                    for(int j=1;j+1<count;j++)
                        if(TriangleOverlap(segment.Box,0,Vector3.Zero,Vector3.Zero,polygon[0],polygon[j],polygon[j+1]))return true;
                }
                return false;
            }
            try {if(Visit(tree))return new(false,segment.Along,null,work);}
            catch(WorkLimit){return new(false,null,"Arbeitsbudget der statischen Standardprüfung erreicht",work);}
        }
        return new(true,null,null,work);
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static CheckResult CheckMovers(RigPredictionResult prediction,IReadOnlyList<ObstacleSphere> movers,Matrix4x4 modelToWorld)
    {
        if(prediction is not{Supported:true}||prediction.Cells.Count==0||prediction.Cells.Any(c=>!Valid(c))||movers.Count>512||
            !Finite(modelToWorld)||!Matrix4x4.Decompose(modelToWorld,out var scale,out var rotation,out var origin))
            return new(false,null,"Bewegte Modellgeometrie nicht an die Gespannvorhersage gebunden",0);
        var rebuilt=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation))*Matrix4x4.CreateTranslation(origin);
        double residual=Math.Sqrt(new[]{modelToWorld.M11-rebuilt.M11,modelToWorld.M12-rebuilt.M12,modelToWorld.M13-rebuilt.M13,
            modelToWorld.M21-rebuilt.M21,modelToWorld.M22-rebuilt.M22,modelToWorld.M23-rebuilt.M23,
            modelToWorld.M31-rebuilt.M31,modelToWorld.M32-rebuilt.M32,modelToWorld.M33-rebuilt.M33}.Sum(v=>(double)v*v));
        double factor=Math.Max(Math.Abs(scale.X),Math.Max(Math.Abs(scale.Y),Math.Abs(scale.Z)))+residual;
        if(!double.IsFinite(factor)||factor is <=0 or >100||Math.Abs(modelToWorld.M14)+Math.Abs(modelToWorld.M24)+Math.Abs(modelToWorld.M34)+Math.Abs(modelToWorld.M44-1)>1e-6)
            return new(false,null,"Ungültiger Modellmaßstab der bewegten Geometrie",0);
        int checks=0;
        foreach(var sphere in movers) {
            if(!Finite(sphere.Center)||!float.IsFinite(sphere.Radius)||sphere.Radius is <=0 or >100)return new(false,null,"Ungültige bewegte Modellhülle",checks);
            var center=Vector3.Transform(sphere.Center,modelToWorld);double radius=sphere.Radius*factor+.01;
            foreach(var cell in prediction.Cells) {
                foreach(var pair in new[]{(cell.Truck,cell.TruckMotionReserve,cell.TruckSourceTranslation),(cell.Trailer,cell.TrailerMotionReserve,cell.TrailerSourceTranslation)}) {
                    checks++;var (body,reserve,source)=pair;var inverse=Quaternion.Inverse(Quaternion.Normalize(body.Rotation));
                    var p=Vector3.Transform(center-body.Position,inverse)-(body.Minimum+body.Maximum)/2;
                    var span=Vector3.Transform(cell.HitchTranslationHalf,inverse);
                    if(SegmentBoxDistance(p-span,p+span,(body.Maximum-body.Minimum)/2+Vector3.Abs(Vector3.Transform(source,inverse)))<=radius+reserve+.003)
                        return new(false,cell.AlongStart,null,checks);
                }
            }
        }
        return new(true,null,null,checks);
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static double SegmentBoxDistance(Vector3 from,Vector3 to,Vector3 half)
    {
        double[] a=[from.X,from.Y,from.Z],d=[(double)to.X-from.X,(double)to.Y-from.Y,(double)to.Z-from.Z],h=[half.X,half.Y,half.Z];
        var cuts=new List<double>{0,1};
        for(int i=0;i<3;i++)if(Math.Abs(d[i])>1e-12)foreach(double edge in new[]{-h[i],h[i]}){double t=(edge-a[i])/d[i];if(t is >0 and <1)cuts.Add(t);}
        cuts.Sort();double best=double.PositiveInfinity;
        double At(double t)=>Enumerable.Range(0,3).Sum(i=>Math.Pow(Math.Max(0,Math.Abs(a[i]+d[i]*t)-h[i]),2));
        for(int n=1;n<cuts.Count;n++) {
            double low=cuts[n-1],high=cuts[n],mid=(low+high)/2,numerator=0,denominator=0;
            for(int i=0;i<3;i++){double point=a[i]+d[i]*mid;if(Math.Abs(point)<=h[i])continue;double edge=Math.Sign(point)*h[i];numerator+=d[i]*(a[i]-edge);denominator+=d[i]*d[i];}
            best=Math.Min(best,Math.Min(At(low),At(high)));if(denominator>0)best=Math.Min(best,At(Math.Clamp(-numerator/denominator,low,high)));
        }
        return Math.Sqrt(best);
    }
    // The input is a motion-model sweep, not all possible physical motions.
    // Ground contact below route height + 0.3m retains the previous static
    // model's support-surface exclusion; low-curb avoidance is outside scope.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public CheckResult Check(RigPredictionResult prediction,Matrix4x4 modelToWorld,Func<double,double?> roadHeight,int budget=1500000,
        Func<RigSweptCell,VehicleBody,double?>? bodyRoadHeight=null,Func<RigSweptCell,VehicleBody,RigGroundPlane?>? bodyRoadPlane=null)
    {
        if(prediction is not {Supported:true}||prediction.Cells.Count==0||prediction.Cells.Any(c=>!Valid(c))||budget is <1 or >5000000||!Finite(modelToWorld)||
            Math.Abs(modelToWorld.M14)>1e-6||Math.Abs(modelToWorld.M24)>1e-6||Math.Abs(modelToWorld.M34)>1e-6||Math.Abs(modelToWorld.M44-1)>1e-6||
            !Matrix4x4.Invert(modelToWorld,out var inverse)||!Finite(inverse))return new(false,null,"Statische Gespannprüfung ohne gültige Transformation oder Vorhersage",0);
        int checkedFaces=0;
        foreach(var cell in prediction.Cells) {
            foreach(var pair in new[]{(cell.Truck,cell.TruckMotionReserve,cell.TruckSourceTranslation),(cell.Trailer,cell.TrailerMotionReserve,cell.TrailerSourceTranslation)}) {
                var (body,reserve,source)=pair;
                double? ground=bodyRoadHeight!=null?bodyRoadHeight(cell,body):roadHeight((cell.AlongStart+cell.AlongEnd)/2);
                if(ground is not double level||!double.IsFinite(level))return new(false,cell.AlongStart,"Straßenhöhe unter dem jeweiligen Gespannkörper fehlt",checkedFaces);
                var requested=bodyRoadPlane?.Invoke(cell,body);
                if(bodyRoadPlane!=null&&requested==null)return new(false,cell.AlongStart,"Straßenebene unter dem jeweiligen Körper nicht verfolgt",checkedFaces);
                var plane=requested??new RigGroundPlane(new(0,(float)level,0),Vector3.UnitY);
                if(!Finite(plane.Point)||!Finite(plane.Normal)||Math.Abs(plane.Normal.LengthSquared()-1)>.001f||plane.Normal.Y<.99f)
                    return new(false,cell.AlongStart,"Nachgewiesene Straßenebene unter dem Gespann fehlt",checkedFaces);
                var span=Vector3.Abs(cell.HitchTranslationHalf)+Vector3.Abs(source)+new Vector3((float)reserve+.01f);
                var corners=body.Corners().ToArray();var lo=corners.Aggregate(Vector3.Min)-span;var hi=corners.Aggregate(Vector3.Max)+span;
                var local=Enumerable.Range(0,8).Select(i=>Vector3.Transform(new Vector3((i&1)==0?lo.X:hi.X,(i&2)==0?lo.Y:hi.Y,(i&4)==0?lo.Z:hi.Z),inverse)).ToArray();
                var min=local.Aggregate(Vector3.Min);var max=local.Aggregate(Vector3.Max);
                [MethodImpl(MethodImplOptions.AggressiveOptimization)]
                bool Visit(Node node) {
                    if(!Overlap(min,max,node.Minimum,node.Maximum))return false;
                    if(node.Left!=null)return Visit(node.Left)||Visit(node.Right!);
                    // One bounded stack buffer per leaf invocation, reused for
                    // every face. Never stackalloc inside the face loop.
                    Span<Vector3> vertices=stackalloc Vector3[3];
                    Span<Vector3> polygon=stackalloc Vector3[4];
                    for(int i=node.Start;i<node.Start+node.Count;i++) {
                        var face=faces[i];if(!Overlap(min,max,face.Minimum,face.Maximum))continue;
                        if(++checkedFaces>budget)throw new WorkLimit();
                        var t=face.Triangle;
                        vertices[0]=Vector3.Transform(t.A,modelToWorld);vertices[1]=Vector3.Transform(t.B,modelToWorld);vertices[2]=Vector3.Transform(t.C,modelToWorld);
                        int count=AboveGround(vertices,plane,polygon);
                        for(int n=1;n+1<count;n++)
                            if(TriangleOverlap(body,reserve,cell.HitchTranslationHalf,source,polygon[0],polygon[n],polygon[n+1]))return true;
                    }
                    return false;
                }
                try {if(Visit(tree))return new(false,cell.AlongStart,null,checkedFaces);}
                catch(WorkLimit){return new(false,cell.AlongStart,"Arbeitsbudget der statischen Gespannprüfung erreicht",checkedFaces);}
            }
        }
        return new(true,null,null,checkedFaces);
    }
    sealed class WorkLimit:Exception { }
    static bool Valid(RigSweptCell c)
    {
        bool Body(VehicleBody b)=>b!=null&&Finite(b.Position)&&Finite(b.Minimum)&&Finite(b.Maximum)&&
            b.Maximum.X>b.Minimum.X&&b.Maximum.Y>b.Minimum.Y&&b.Maximum.Z>b.Minimum.Z&&
            float.IsFinite(b.Rotation.LengthSquared())&&Math.Abs(b.Rotation.LengthSquared()-1)<.01;
        return c!=null&&Body(c.Truck)&&Body(c.Trailer)&&Finite(c.HitchTranslationHalf)&&Finite(c.TruckSourceTranslation)&&Finite(c.TrailerSourceTranslation)&&
            c.TruckSourceTranslation.Length()<=5&&c.TrailerSourceTranslation.Length()<=5&&
            double.IsFinite(c.TruckMotionReserve)&&c.TruckMotionReserve is >=0 and <=100&&
            double.IsFinite(c.TrailerMotionReserve)&&c.TrailerMotionReserve is >=0 and <=100&&
            double.IsFinite(c.AlongStart)&&double.IsFinite(c.AlongEnd)&&c.AlongEnd>=c.AlongStart;
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int AboveGround(ReadOnlySpan<Vector3> input,RigGroundPlane plane,Span<Vector3> output)
    {
        int count=0;
        for(int i=0;i<input.Length;i++) {
            var a=input[i];var b=input[(i+1)%input.Length];float da=Vector3.Dot(a-plane.Point,plane.Normal)-.3f,db=Vector3.Dot(b-plane.Point,plane.Normal)-.3f;bool ain=da>=0,bin=db>=0;
            if(ain)output[count++]=a;
            if(ain!=bin)output[count++]=Vector3.Lerp(a,b,da/(da-db));
        }
        return count;
    }
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static bool TriangleOverlap(VehicleBody body,double reserve,Vector3 translation,Vector3 source,Vector3 a,Vector3 b,Vector3 c)
    {
        var q=Quaternion.Normalize(body.Rotation);var inverse=Quaternion.Inverse(q);
        Vector3 Local(Vector3 p)=>Vector3.Transform(p-body.Position,inverse)-(body.Minimum+body.Maximum)/2;
        a=Local(a);b=Local(b);c=Local(c);translation=Vector3.Transform(translation,inverse);source=Vector3.Transform(source,inverse);
        var half=(body.Maximum-body.Minimum)/2;var e0=b-a;var e1=c-b;var e2=a-c;
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        bool Separate(Vector3 axis) {
            double length=Math.Sqrt(Dot(axis,axis));if(length<1e-10)return false;
            double radius=Math.Abs(axis.X)*half.X+Math.Abs(axis.Y)*half.Y+Math.Abs(axis.Z)*half.Z+
                Math.Abs(Dot(axis,translation))+Math.Abs(Dot(axis,source))+(reserve+.003)*length;
            double pa=Dot(axis,a),pb=Dot(axis,b),pc=Dot(axis,c);
            return Math.Min(pa,Math.Min(pb,pc))>radius||Math.Max(pa,Math.Max(pb,pc))< -radius;
        }
        if(Separate(Vector3.UnitX)||Separate(Vector3.UnitY)||Separate(Vector3.UnitZ)||Separate(Vector3.Cross(e0,e1)))return false;
        for(int i=0;i<3;i++) {
            var edge=i==0?e0:i==1?e1:e2;
            if(Separate(Vector3.Cross(edge,Vector3.UnitX))||Separate(Vector3.Cross(edge,Vector3.UnitY))||Separate(Vector3.Cross(edge,Vector3.UnitZ)))return false;
        }
        return true;
    }
    static double Dot(Vector3 a,Vector3 b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;
    static bool Overlap(Vector3 a,Vector3 b,Vector3 c,Vector3 d)=>a.X<=d.X&&b.X>=c.X&&a.Y<=d.Y&&b.Y>=c.Y&&a.Z<=d.Z&&b.Z>=c.Z;
    static bool Finite(Vector3 p)=>SceneSnapshotProtocol.Finite(p)&&Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z)))<=1e7;
    static bool Finite(Matrix4x4 m)=>new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}.All(v=>float.IsFinite(v)&&Math.Abs(v)<=1e7);
}
