using System.Text;
using MautAssistent;
using TruckLib;

public static class ModelRegistryIntegration
{
    public static void Run(Action<bool,string> assert)
    {
        var models=TollModelEvidence.ReadModels(new Files());
        assert(models.Count==4&&models["poland"]=="/prefab/tollgate/tollgate_a2_poland/tollgate.pmd"&&
            models["scandinavia"]=="/prefab2/toll_norway_small_sc.pmd"&&models.ContainsKey("greece")&&models.ContainsKey("ordinary"),
            "The prefab registry must include actual legacy and Scandinavian locations, not depend on a tollgate directory");
        assert(!models.ContainsKey("invalid"),"Non-model or traversal paths cannot enter the registry");
        assert(TollModelEvidence.DecodeHookup(new byte[128],0)==TollPayment.Unknown,
            "Model discovery alone does not grant automatic payment or clearance");
    }
    sealed class Files:IFileSystem
    {
        const string Data="""
        SiiNunit
        {
          prefab_model : prefab.poland { model_desc: "/prefab/tollgate/tollgate_a2_poland/tollgate.pmd" }
          prefab_model : prefab.scandinavia { model_desc: "/prefab2/toll_norway_small_sc.pmd" }
          prefab_model : prefab.greece { model_desc: "/prefab2/tollgate/tollgate_hw2_3_plus_3_lanes_gr.pmd" }
          prefab_model : prefab.ordinary { model_desc: "/prefab2/junction.pmd" }
          prefab_model : prefab.invalid { model_desc: "/prefab/../unverified.pmd" }
        }
        """;
        public char DirectorySeparator=>'/';public bool FileExists(string p)=>true;public bool DirectoryExists(string p)=>true;
        public IList<string> GetFiles(string p)=>["/def/world/prefab.fixture.sii"];
        public byte[] ReadAllBytes(string p)=>Encoding.UTF8.GetBytes(Data);
        public string ReadAllText(string p)=>Data;public string ReadAllText(string p,Encoding e)=>Data;
        public Stream Open(string p)=>new MemoryStream(ReadAllBytes(p));public string GetParent(string p)=>"/def/world";
    }
}
