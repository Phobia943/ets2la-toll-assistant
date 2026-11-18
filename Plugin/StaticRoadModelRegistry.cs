using System.Collections.ObjectModel;
using System.Text;

namespace MautAssistent;

// Deliberately small, linear reader for model_def registry IDs and model_desc.
// No full SII unit graph or repeated large string replacement is necessary.
// The selected stock registry uses no includes. Unsupported directives fail
// this source rather than silently ignoring a possible model override.
internal static class StaticRoadModelRegistry
{
    internal static IReadOnlyDictionary<string,string?> Parse(string text)
    {
        if(text.Length>8*1024*1024)throw new InvalidDataException("Model registry file too large");
        var lex=new Lexer(text);Token Need()=>lex.Next()??throw new InvalidDataException("Truncated model registry");
        void Expect(string value){var t=Need();if(t.Quoted||t.Text!=value)throw new InvalidDataException("Unsupported model registry syntax");}
        Expect("SiiNunit");Expect("{");var result=new Dictionary<string,string?>(StringComparer.Ordinal);int units=0;
        for(;;) {
            var kind=Need();if(!kind.Quoted&&kind.Text=="}")break;
            if(++units>100000||kind.Quoted||kind.Text!="model_def")throw new InvalidDataException("Unsupported model registry unit");
            Expect(":");var id=Need();
            if(id.Quoted||!id.Text.StartsWith("model.",StringComparison.Ordinal))throw new InvalidDataException("Unsupported model registry ID");
            string name=id.Text[6..];
            if(name.Length is <1 or >64||!name.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'))throw new InvalidDataException("Unsupported model registry token");
            Expect("{");string? model=null;bool seen=false,ambiguous=false;
            for(;;) {
                var token=Need();if(!token.Quoted&&token.Text=="}")break;
                if(!token.Quoted&&token.Text=="{")throw new InvalidDataException("Nested model registry unit");
                if(!token.Quoted&&token.Text is "model_coll" or "model_anim") {
                    Expect(":");var linked=Need();if(!linked.Quoted||linked.Text.Length!=0)ambiguous=true;
                    continue;
                }
                if(token.Quoted||token.Text!="model_desc")continue;
                Expect(":");var value=Need();
                if(seen||!value.Quoted)ambiguous=true;seen=true;model=value.Text;
            }
            if(!seen||ambiguous||model is not {Length:>5}||!model.StartsWith('/')||!model.EndsWith(".pmd",StringComparison.Ordinal)||
                model.Contains('\\')||model.Split('/').Any(p=>p is "." or ".."))model=null;
            if(result.TryGetValue(name,out var prior)&&prior!=model)model=null;
            result[name]=model;
        }
        if(lex.Next()!=null)throw new InvalidDataException("Trailing registry data");
        return new ReadOnlyDictionary<string,string?>(result);
    }
    internal readonly record struct Token(string Text,bool Quoted);
    internal sealed class Lexer(string text)
    {
        int at,count;
        internal Token? Next()
        {
            if(++count>2000000)throw new InvalidDataException("Registry token budget");
            for(;;) {
                while(at<text.Length&&(char.IsWhiteSpace(text[at])||text[at]=='\ufeff'))at++;
                if(at>=text.Length)return null;
                if(text[at]=='#'||text[at]=='/'&&at+1<text.Length&&text[at+1]=='/') {
                    while(at<text.Length&&text[at]!='\n')at++;continue;
                }
                if(text[at]=='/'&&at+1<text.Length&&text[at+1]=='*') {
                    int end=text.IndexOf("*/",at+2,StringComparison.Ordinal);if(end<0)throw new InvalidDataException("Unterminated registry comment");at=end+2;continue;
                }
                break;
            }
            char c=text[at++];if(c=='"') {
                var output=new StringBuilder();bool ended=false;
                while(at<text.Length) {
                    c=text[at++];if(c=='"'){ended=true;break;}
                    if(c=='\\') {
                        if(at>=text.Length)throw new InvalidDataException("Truncated registry string");
                        c=text[at++] switch {'\\'=>'\\','"'=>'"','n'=>'\n','r'=>'\r','t'=>'\t',_=>throw new InvalidDataException("Unsupported registry escape")};
                    }
                    output.Append(c);
                }
                if(!ended)throw new InvalidDataException("Unterminated registry string");return new(output.ToString(),true);
            }
            if("{}:[](),".Contains(c))return new(c.ToString(),false);
            int begin=at-1;
            while(at<text.Length&&!char.IsWhiteSpace(text[at])&&!"{}:[](),\"#".Contains(text[at]))at++;
            string atom=text[begin..at];if(atom.StartsWith('@'))throw new InvalidDataException("Registry directives/includes require separate validation");
            return new(atom,false);
        }
    }
}
