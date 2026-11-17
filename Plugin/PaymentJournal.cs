using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MautAssistent;

public sealed record PaymentJournalEntry(string Gate,float X,float Y,float Z,string AttemptToken)
{
    [JsonIgnore] public Vector3 Anchor=>new(X,Y,Z);
}
public sealed record PaymentJournalSnapshot(bool Available,string? Issue,IReadOnlyList<PaymentJournalEntry> Entries);
public sealed record PaymentJournalAck(bool Durable,bool Applied,string Gate,string RequestToken,string? StoredReservationToken,string? Issue);

/// <summary>Bounded journal only, never activation authority. Enqueue/Dispose do
/// no file IO and never wait for storage. One dedicated worker performs every
/// transaction. A late durable acknowledgement must be matched to a still-current
/// request by the caller, then pass a NEW current-context activation decision.
/// Retain this instance across F9/disable; do not synchronously await its tasks in
/// a driving/host lock. Only after proven physical departure may RemoveAsync run.</summary>
public sealed class PaymentJournal : IDisposable,IAsyncDisposable
{
    public const int Capacity=32,MaximumEntries=4096;
    const int MaximumBytes=32*1024*1024;
    readonly Func<byte[]?> read;
    readonly Action<byte[]> durableWrite;
    readonly Func<IDisposable>? transaction;
    readonly Channel<Action> queue=Channel.CreateBounded<Action>(new BoundedChannelOptions(Capacity) {
        SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait,AllowSynchronousContinuations=false
    });
    // Count the executing operation as well as the queued ones.
    readonly SemaphoreSlim slots=new(Capacity,Capacity);
    readonly Task worker;
    string? fatalIssue;

