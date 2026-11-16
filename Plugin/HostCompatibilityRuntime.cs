using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ETS2LA.Game.Output;
using ETS2LA.State;
namespace MautAssistent;
internal static class PinnedHostBuild
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type,string),bool> verified=new();
    internal static bool Matches(Type type,string file)=>verified.GetOrAdd((type,file),key=> {
        try {
            using var source=typeof(TollPlugin).Assembly.GetManifestResourceStream("MautAssistent.HostCompatibility.json")!;
            using var manifest=JsonDocument.Parse(source);
            var row=manifest.RootElement.GetProperty("files").EnumerateArray().Single(r=>r.GetProperty("file").GetString()==key.Item2);
            var assembly=key.Item1.Assembly;
            return key.Item1.GetField("MautHostRevision")?.GetRawConstantValue() as string==manifest.RootElement.GetProperty("version").GetString()&&
                assembly.ManifestModule.ModuleVersionId==row.GetProperty("module").GetGuid()&&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))==row.GetProperty("preparedSha256").GetString();
        } catch {return false;}
    });
}
public sealed partial class TollPlugin
{
    double hostCheckAt=-100;
    string? hostCheckIssue;
    Assembly[]? checkedHostAssemblies;
    readonly bool requireHostCompatibility;
    string? CheckHostCompatibility()
    {
        if(!requireHostCompatibility)return null; // Dependency-injected offline fixtures.
        Type?[] types=[typeof(GameOutput),typeof(ApplicationState),readAcc()?.GetType(),readLaneAssist()?.GetType()];
        if(types.Any(t=>t==null))return "Ausgabemodule werden noch geladen.";
        var assemblies=types.Select(t=>t!.Assembly).ToArray();
        if(checkedHostAssemblies!=null&&assemblies.SequenceEqual(checkedHostAssemblies)&&
            (hostCheckIssue==null||clock.Elapsed.TotalSeconds-hostCheckAt<1))return hostCheckIssue;
        checkedHostAssemblies=assemblies;hostCheckAt=clock.Elapsed.TotalSeconds;
        try {
            using var source=typeof(TollPlugin).Assembly.GetManifestResourceStream("MautAssistent.HostCompatibility.json")??throw new InvalidDataException("Paketnachweis fehlt");
            using var manifest=JsonDocument.Parse(source);
            var rows=manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
            if(rows.Length!=4)throw new InvalidDataException("Unvollständiges Ausgabepaket");
            for(int i=0;i<4;i++) {
                var row=rows[i];var type=types[i]!;var assembly=assemblies[i];
                if(!PinnedHostBuild.Matches(type,row.GetProperty("file").GetString()!))
                    throw new InvalidDataException("Ausgabepaket passt nicht zur geladenen Komponente: "+assembly.GetName().Name);
            }
            return hostCheckIssue=null;
        } catch(Exception ex) {
            return hostCheckIssue="Ausgabekorrektur 0.3.18 ist nicht vollständig geladen. ETS2LA geschlossen aktualisieren und neu starten. "+ex.Message;
        }
    }
}
