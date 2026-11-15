using AdaptiveCruiseControl;
using ETS2LA.Game.Output;
using ETS2LA.State;
using PIDLib;
namespace MautHostCompatibility;
#pragma warning disable CS0649,CS0414
internal sealed class AccCoastTemplate
{
    float currentSpeed,currentAcceleration,pedalOutput,pidOutput;
    Dictionary<string,float> constraintValues=null!;
    PIDController? accelerationPID,decelerationPID;
    public void MautApplyCoast(ControlVariables variables,long epoch)
    {
        if(ACCSettings.Current.IgnoredConstraints.Count!=0||
            !OutputTemplate.MautCanCoast(epoch,currentSpeed,ApplicationState.Current.DesiredSpeed,currentAcceleration,constraintValues))return;
        variables.aforward=0;variables.abackward=0;
        pedalOutput=pidOutput=0;
        accelerationPID?.Reset();decelerationPID?.Reset();
    }
}
