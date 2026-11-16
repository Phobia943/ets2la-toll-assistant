using System.Numerics;
using System.Reflection;
using ETS2LA.Game.Data;
using ETS2LA.Game.Utils;
namespace MautAssistent;
internal static class PrefabModelTransform
{
    static readonly FieldInfo? origin=typeof(PrefabPath).GetField("prefabStart",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo? rotation=typeof(PrefabPath).GetField("rotationMatrix",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly MethodInfo clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!;
    internal static bool TryRead(PrefabPath input,out Matrix4x4 result)
    {
        result=default;var path=PrefabRefinement.OriginalFor(input);
        if(origin?.GetValue(path) is not Vector3 start||rotation?.GetValue(path) is not Matrix4x4 matrix||path.Curves.Count==0)return false;
        // Exact pinned host InterpolateDist: Transform(local + prefabStart,
        // rotationMatrix). Do not reverse translation/rotation composition.
        result=Matrix4x4.CreateTranslation(start)*matrix;
        if(!Matrix4x4.Invert(result,out _))return false;
        foreach(var curve in path.Curves.Take(1).Concat(path.Curves.TakeLast(1))) {
            var one=(PrefabPath)clone.Invoke(path,null)!;one.Curves=[curve];one.Length=curve.Length;
            foreach(float t in new[]{0f,.5f,1f}) {
                var local=PrefabUtils.InterpolateNavCurveOriented(curve,t).Position;
                var world=one.Interpolate(t,0)?.Position;
                if(world==null||!SceneSnapshotProtocol.Finite(world.Value)||Vector3.Distance(Vector3.Transform(local,result),world.Value)>.03f)return false;
            }
        }
        return true;
    }
}
