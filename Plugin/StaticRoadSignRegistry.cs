using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using TruckLib;

namespace MautAssistent;

// Only explicit, immovable sign models with their own collision asset. Render
// geometry and dynamic signs' original editor placements are not body evidence.
internal static class StaticRoadSignRegistry
{
    internal sealed record Definition(string Model,string Collision,string Variant);
    internal static IReadOnlyDictionary<string,Definition?> ReadCatalog(IFileSystem files)
    {
        var paths=files.GetFiles("/def/world/").Where(p=>p.StartsWith("/def/world/sign.",StringComparison.Ordinal)&&p.EndsWith(".sii",StringComparison.Ordinal)).Take(1025).ToArray();
        if(paths.Length>1024)throw new InvalidDataException("Sign registry budget exceeded");
        var result=new Dictionary<string,Definition?>(StringComparer.Ordinal);int units=0;
        foreach(string path in paths)foreach(var unit in Read(files,path)) {
            if(++units>100000)throw new InvalidDataException("Sign registry unit budget exceeded");
            if(result.TryGetValue(unit.Key,out var previous)&&previous!=unit.Value)result[unit.Key]=null;
            else if(!result.ContainsKey(unit.Key))result.Add(unit.Key,unit.Value);
        }
        return new ReadOnlyDictionary<string,Definition?>(result);
    }
    internal static IReadOnlyDictionary<string,Definition?> Read(IFileSystem files,string path)
    {
        string text=files.ReadAllText(path);
        // Stock's obsolete optional compatibility bridge is not an arbitrary
        // missing include. Check its entire syntax before accepting absence.
        // SCS Game_data documents sign.custom.sii as backward compatibility.
        if(path=="/def/world/sign.custom.sii"&&Regex.IsMatch(text,
            "\\ASiiNunit\\s*\\{\\s*(?:#[^\\r\\n]*(?:\\r?\\n|$)\\s*)*@include\\s*\"/custom/sign\\.sii\"\\s*\\}\\s*\\z",RegexOptions.CultureInvariant)) {
            if(!files.FileExists("/custom/sign.sii"))return new ReadOnlyDictionary<string,Definition?>(new Dictionary<string,Definition?>());
            string legacy=files.ReadAllText("/custom/sign.sii");
            text=legacy.TrimStart('\ufeff',' ','\r','\n','\t').StartsWith("SiiNunit",StringComparison.Ordinal)?legacy:"SiiNunit {\n"+legacy+"\n}";
        }
        return Parse(text);
    }
    internal static IReadOnlyDictionary<string,Definition?> Parse(string text)
    {
        if(text.Length>8*1024*1024)throw new InvalidDataException("Sign registry file too large");
        var lex=new StaticRoadModelRegistry.Lexer(text);
        StaticRoadModelRegistry.Token Need()=>lex.Next()??throw new InvalidDataException("Truncated sign registry");
        void Expect(string value){var token=Need();if(token.Quoted||token.Text!=value)throw new InvalidDataException("Unsupported sign registry syntax");}
        Expect("SiiNunit");Expect("{");var result=new Dictionary<string,Definition?>(StringComparer.Ordinal);int units=0;
        for(;;) {
            var kind=Need();if(!kind.Quoted&&kind.Text=="}")break;
            if(++units>100000||kind.Quoted||kind.Text!="sign_model")throw new InvalidDataException("Unsupported sign registry unit");
            Expect(":");var id=Need();
            if(id.Quoted||!id.Text.StartsWith("sign.",StringComparison.Ordinal))throw new InvalidDataException("Unsupported sign registry ID");
            string name=id.Text[5..];if(name.Length is <1 or >64||!name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'))throw new InvalidDataException("Unsupported sign token");
            Expect("{");var attributes=new Dictionary<string,StaticRoadModelRegistry.Token>(StringComparer.Ordinal);bool invalid=false;
            for(;;) {
                var key=Need();if(!key.Quoted&&key.Text=="}")break;
                if(key.Quoted||!key.Text.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'))throw new InvalidDataException("Unsupported sign attribute");
                var colon=Need();
                if(!colon.Quoted&&colon.Text=="[") { // unknown array semantics cannot authorize a body
                    invalid=true;var index=Need();if(index.Text!="]")Expect("]");colon=Need();
                }
                if(colon.Quoted||colon.Text!=":")throw new InvalidDataException("Unsupported sign attribute syntax");
                var value=Need();if(!value.Quoted&&value.Text is "{" or "}" or ":" or "[")throw new InvalidDataException("Invalid sign value");
                if(!attributes.TryAdd(key.Text,value))invalid=true;
                if(key.Text is not ("sign_name" or "model_desc" or "model_coll" or "look_name" or "variant_name" or
                    "dynamic" or "mass" or "category" or "road_model" or "instanced" or "drop_sun_shadows" or
                    "distance_type" or "short_distance" or "editable" or "traffic_rule"))invalid=true;
            }
            string? Value(string key)=>attributes.TryGetValue(key,out var token)?token.Text:null;
            bool PathValue(string key,string suffix)=>attributes.TryGetValue(key,out var token)&&token.Quoted&&
                token.Text.Length>suffix.Length&&token.Text.StartsWith('/')&&token.Text.EndsWith(suffix,StringComparison.Ordinal)&&
                !token.Text.Contains('\\')&&!token.Text.Split('/').Any(p=>p is "." or "..");
            bool stationary=attributes.TryGetValue("dynamic",out var dynamic)&&!dynamic.Quoted&&dynamic.Text=="false";
            string variant=Value("variant_name")??"";
            // SCS model variant is a lowercase Token, not an arbitrary SII
            // link/pointer (a stock definition contains `Default` as a link).
            if(variant.Length>12||!variant.All(c=>char.IsAsciiLetterLower(c)||char.IsAsciiDigit(c)||c=='_'))invalid=true;
            // Editable templates can replace/offset sign boards. Their instance
            // composition is not yet a proven fixed collision layout.
            if(attributes.TryGetValue("editable",out var editable)&&(editable.Quoted||editable.Text!="false"))invalid=true;
            Definition? definition=!invalid&&stationary&&PathValue("model_desc",".pmd")&&PathValue("model_coll",".pmc")?
                new(Value("model_desc")!,Value("model_coll")!,variant):null;
            if(result.TryGetValue(name,out var previous)&&previous!=definition)definition=null;
            // A previous null stays null even if a third duplicate looks valid.
            if(result.ContainsKey(name)&&result[name]==null)definition=null;
            result[name]=definition;
        }
        if(lex.Next()!=null)throw new InvalidDataException("Trailing sign registry data");
        return new ReadOnlyDictionary<string,Definition?>(result);
    }
}
