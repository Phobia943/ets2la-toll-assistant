using System.Text.Json;
using System.Text.Json.Serialization;
using ETS2LA.Logging;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly DiagnosticQueue diagnostics = new(ex => Logger.Warn("Maut-Diagnose: " + ex.Message));
    static readonly JsonSerializerOptions diagnosticJson = new() {
        WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    void QueueDiagnostic(DiagnosticQueue.Slot slot, string name, object snapshot)
    {
        var directory = Path.GetDirectoryName(DiagnosticPath)!;
        diagnostics.Enqueue(slot, () => {
            Directory.CreateDirectory(directory);
            writeDiagnostic(Path.Combine(directory, name), JsonSerializer.Serialize(snapshot, diagnosticJson));
        });
    }

    void StopDiagnostics() => diagnostics.Pause();
}
