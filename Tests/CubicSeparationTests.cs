using System.Numerics;
using MautAssistent;
public static class CubicSeparationTests
{
    public static void Bounds(Action<bool,string> assert)
    {
        var a=new Vector3(0,0,0);var b=new Vector3(0,0,10);var c=new Vector3(0,0,20);var d=new Vector3(0,0,30);
        assert(CubicSeparation.Outside(a,b,c,d,new(new(20,0,15),6),1.7f),"Far parking sphere cannot invalidate the full plaza");
        assert(!CubicSeparation.Outside(a,b,c,d,new(new(7,0,15),6),1.7f),"Own rig width is included in a near parking sphere");
        assert(!CubicSeparation.Outside(a,b,c,d,new(new(0,100,15),6),1.7f),"Unknown vertical dynamics cannot create a horizontal pass");
        var controls=CubicSeparation.FromCubicSamples(a,b,c,d);
        assert(Vector3.Distance(controls.B,b)<.001&&Vector3.Distance(controls.C,c)<.001,"Cubic controls are reconstructed from actual interpolation samples");
        // The obstacle lies between sparse endpoint samples; convex-hull
        // subdivision must still retain the real bent curve.
        b=new(25,0,10);c=new(25,0,20);
        Vector3 At(float t)=>MathF.Pow(1-t,3)*a+3*(1-t)*(1-t)*t*b+3*(1-t)*t*t*c+t*t*t*d;
        var hit=At(.47f);
        var reconstructed=CubicSeparation.FromCubicSamples(At(0),At(1f/3),At(2f/3),At(1));
        assert(Vector3.Distance(reconstructed.B,b)<.001&&Vector3.Distance(reconstructed.C,c)<.001,"Nonlinear cubic reconstruction retains both actual control points");
        assert(!CubicSeparation.Outside(a,b,c,d,new(hit,.1f),.1f),"Unsampled interior intersections cannot be cleared");
        assert(CubicSeparation.Outside(a,b,c,d,new(new(-10,0,15),1),1),"AABB subdivision can prove separation beside a bend");
        for(int i=0;i<=100;i++)assert(!CubicSeparation.Outside(a,b,c,d,new(At(i/100f),.01f),.01f),"Every curve point remains enclosed");
        assert(!CubicSeparation.Outside(a,b,c,d,new(new(float.NaN,0,0),1),1),"Invalid sphere data cannot grant clearance");
        assert(!CubicSeparation.Outside(Vector3.Zero,new(0,0,10),new(0,0,20),new(0,0,30),new(new(3.1f,0,15),1),2),"Exact inflated tangency stays blocked");
        foreach(float shift in new[]{-9990f,9990f}) {
            var offset=new Vector3(shift,0,0);
            assert(!CubicSeparation.Outside(offset,offset+new Vector3(0,0,10),offset+new Vector3(0,0,20),offset+new Vector3(0,0,30),new(offset+new Vector3(0,0,15),1),1),"Large valid local coordinates preserve a real intersection");
        }
        assert(!CubicSeparation.Outside(new(10001,0,0),b,c,d,new(new(0,0,0),1),1),"Coordinates outside the proved rounding range cannot grant separation");
    }
}
