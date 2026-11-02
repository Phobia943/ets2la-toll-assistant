using System.Text.Json;
namespace MautAssistent;

public sealed class PreferenceStore(string filename)
{
    public Dictionary<string,int> Gates {get;private set;}=[];
    public TollOptions Options {get;private set;}=new();
    public Dictionary<string,TollPayment> Payments {get;private set;}=[];
    public void Load()
    {
        if(!File.Exists(filename))return;
        var saved=JsonSerializer.Deserialize<Saved>(File.ReadAllText(filename)) ?? throw new InvalidDataException("Leere Maut-Einstellungen");
        saved.Options.Validate();
        if(saved.Gates==null || saved.Gates.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value<0)) throw new InvalidDataException("Ungültige Mautspuren");
        var payments=saved.Payments??new();
        if(payments.Any(p=>!Enum.IsDefined(p.Value)))throw new InvalidDataException("Ungültige Maut-Zahlart");
        Options=saved.Options;Gates=saved.Gates;Payments=payments;
    }
    public void Save(TollOptions options,Dictionary<string,int> gates,Dictionary<string,TollPayment>? payments=null)
    {
        options.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filename))!);
        var temp=filename+".tmp";
        var nextPayments=payments??Payments;
        File.WriteAllText(temp,JsonSerializer.Serialize(new Saved(options,gates,nextPayments),new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,filename,true);
        Options=options;Gates=new(gates);Payments=new(nextPayments);
    }
    public sealed record Saved(TollOptions Options,Dictionary<string,int> Gates,Dictionary<string,TollPayment>? Payments=null);
}
