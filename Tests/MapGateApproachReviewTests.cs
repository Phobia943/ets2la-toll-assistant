using System.Numerics;
using MautAssistent;

internal static class MapGateApproachReviewTests
{
    internal static IReadOnlyList<TraceSlicePoint> Straight(float x=0,bool reverse=false)=>Enumerable.Range(0,241).Select(i=>
        new TraceSlicePoint(new(x,0,reverse?120-i*.5f:i*.5f),reverse?-Vector3.UnitZ:Vector3.UnitZ,i*.5,"gate-road",false)).ToArray();
    internal static MapGateArea Area(ulong uid=1,float z=40,float x=3)=>new(uid,new(x,0,z),Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),
        new(x-3,0,z-4),Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI),5,12);
    internal static void Run(Action<bool,string> check)
    {
        var gate=Area();var points=Straight();var hit=MapGateApproach.Bind(gate,points,0);
        check(hit is {PlaneAlong:>39.99 and <40.01,StopAlong:>34.99 and <35.01},"Standalone post3m beside route binds through its explicit activation area and stops5m before its plane");
        check(MapGateApproach.Bind(gate,Straight(6),0)==null&&MapGateApproach.Bind(gate,Straight(reverse:true),0)==null,
            "Neighbor lane and opposing direction do not borrow a nearby gate");
        check(MapGateApproach.Bind(gate with{Activation=gate.Anchor+new Vector3(-3,0,4)},points,0)==null,
            "Activation area behind the gate cannot lend an approach");
        check(MapGateApproach.Bind(gate with{Anchor=gate.Anchor+new Vector3(0,5,0)},points,0)==null,
            "Another bridge/deck does not become the route's gate");
        var turning=points.Select(p=>p.Position.Z>37?p with{Position=p.Position+new Vector3((p.Position.Z-37)*.7f,0,0),Direction=Vector3.Normalize(new Vector3(.7f,0,1))}:p).ToArray();
        double progress=0;for(int i=1;i<turning.Length;i++){progress+=Vector3.Distance(turning[i-1].Position,turning[i].Position);turning[i]=turning[i] with{Along=progress};}
        check(MapGateApproach.Bind(gate,turning,0)==null,"Turning away after the activation area is not a passage through that gate");
        check(MapGateApproach.Bind(gate,points.Take(80).ToArray(),0)==null,"A route ending before the gate cannot confirm its traversal");
        var late=points.Skip(75).ToArray();check(MapGateApproach.Bind(gate,late,late[0].Along)!=null,"A current route beginning inside the explicit area still binds before its gate plane");
        var stopped=points.Skip(70).ToArray();check(MapGateApproach.Bind(gate,stopped,stopped[0].Along)!=null,
            "After the planned stop, a new forward-only query still binds and can accept fresh open3 evidence");
        check(MapGateApproach.Bind(gate with{Length=2},points,0)==null,"An area ending before the gate plane cannot create a stop that loses its later opening binding");
        check(MapGateApproach.Bind(gate,points.Skip(82).ToArray(),41)==null,"An already passed gate does not create a new backward stop");
        var second=MapGateApproach.Bind(Area(2,60),points,0);check(second!=null&&second.PlaneAlong>hit!.PlaneAlong,"Serial gates retain separate stop identities/progress");
        check(MapGateApproach.Bind(gate with{Rotation=new(float.NaN,0,0,1)},points,0)==null&&
            MapGateApproach.Bind(gate,points.Select((p,i)=>i==75?p with{Direction=new(float.NaN,0,1)}:p).ToArray(),0)==null,
            "Malformed rotations/directions never authorize a gate binding");
    }
}
