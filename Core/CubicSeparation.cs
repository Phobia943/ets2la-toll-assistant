using System.Numerics;
namespace MautAssistent;
public readonly record struct ObstacleSphere(Vector3 Center,float Radius);
public static class CubicSeparation
{
    // A cubic lies inside its Bezier control hull. Subdividing hulls therefore
    // gives a lower distance bound, unlike assuming samples cover the curve.
    // Inconclusive intervals remain blocked. Vertical separation is not used.
    public static bool Outside(Vector3 a,Vector3 b,Vector3 c,Vector3 d,ObstacleSphere obstacle,float halfWidth)
    {
        if(!float.IsFinite(obstacle.Radius)||obstacle.Radius<=0||!float.IsFinite(halfWidth)||halfWidth<0)return false;
        var values=new[]{a,b,c,d,obstacle.Center};
        if(values.Any(p=>!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z)||Math.Abs(p.X)>10000||Math.Abs(p.Z)>10000))return false;
        // Covers the bounded float interpolation/control reconstruction error
        // in local prefab coordinates. It can only enlarge the obstacle.
        double radius=obstacle.Radius+halfWidth+.1;
        bool Separated(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,int depth)
        {
            double lowX=Math.Min(Math.Min(p0.X,p1.X),Math.Min(p2.X,p3.X)),highX=Math.Max(Math.Max(p0.X,p1.X),Math.Max(p2.X,p3.X));
            double lowZ=Math.Min(Math.Min(p0.Z,p1.Z),Math.Min(p2.Z,p3.Z)),highZ=Math.Max(Math.Max(p0.Z,p1.Z),Math.Max(p2.Z,p3.Z));
            double dx=Math.Max(lowX-obstacle.Center.X,Math.Max(0,obstacle.Center.X-highX));
            double dz=Math.Max(lowZ-obstacle.Center.Z,Math.Max(0,obstacle.Center.Z-highZ));
            if(dx*dx+dz*dz>radius*radius)return true;
            if(depth==12)return false;
            var q0=(p0+p1)/2;var q1=(p1+p2)/2;var q2=(p2+p3)/2;
            var r0=(q0+q1)/2;var r1=(q1+q2)/2;var mid=(r0+r1)/2;
            return Separated(p0,q0,r0,mid,depth+1)&&Separated(mid,r1,q2,p3,depth+1);
        }
        return Separated(a,b,c,d,0);
    }
    public static (Vector3 A,Vector3 B,Vector3 C,Vector3 D) FromCubicSamples(Vector3 p0,Vector3 oneThird,Vector3 twoThirds,Vector3 p1)
    {
        var x=27*oneThird-8*p0-p1;var y=27*twoThirds-p0-8*p1;
        return(p0,(2*x-y)/18,(2*y-x)/18,p1);
    }
}
