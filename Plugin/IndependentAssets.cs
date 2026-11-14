using System.Reflection;
using TruckLib;
using TruckLib.HashFs;

namespace MautAssistent;

/// <summary>Own archive streams: native HashFS readers seek a shared stream.</summary>
internal sealed class IndependentAssets : IDisposable
{
    readonly List<IDisposable> readers=[];
    public IFileSystem Files {get;}
    public IndependentAssets(string[] paths)
    {
        try {
            var sources=paths.Select(path=> {
                var reader=HashFsReader.Open(path);
                readers.Add((IDisposable)reader);
                return (IFileSystem)reader;
            }).ToArray();
            Files=new AssetLoader(sources);
        } catch {Dispose();throw;}
    }
    public void Dispose(){foreach(var reader in readers)reader.Dispose();readers.Clear();}
    public IEnumerable<string> FilesBelow(string root)
    {
        var visited=new HashSet<string>(StringComparer.Ordinal);var files=new HashSet<string>(StringComparer.Ordinal);
        var folders=new Queue<string>();folders.Enqueue(root);
        while(folders.TryDequeue(out var folder)) {
            if(!visited.Add(folder))continue;
            if(visited.Count>4096)throw new InvalidDataException("Hook-up-Verzeichnis zu groß.");
            foreach(var reader in readers.OfType<IHashFsReader>()) {
                if(!((IFileSystem)reader).DirectoryExists(folder))continue;
                var listing=reader.GetDirectoryListing(folder);
                files.UnionWith(listing.Files);
                foreach(var child in listing.Subdirectories)folders.Enqueue(child);
            }
        }
        return files;
    }
    public static string[]? SnapshotPaths(IFileSystem source)
    {
        // Read immutable archive names only. Never extract or seek through a
        // host reader from the worker. Unsupported/mod filesystem types fail closed.
        IEnumerable<IFileSystem> sources=source is AssetLoader?
            typeof(AssetLoader).GetField("fileSystems",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(source) as IFileSystem[] ?? []:
            [source];
        var result=new List<string>();
        foreach(var item in sources) {
            if(item is not HashFsReaderBase reader||reader.BaseReader.BaseStream is not FileStream stream)return null;
            result.Add(stream.Name);
        }
        return result.Count>0?result.ToArray():null;
    }
}
