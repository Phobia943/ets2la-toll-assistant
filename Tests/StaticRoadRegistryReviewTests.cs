using MautAssistent;

internal static class StaticRoadRegistryReviewTests
{
    internal static void Run(Action<bool,string> check)
    {
        var parsed=StaticRoadModelRegistry.Parse("""
        SiiNunit {
         # model_def : model.fake { model_desc: "/wrong.pmd" }
         model_def : model.a { category: "model_desc: /fake.pmd" model_desc: "/model/a.pmd" color_variant[]: "{ } :" }
         /* model_desc: "/another.pmd" */
         model_def : model.b { model_desc: "/model/b.pmd" }
        }
        """);
        check(parsed.Count==2&&parsed["a"]=="/model/a.pmd"&&parsed["b"]=="/model/b.pmd","Quoted metadata, comments and punctuation must not introduce fake registry models");
        var duplicate=StaticRoadModelRegistry.Parse("SiiNunit { model_def : model.a { model_desc: \"/a.pmd\" model_desc: \"/b.pmd\" } }");
        check(duplicate["a"]==null,"Duplicate model_desc must be unknown");
        var conflict=StaticRoadModelRegistry.Parse("SiiNunit { model_def : model.a { model_desc: \"/a.pmd\" } model_def : model.a { model_desc: \"/b.pmd\" } }");
        check(conflict["a"]==null,"Conflicting duplicate unit must be unknown");
        foreach(string raw in new[]{"SiiNunit { @include \"missing.sui\" }","SiiNunit { model_def : model.a { model_desc: \"/a.pmd\" }", "SiiNunit { /* unterminated", "SiiNunit { model_def : model.a { model_desc: \"/a.pmd\" nested { } } }"}) {
            bool rejected=false;try{StaticRoadModelRegistry.Parse(raw);}catch(InvalidDataException){rejected=true;}
            check(rejected,"Unsupported/truncated registry input must not silently drop possible overrides");
        }
        var bad=StaticRoadModelRegistry.Parse("SiiNunit { model_def : model.a { model_desc: \"/../wrong.pmd\" } }");
        check(bad["a"]==null,"Noncanonical model paths must not be loaded");
        foreach(string field in new[]{"model_coll","model_anim"}) {
            var other=StaticRoadModelRegistry.Parse("SiiNunit { model_def : model.a { model_desc: \"/a.pmd\" "+field+": \"/other\" } }");
            check(other["a"]==null,"An external "+field+" definition cannot inherit the ordinary static PMG collision assumption");
        }
    }
}
