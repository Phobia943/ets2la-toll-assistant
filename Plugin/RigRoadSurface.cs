using System.Numerics;
using System.Runtime.CompilerServices;
namespace MautAssistent;
internal static class RigRoadSurface
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static RigGroundPlane? ForBody(RouteTrace trace,RigSweptCell cell,VehicleBody body)
        =>RigRoadPrediction.GroundForBody(trace,cell,body);
}
