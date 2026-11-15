using MautAssistent;

public static class CruisePolicyTests
{
    public static void Limits(Action<bool,string> assert)
    {
        assert(CruiseLimitPolicy.Preferred(100/3.6,new(80/3.6,false),double.NaN)==80/3.6,"Configured host maximum caps the automatic target");
        assert(CruiseLimitPolicy.Preferred(100/3.6,new(0,true),70/3.6)==70/3.6,"Enabled road limits cap the automatic target");
        assert(CruiseLimitPolicy.Preferred(100/3.6,new(0,true),0)==null,"Unknown road limit cannot authorize a higher automatic target");
        assert(CruiseLimitPolicy.Preferred(100/3.6,new(0,false),double.NaN)==100/3.6,"Explicit disabled road-limit rule and no host maximum retain the requested target");
        assert(CruiseLimitPolicy.Preferred(double.NaN,new(0,false),0)==null,"Invalid preferred speed is refused");
    }
}
