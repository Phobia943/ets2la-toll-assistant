using System.Numerics;
using System.Reflection;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.ScsMap;
using TruckLib.Sii;

internal static class MapGateSourceReviewTests
{
    const string Id="universal.rect.manual.border.truck_only";
    const string Fields="interaction_mode: \"ai_and_player\"\nautomatic_trigger: false\none_way: true\nmodel: \"/model/activation/border.pmd\"\nbus_allowed: false\ncar_allowed: false\ntrigger_length: 12\ntrigger_width: 5";
    static string Definition(string fields=Fields)=>"SiiNunit\n{\ngate_item_trigger: "+Id+"\n{\n"+fields+"\n}\n}";
    internal static void Run(Action<bool,string> check)
    {
        var assets=new Assets();assets.Data["/unit/hookup/gates.sii"]=Definition();
        var profiles=MapGateSource.ReadProfiles(assets);
        check(profiles[Id]==new MapGateSource.Trigger(5,12),"The actual stock border profile can omit truck_allowed; it binds a danger, never grants truck access: "+
            string.Join("; ",SiiFile.Open("/unit/hookup/gates.sii",assets).Units[0].Attributes.Select(a=>a.Key+"="+((object?)a.Value)+" ("+((object?)a.Value)?.GetType().Name+")")));
        assets.Data["/unit/hookup/conflict.sii"]=Definition(Fields.Replace("trigger_width: 5","trigger_width: 7"));
        assets.Data["/unit/hookup/third.sii"]=Definition();check(MapGateSource.ReadProfiles(assets)[Id]==null,"Conflicting profile duplicates stay unknown even after a third matching declaration");
        assets.Data.Clear();assets.Data["/unit/hookup/first.sii"]="SiiNunit { other_class: "+Id+" {x: 1} }";
        assets.Data["/unit/hookup/gate.sii"]=Definition();check(MapGateSource.ReadProfiles(assets)[Id]==null,"An earlier different-class ID cannot be silently replaced by a border trigger");
        assets.Fail="/unit/hookup/first.sii";bool failed=false;try{MapGateSource.ReadProfiles(assets);}catch(IOException){failed=true;}check(failed,"An unreadable possibly competing file prevents a partial gate catalog");assets.Fail=null;
        foreach(string include in new[]{"SiiNunit { @include \"missing.sui\" }","SiiNunit\n{\n@include \"missing.sui\"\n}"}) {
            assets.Data.Clear();assets.Data["/unit/hookup/gate.sii"]=Definition();assets.Data["/unit/hookup/include.sii"]=include;
            failed=false;try{MapGateSource.ReadProfiles(assets);}catch(IOException){failed=true;}catch(InvalidDataException){failed=true;}
            check(failed,"A missing/unsupported include cannot hide a competing gate definition behind a positive catalog");
        }
        assets.Data.Clear();assets.Data["/unit/hookup/gate.sii"]="SiiNunit\n{\n@include \"included.sui\"\n}";
        assets.Data["/unit/hookup/included.sui"]="gate_item_trigger: "+Id+"\n{\n"+Fields+"\n}";
        check(MapGateSource.ReadProfiles(assets)[Id]==new MapGateSource.Trigger(5,12),"A bounded valid relative include preserves its declared gate profile");
        assets.Data["/unit/hookup/competing.sii"]=Definition(Fields.Replace("trigger_width: 5","trigger_width: 7"));
        check(MapGateSource.ReadProfiles(assets)[Id]==null,"A conflicting included declaration cannot hide behind an otherwise valid direct profile");
        foreach(string altered in new[]{Fields.Replace("one_way: true","one_way: false"),Fields+"\ntruck_allowed: bad",Fields+"\nunsupported_condition: true"}) {
            assets.Data.Clear();assets.Data["/unit/hookup/gate.sii"]=Definition(altered);check(MapGateSource.ReadProfiles(assets)[Id]==null,"Unknown/different shape/condition semantics cannot lend a gate approach");
        }
        var map=new Map();var gate=TruckLib.ScsMap.Gate.Add(map,new(3,0,40),"bar_ll_01",GateType.TriggerActivated);
        gate.Node.Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI);
        gate.ActivationPoints.Add(new Vector3(0,0,36),Id).Node.Rotation=gate.Node.Rotation;
        check(MapGateSource.Capture(gate,profiles)!=null,"A default main-layer standalone gate resolves its own activation node");
        gate.Layer=1;check(MapGateSource.Capture(gate,profiles)==null,"Conditional/event-layer gates remain unknown");gate.Layer=0;
        gate.ActivationPoints.Add(new Vector3(0,0,44),Id);check(MapGateSource.Capture(gate,profiles)==null,"Multiple activation areas require a separate verified contract, not an arbitrary first area");gate.ActivationPoints.RemoveAt(1);
        var metadata=typeof(MapItem).GetProperty("Kdop",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(gate)!;
        metadata.GetType().GetField("Flags",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(metadata,new FlagField(1u<<19));
        check(MapGateSource.Capture(gate,profiles)==null,"Unknown standalone-gate flags are not mistaken for unconditional presence");
    }
    sealed class Assets:IModelAssets,IFileSystem
    {
        internal readonly Dictionary<string,string> Data=[];internal string? Fail;
        public IFileSystem Files=>this;public IEnumerable<string> FilesBelow(string root)=>Data.Keys;public void Dispose(){}
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>Data.ContainsKey(p);public bool DirectoryExists(string p)=>true;
        public IList<string> GetFiles(string p)=>Data.Keys.ToArray();public byte[] ReadAllBytes(string p)=>Encoding.UTF8.GetBytes(ReadAllText(p));
        public string ReadAllText(string p){if(p==Fail)throw new IOException("fixture unavailable");return Data[p];}
        public string ReadAllText(string p,Encoding e)=>ReadAllText(p);public Stream Open(string p)=>new MemoryStream(ReadAllBytes(p));
        public string GetParent(string p)=>p[..p.LastIndexOf('/')];
    }
}
