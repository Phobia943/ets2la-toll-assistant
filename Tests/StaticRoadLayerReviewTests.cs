using System.Numerics;
using System.Text;
using MautAssistent;
using TruckLib;
using TruckLib.ScsMap;

internal static class StaticRoadLayerReviewTests
{
    internal static void Run(Action<bool,string> check)
    {
        string parent=Path.GetFullPath(Path.GetTempPath());
        string folder=Path.Combine(parent,"maut-static-layer-"+Guid.NewGuid().ToString("N"));
        try {
            var saved=new Map();var item=Model.Add(saved,new Vector3(100,0,100),"test","default","default");item.Layer=1;
            saved.Save(Path.Combine(folder,"map"),"review");
            var files=new Files(folder);var read=new Map();
            StaticRoadAuxMapReader.Read(read,files,"review",new(0,0));
            var actual=(Model)read.MapItems[item.Uid];
            check(actual.Layer==1&&!StaticRoadSceneSource.EligibleInstance(actual),"Successful auxiliary read must retain conditional layer and exclude its model");
            check(actual.Node.Position==item.Node.Position,"Auxiliary-only parsing must resolve the model's own node and placement");
            foreach(bool listingFailure in new[]{false,true}) {
                files.FailLayer=true;files.FailListing=listingFailure;var partial=new Map();bool rejected=false;
                try{StaticRoadAuxMapReader.Read(partial,files,"review",new(0,0));}catch(IOException){rejected=true;}
                check(rejected,"Layer "+(listingFailure?"existence":"read")+" failure must propagate instead of publishing default-layer models");
                check(partial.MapItems.TryGetValue(item.Uid,out var unvalidated)&&unvalidated.Layer==0,
                    "Regression must actually contain a dangerous partial default-layer model before the layer failure");
            }
        } finally {
            string target=Path.GetFullPath(folder);
            if(target.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(target).StartsWith("maut-static-layer-",StringComparison.Ordinal)&&Directory.Exists(target))
                Directory.Delete(target,true);
        }
    }
    sealed class Files(string root):IFileSystem
    {
        public bool FailLayer,FailListing;
        public char DirectorySeparator=>'/';
        string Local(string path)=>Path.Combine(root,path.TrimStart('/').Replace('/',Path.DirectorySeparatorChar));
        public bool FileExists(string path){if(FailLayer&&FailListing&&path.EndsWith(".layer"))throw new IOException("Temporary layer query failure");return File.Exists(Local(path));}
        public bool DirectoryExists(string path)=>Directory.Exists(Local(path));
        public IList<string> GetFiles(string path)=>throw new NotSupportedException();
        public byte[] ReadAllBytes(string path)=>File.ReadAllBytes(Local(path));
        public string ReadAllText(string path)=>File.ReadAllText(Local(path));
        public string ReadAllText(string path,Encoding encoding)=>File.ReadAllText(Local(path),encoding);
        public Stream Open(string path){if(FailLayer&&path.EndsWith(".layer"))throw new IOException("Temporary layer read failure");return File.OpenRead(Local(path));}
        public string GetParent(string path)=>path[..path.LastIndexOf('/')];
    }
}
