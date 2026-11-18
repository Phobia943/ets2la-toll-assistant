using System.Numerics;
using MautAssistent;
using TruckLib.Models;

internal static class StaticRoadModelReviewTests
{
    internal static void Run(Action<bool,string> check)
    {
        var model=new Model {Parts=[new("vis"),new("coll")],Variants=[new("default"){Attributes=[
            new(){Type=0,Tag="visible",Value=1},new(){Type=0,Tag="visible",Value=1}]}]};
        model.Parts[0].Pieces.Add(new(){Vertices=[new(new Vector3(-100,0,-100)),new(new Vector3(100,0,0)),new(new Vector3(0,0,100))],Triangles=[new(0,1,2)]});
        model.Parts[1].Pieces.Add(new(){Vertices=[new(new Vector3(1,0,0)),new(new Vector3(1,2,0)),new(new Vector3(1,0,2))],Triangles=[new(0,1,2)]});
        var rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2);
        var result=StaticRoadModelGeometry.Capture(model,"default",[],true,new(10,20,30),rotation,new(2,3,4));
        check(result is {Issue:null,Geometry.Count:1},"Only the explicit coll mesh must be captured, excluding the visible ground/decal");
        var triangle=result.Geometry!.Triangles.Single();
        check(Vector3.Distance(triangle.A,new(10,20,28))<.001&&Vector3.Distance(triangle.B,new(10,26,28))<.001&&
            Vector3.Distance(triangle.C,new(18,20,28))<.001,"Nonuniform scale, rotation and world placement must be applied in that order");
        StaticRoadModelGeometry.Result Capture(string variant="default",bool collision=true,Vector3? scale=null)=>
            StaticRoadModelGeometry.Capture(model,variant,[],collision,Vector3.Zero,Quaternion.Identity,scale??Vector3.One);
        check(Capture("unknown").Geometry==null&&!string.IsNullOrEmpty(Capture("unknown").Issue),"Unknown variant must remain unknown, never free");
        check(Capture(collision:false).Geometry==null,"Disabled collision must not publish a physical obstacle");
        check(Capture(scale:new(float.NaN,1,1)).Geometry==null&&Capture(scale:new(-1,1,1)).Geometry==null,"Invalid transforms must not invent collision geometry");
        model.Variants[0].Attributes[1].Value=0;
        check(Capture().Geometry==null,"Inactive collision part must not become a phantom obstacle");
        check(StaticRoadModelGeometry.Capture(model,"default",["coll"],true,Vector3.Zero,Quaternion.Identity,Vector3.One).Geometry!=null,"Explicit additional coll part may enable collision");
        model.Variants[0].Attributes[1].Value=1;model.Parts[1].Name="coll_work";
        check(Capture().Geometry==null,"Undocumented coll_* name must not be promoted into collision evidence");
        model.Parts[1].Name="coll";model.Parts[1].Pieces[0].UseBoneWeights=true;
        check(Capture().Geometry==null,"Animated collision must not be frozen into a static obstacle");
        model.Parts[1].Pieces[0].UseBoneWeights=false;model.Parts[1].Pieces[0].Triangles=[new(0,1,99)];
        check(Capture().Geometry==null,"Malformed collision triangles must reject the source");
    }
}
