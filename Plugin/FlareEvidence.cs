using System.Globalization;
using System.Text.RegularExpressions;
using TruckLib;
using TruckLib.Models;
using TruckLib.Sii;
namespace MautAssistent;

internal static class FlareEvidence
{
    internal static bool Validate(Unit unit,Model model,string path,IFileSystem files)
    {
        if(unit.Class is not ("flare" or "flare_lamp" or "flare_blink")||unit.Attributes.Keys.Any(k=>k is not ("model" or "dir_type" or "scale_factor")&&
            !(unit.Class=="flare_blink"&&(k is "blink_pattern" or "blink_step_length")))||
            !unit.Attributes.TryGetValue("model",out var rawModel)||(object?)rawModel is not string actualPath||actualPath!=path||
            !unit.Attributes.TryGetValue("dir_type",out var direction)||
            (object?)direction is not (Token or string)||((object?)direction)?.ToString() is not ("all" or "wide"))return false;
        if(unit.Class=="flare_blink") {
            if(!unit.Attributes.TryGetValue("blink_pattern",out var rawPattern)||(object?)rawPattern is not string pattern||pattern.Length is <1 or >64||pattern.Any(c=>c is not ('X' or '-'))||
                !unit.Attributes.TryGetValue("blink_step_length",out var rawStep))return false;
            double step=(object?)rawStep switch {int v=>v,uint v=>v,float v=>v,double v=>v,_=>double.NaN};
            if(!double.IsFinite(step)||step is <=0 or >10000)return false;
        }
        double scale=1;
        if(unit.Attributes.TryGetValue("scale_factor",out var rawScale))scale=(object?)rawScale switch {int v=>v,uint v=>v,float v=>v,double v=>v,_=>double.NaN};
        if(!double.IsFinite(scale)||scale<=0||scale>100||model.Skeleton.Count!=0||model.Variants.Count!=1||
            model.Parts.Count==0||model.Looks.Count==0||model.Parts.Any(p=>p.Locators.Count!=0)||
            model.Variants.Any(v=>v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Tag.ToString()!="visible"||a.Value!=1)))return false;
        foreach(string extension in new[]{".pmc",".pma"}) {
            try {using var collision=files.Open(Path.ChangeExtension(path,extension));return false;}
            catch(FileNotFoundException) { }
        }
        foreach(var piece in model.Parts.SelectMany(p=>p.Pieces)) {
            if(piece.UseBoneIndexes||piece.UseBoneWeights||piece.Vertices.Count==0||piece.Triangles.Count==0||
                piece.Vertices.Any(v=>!float.IsFinite(v.Position.X)||!float.IsFinite(v.Position.Y)||!float.IsFinite(v.Position.Z))||
                piece.Triangles.Any(t=>t.A>=piece.Vertices.Count||t.B>=piece.Vertices.Count||t.C>=piece.Vertices.Count))return false;
            foreach(var look in model.Looks) {
                if(piece.Material>=look.Materials.Count||!Material(files.ReadAllText(look.Materials[(int)piece.Material])))return false;
            }
        }
        return model.Parts.Any(p=>p.Pieces.Count>0);
    }
    internal static bool Material(string text)
    {
        // Full, narrow renderer grammar, not a shader-name substring. Unknown
        // fields, a second effect/texture, or another material cannot hide here.
        const string number=@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";
        string pattern="\\A\\s*effect\\s*:\\s*\"eut2\\.flare\\.rfx\"\\s*\\{\\s*diffuse\\s*:\\s*\\{\\s*"+
            number+@"\s*,\s*"+number+@"\s*,\s*"+number+@"\s*\}\s*luminance_night\s*:\s*"+number+
            @"\s*luminance_output\s*:\s*"+number+"\\s*texture\\s*:\\s*\"texture_base\"\\s*\\{\\s*source\\s*:\\s*\"(?<source>/[^\"\\\\]+\\.tobj)\"\\s*(?:sampler\\s*:\\s*default|u_address\\s*:\\s*clamp\\s+v_address\\s*:\\s*clamp)\\s*\\}\\s*\\}\\s*\\z";
        var match=Regex.Match(text,pattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(!match.Success||match.Groups["source"].Value.Contains(".."))return false;
        return Regex.Matches(text,number,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))
            .All(m=>double.TryParse(m.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)&&double.IsFinite(v));
    }
}
