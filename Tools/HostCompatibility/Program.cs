using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using MautHostCompatibility;

if(args.Length is not (3 or 5)||args.Length==5&&args[3]!="--originals")throw new ArgumentException("Usage: HostCompatibility prepare|verify <ETS2LA root> <output directory> [--originals <verified original backup>]");
string originals=Path.GetFullPath(args.Length==5?args[4]:args[1]);
if(args[0]=="verify") {CompatibilityManifest.Verify(originals,Path.GetFullPath(args[2]));Replay.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));return;}
if(args[0]!="prepare")throw new ArgumentException("Unknown operation");
string source=Path.GetFullPath(args[1]),destination=Path.GetFullPath(args[2]);
if(Directory.Exists(destination)||destination.Equals(source,StringComparison.OrdinalIgnoreCase)||
    destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Only a new separate preparation directory is allowed.");
var expected=new Dictionary<string,string>{
    ["ETS2LA.Game.dll"]="69E608A881E09C611DC467B5AB183A102844904C8755553BF3F9752AD646AE2D",
    ["ETS2LA.State.dll"]="1EF2ACA012A613ED87FAF717F7C45F2BA05D5C8C268B0D5CC4C99AD25527A297",
    ["Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll"]="52DE460350B224918AD86792FE60B9631BF43CB64159E1516EEF5046F6CBD282",
    ["Plugins/tumppi066.laneassist/LaneAssist.dll"]="8984FD27F3DFF0C11C9CE45A0326E573FDDD328E58B9C45C3EB2E72960466D2C"
};
static string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
foreach(var file in expected)if(Hash(Path.Combine(originals,file.Key))!=file.Value)throw new NotSupportedException("Unverified original build: "+file.Key);
using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(source);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
foreach(var dir in Directory.GetDirectories(Path.Combine(source,"Plugins")))resolver.AddSearchDirectory(dir);
using var seed=ModuleDefinition.ReadModule(System.Reflection.Assembly.GetExecutingAssembly().Location,new ReaderParameters{AssemblyResolver=resolver});
var modules=expected.ToDictionary(p=>p.Key,p=>ModuleDefinition.ReadModule(Path.Combine(originals,p.Key),new ReaderParameters{AssemblyResolver=resolver}));
try {
    Rewrite.PrefabRouting(modules["ETS2LA.Game.dll"],seed.GetType("MautHostCompatibility.PrefabRoutingTemplate"));
    var output=Rewrite.Output(modules["ETS2LA.Game.dll"],seed.GetType("MautHostCompatibility.OutputTemplate"));
    Rewrite.State(modules["ETS2LA.State.dll"],output);
    var acc=modules["Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll"];
    Rewrite.AccDeadBand(acc);
    int accSites=Rewrite.StampPublishers(acc,"AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",output);
    int laneSites=Rewrite.StampPublishers(modules["Plugins/tumppi066.laneassist/LaneAssist.dll"],"LaneAssist.LaneAssistPlugin",output);
    Rewrite.Coasting(modules["ETS2LA.Game.dll"],acc,seed.GetType("MautHostCompatibility.AccCoastTemplate"),seed.GetType("MautHostCompatibility.NativeInputTemplate"),output);
    Directory.CreateDirectory(destination);
    var results=new List<object>();
    foreach(var entry in modules) {
        entry.Value.Mvid=Guid.NewGuid();
        entry.Value.Types.First(t=>t.FullName== (entry.Key=="ETS2LA.Game.dll"?"ETS2LA.Game.Output.GameOutput":entry.Key=="ETS2LA.State.dll"?"ETS2LA.State.ApplicationState":
            entry.Key.EndsWith("/AdaptiveCruiseControl.dll")?"AdaptiveCruiseControl.AdaptiveCruiseControlPlugin":"LaneAssist.LaneAssistPlugin"))
            .Fields.Add(new("MautHostRevision",FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.Literal|FieldAttributes.HasDefault,entry.Value.TypeSystem.String){Constant=OutputTemplate.MautCompatibility});
        string path=Path.Combine(destination,entry.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        entry.Value.Write(path);
        using var verify=ModuleDefinition.ReadModule(path);
        if(verify.AssemblyReferences.Any(r=>r.Name=="HostCompatibility"))throw new InvalidDataException("Template dependency leaked into host");
        results.Add(new{file=entry.Key,originalSha256=expected[entry.Key],preparedSha256=Hash(path),module=verify.Mvid});
    }
    File.WriteAllText(Path.Combine(destination,"compatibility.json"),JsonSerializer.Serialize(new{
        version=OutputTemplate.MautCompatibility,preparedOnly=true,installed=false,accPublisherSites=accSites,lanePublisherSites=laneSites,files=results
    },new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine(JsonSerializer.Serialize(new{preparedOnly=true,installed=false,destination,accSites,laneSites}));
} finally {foreach(var module in modules.Values)module.Dispose();}
