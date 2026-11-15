using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
namespace MautHostCompatibility;
internal static class CompatibilityManifest
{
    internal static void Verify(string root,string prepared)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(prepared,"compatibility.json")));
        var data=document.RootElement;
        if(data.GetProperty("version").GetString()!=OutputTemplate.MautCompatibility)throw new InvalidDataException("Unapproved compatibility revision");
        string[] names=["ETS2LA.Game.dll","ETS2LA.State.dll","Plugins/tumppi066.adaptivecruisecontrol/AdaptiveCruiseControl.dll","Plugins/tumppi066.laneassist/LaneAssist.dll"];
        var rows=data.GetProperty("files").EnumerateArray().ToArray();
        if(rows.Length!=4||rows.Select(r=>r.GetProperty("file").GetString()).Distinct().Count()!=4)throw new InvalidDataException("Incomplete compatibility set");
        foreach(var row in rows) {
            string name=row.GetProperty("file").GetString()!;
            if(!names.Contains(name))throw new InvalidDataException("Foreign component path");
            string file=Path.Combine(prepared,name);
            string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if(Hash(file)!=row.GetProperty("preparedSha256").GetString()||Hash(Path.Combine(root,name))!=row.GetProperty("originalSha256").GetString())
                throw new InvalidDataException("Component hash mismatch: "+name);
            using var module=ModuleDefinition.ReadModule(file);
            if(module.Mvid!=row.GetProperty("module").GetGuid()||
                module.Types.SelectMany(t=>t.Fields).Single(f=>f.Name=="MautHostRevision").Constant as string!=OutputTemplate.MautCompatibility)
                throw new InvalidDataException("Component build identity mismatch: "+name);
        }
    }
}