    public PaymentJournal(string path)
    {
        string fullPath=Path.GetFullPath(path);
        read=()=>ReadDisk(fullPath);durableWrite=data=>WriteDisk(fullPath,data);
        string normalized=OperatingSystem.IsWindows()?fullPath.ToUpperInvariant():fullPath;
        string mutexName=(OperatingSystem.IsWindows()?"Local\\":"")+"ETS2LA-Maut-Payment-"+
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        transaction=()=>AcquireMutex(mutexName);
        worker=StartWorker();
    }
    /// <summary>Injected storage runs exclusively on the journal worker. The
    /// writer must return only after an atomic durable commit. A production
    /// multi-instance store additionally needs a transaction lock; the path
    /// constructor provides it. Tests may use isolated in-memory storage.</summary>
    public PaymentJournal(Func<byte[]?> read,Action<byte[]> durableWrite,Func<IDisposable>? transaction=null)
    {
        this.read=read??throw new ArgumentNullException(nameof(read));
        this.durableWrite=durableWrite??throw new ArgumentNullException(nameof(durableWrite));
        this.transaction=transaction;worker=StartWorker();
    }
    Task StartWorker()=>Task.Factory.StartNew(Work,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    void Work()
    {
        while(queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            while(queue.Reader.TryRead(out var run))run();
    }
    Task<T> Enqueue<T>(Func<T> work,Func<string,T> rejected)
    {
        if(!slots.Wait(0))return Task.FromResult(rejected("Zahlungsprotokoll-Warteschlange ist voll"));
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!queue.Writer.TryWrite(()=> {
            T result;
            try {result=work();}
            catch(Exception ex) when(ex is not OutOfMemoryException) {
                fatalIssue="Zahlungsprotokoll nicht verfügbar: "+ex.GetType().Name;
                result=rejected(fatalIssue);
            }
            finally {slots.Release();}
            completion.TrySetResult(result);
        })) {
            slots.Release();completion.TrySetResult(rejected("Zahlungsprotokoll ist geschlossen"));
        }
        return completion.Task;
    }
    static PaymentJournalSnapshot Unavailable(string issue)=>new(false,issue,Array.Empty<PaymentJournalEntry>());
    static PaymentJournalSnapshot Snapshot(Dictionary<string,PaymentJournalEntry> entries)=>
        new(true,null,Array.AsReadOnly(entries.Values.OrderBy(e=>e.Gate,StringComparer.Ordinal).ToArray()));
    static bool ValidText(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum;
    static PaymentJournalAck Reject(string gate,string token,string issue)=>new(false,false,gate,token,null,issue);

    public Task<PaymentJournalSnapshot> LoadAsync()=>Enqueue(()=> {
        if(fatalIssue!=null)return Unavailable(fatalIssue);
        using var lease=transaction?.Invoke();
        return Snapshot(ReadCurrent());
    },Unavailable);

    public Task<PaymentJournalAck> ReserveAsync(string gate,Vector3 anchor,string requestToken)
    {
        if(!ValidText(gate,4096)||!ValidText(requestToken,512)||!Finite(anchor))
            return Task.FromResult(Reject(gate,requestToken,"Ungültige Zahlungsreservierung"));
        return Enqueue(()=> {
            if(fatalIssue!=null)return Reject(gate,requestToken,fatalIssue);
            using var lease=transaction?.Invoke();
            // Re-read INSIDE the shared file transaction: a retiring plugin
            // instance must never overwrite a newer instance's reservation.
            var entries=ReadCurrent();
            if(entries.TryGetValue(gate,out var prior))
                return new PaymentJournalAck(true,false,gate,requestToken,prior.AttemptToken,"Schranke bereits reserviert");
            if(entries.Count>=MaximumEntries)return Reject(gate,requestToken,"Zahlungsprotokoll ist voll");
            entries.Add(gate,new(gate,anchor.X,anchor.Y,anchor.Z,requestToken));
            WriteCurrent(entries);
            return new PaymentJournalAck(true,true,gate,requestToken,requestToken,null);
        },issue=>Reject(gate,requestToken,issue));
    }
    public Task<PaymentJournalAck> RemoveAsync(string gate,string expectedReservationToken,string requestToken)
    {
        if(!ValidText(gate,4096)||!ValidText(expectedReservationToken,512)||!ValidText(requestToken,512))
            return Task.FromResult(Reject(gate,requestToken,"Ungültiger Abschluss der Mautbegegnung"));
        return Enqueue(()=> {
            if(fatalIssue!=null)return Reject(gate,requestToken,fatalIssue);
            using var lease=transaction?.Invoke();
            var entries=ReadCurrent();
            if(!entries.TryGetValue(gate,out var prior))return new PaymentJournalAck(true,false,gate,requestToken,null,"Reservierung nicht vorhanden");
            if(prior.AttemptToken!=expectedReservationToken)
                return new PaymentJournalAck(true,false,gate,requestToken,prior.AttemptToken,"Reservierung gehört zu einer anderen Begegnung");
            entries.Remove(gate);WriteCurrent(entries);
            return new PaymentJournalAck(true,true,gate,requestToken,expectedReservationToken,null);
        },issue=>Reject(gate,requestToken,issue));
    }
    sealed record Document(int Version,PaymentJournalEntry[] Entries);
    Dictionary<string,PaymentJournalEntry> ReadCurrent()
    {
        var bytes=read();if(bytes==null)return new(StringComparer.Ordinal);
        if(bytes.Length==0||bytes.Length>MaximumBytes)throw new InvalidDataException("Ungültige Protokollgröße");
        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=8});
        var root=document.RootElement;
        bool legacy=root.ValueKind==JsonValueKind.Array;
        JsonElement array;
        if(legacy)array=root;
        else {
            ExactFields(root,["Version","Entries"]);
            if(root.GetProperty("Version").GetInt32()!=1)throw new InvalidDataException("Unbekannte Protokollversion");
            array=root.GetProperty("Entries");
        }
        if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()>MaximumEntries)throw new InvalidDataException("Ungültige Protokolleinträge");
        var result=new Dictionary<string,PaymentJournalEntry>(StringComparer.Ordinal);
        foreach(var element in array.EnumerateArray()) {
            ExactFields(element,legacy?["Gate","X","Y","Z"]:["Gate","X","Y","Z","AttemptToken"]);
            string gate=element.GetProperty("Gate").GetString()??"";
            float x=element.GetProperty("X").GetSingle(),y=element.GetProperty("Y").GetSingle(),z=element.GetProperty("Z").GetSingle();
            string token=legacy?"legacy:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gate+":"+
                BitConverter.SingleToInt32Bits(x)+":"+BitConverter.SingleToInt32Bits(y)+":"+BitConverter.SingleToInt32Bits(z)))):
                element.GetProperty("AttemptToken").GetString()??"";
            if(!ValidText(gate,4096)||!ValidText(token,512)||!Finite(new(x,y,z))||!result.TryAdd(gate,new(gate,x,y,z,token)))
                throw new InvalidDataException("Inkonsistenter Protokolleintrag");
        }
        return result;
    }
    static void ExactFields(JsonElement element,string[] names)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Ungültige Protokollstruktur");
        var fields=element.EnumerateObject().Select(p=>p.Name).ToArray();
        if(fields.Length!=names.Length||fields.Distinct(StringComparer.Ordinal).Count()!=fields.Length||fields.Any(n=>!names.Contains(n,StringComparer.Ordinal)))
            throw new InvalidDataException("Unbekannte oder doppelte Protokollfelder");
    }
    void WriteCurrent(Dictionary<string,PaymentJournalEntry> entries)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Document(1,entries.Values.OrderBy(e=>e.Gate,StringComparer.Ordinal).ToArray()));
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("Zahlungsprotokoll zu groß");
        durableWrite(bytes);
    }
    static bool Finite(Vector3 value)=>float.IsFinite(value.X)&&float.IsFinite(value.Y)&&float.IsFinite(value.Z);
    static byte[]? ReadDisk(string path)
    {
        if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>MaximumBytes)throw new InvalidDataException("Zahlungsprotokoll zu groß");
        return File.ReadAllBytes(path);
    }
    static void WriteDisk(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending=path+".pending-"+Guid.NewGuid().ToString("N");
        try {
            using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
                stream.Write(bytes);stream.Flush(true);
            }
            if(File.Exists(path))File.Replace(pending,path,null);
            else File.Move(pending,path);
        }finally {if(File.Exists(pending))File.Delete(pending);}
    }
    sealed class MutexLease(Mutex mutex):IDisposable
    {
        public void Dispose() {mutex.ReleaseMutex();mutex.Dispose();}
    }
    static IDisposable AcquireMutex(string name)
    {
        var mutex=new Mutex(false,name);bool held=false;
        try {
            try {held=mutex.WaitOne(TimeSpan.FromSeconds(5));}
            catch(AbandonedMutexException) {held=true;}
            if(!held)throw new TimeoutException("Zahlungsprotokoll wird bereits verwendet");
            return new MutexLease(mutex);
        }catch {if(held)mutex.ReleaseMutex();mutex.Dispose();throw;}
    }
    public void Dispose()=>queue.Writer.TryComplete();
    public ValueTask DisposeAsync() {Dispose();return new ValueTask(worker);}
}
