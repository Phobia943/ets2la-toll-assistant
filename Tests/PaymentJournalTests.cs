using System.Diagnostics;
using System.Numerics;
using System.Text;
using MautAssistent;

public static class PaymentJournalTests
{
    public static void Run(Action<bool,string> check)=>RunAsync(check).GetAwaiter().GetResult();
    static async Task RunAsync(Action<bool,string> check)
    {
        await Persistence(check);
        await BoundedNonblocking(check);
        await Corruption(check);
        await SerializedTransactions(check);
    }
    static async Task Persistence(Action<bool,string> check)
    {
        byte[]? bytes=null;int writes=0,writerThread=0;
        using var journal=new PaymentJournal(()=>bytes,data=>{writerThread=Environment.CurrentManagedThreadId;bytes=data.ToArray();writes++;});
        check((await journal.LoadAsync()).Available,"A genuinely absent journal is a known empty reservation set");
        int callerThread=Environment.CurrentManagedThreadId;
        var first=await journal.ReserveAsync("booth:A",new(1,2,3),"session-A:request-1");
        check(first is {Durable:true,Applied:true,RequestToken:"session-A:request-1",StoredReservationToken:"session-A:request-1"}&&writes==1&&writerThread!=callerThread,
            "Only the IO worker durably writes the exact reservation token before acknowledging it");
        var same=await journal.ReserveAsync("booth:A",new(90,2,3),"session-A:request-1");
        var another=await journal.ReserveAsync("booth:A",new(90,2,3),"session-B:request-2");
        check(same is {Durable:true,Applied:false}&&another is {Durable:true,Applied:false,StoredReservationToken:"session-A:request-1"}&&writes==1,
            "Neither a repeated request nor a newer epoch/session token gets a second reservation at a consumed booth");
        var snapshot=await journal.LoadAsync();
        check(snapshot.Entries.Single() is {X:1,Y:2,Z:3}&&snapshot.Entries is not PaymentJournalEntry[],
            "Snapshots retain the original gate anchor and cannot be mutated through an exported array");
        check(!(await journal.RemoveAsync("booth:A","wrong-token","departure:1")).Applied&&writes==1,
            "A stale or foreign departure cannot remove a different encounter's reservation");
        check((await journal.RemoveAsync("booth:A","session-A:request-1","departure:2")) is {Durable:true,Applied:true}&&
            (await journal.LoadAsync()).Entries.Count==0,"An explicitly authorized departure is acknowledged only after durable removal");
        var next=await journal.ReserveAsync("booth:A",new(1,2,3),"session-C:request-3");
        check(next.Applied&&!(await journal.RemoveAsync("booth:A","session-A:request-1","late-departure")).Applied,
            "A late removal cannot erase the new reservation created after a legitimate revisit");
        var frozen=snapshot.Entries.Single();
        check(frozen.AttemptToken=="session-A:request-1"&&snapshot.Entries.Count==1,
            "Previously returned snapshots remain immutable after subsequent journal mutations");

        byte[] legacy=Encoding.UTF8.GetBytes("[{\"Gate\":\"old-booth\",\"X\":1,\"Y\":2,\"Z\":3}]");
        using var imported=new PaymentJournal(()=>legacy,data=>legacy=data);
        var old=await imported.LoadAsync();
        check(old.Available&&old.Entries.Single().AttemptToken.StartsWith("legacy:",StringComparison.Ordinal)&&
            !(await imported.ReserveAsync("old-booth",Vector3.Zero,"new-request")).Applied,
            "The old gate/anchor-only journal migrates as consumed, never as a fresh activation acknowledgement");
        check((await imported.RemoveAsync("old-booth",old.Entries.Single().AttemptToken,"observed-departure")).Applied,
            "A proven departure can retire an imported legacy tombstone using its explicit stored token");
    }
    static async Task BoundedNonblocking(Action<bool,string> check)
    {
        byte[]? bytes=null;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var journal=new PaymentJournal(()=>bytes,data=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();bytes=data.ToArray();});
        await journal.LoadAsync();
        var watch=Stopwatch.StartNew();
        var pending=journal.ReserveAsync("booth:0",Vector3.Zero,"epoch1:request0");
        watch.Stop();
        check(watch.ElapsedMilliseconds<200&&entered.Wait(TimeSpan.FromSeconds(2))&&!pending.IsCompleted,
            "Enqueuing a reservation returns immediately while the dedicated writer is blocked");
        var requests=new List<Task<PaymentJournalAck>>{pending};
        for(int i=1;i<PaymentJournal.Capacity;i++)requests.Add(journal.ReserveAsync("booth:"+i,new(i,0,0),"epoch1:request"+i));
        var overflow=journal.ReserveAsync("overflow",Vector3.Zero,"epoch1:overflow");
        check(overflow.IsCompleted&&await overflow is {Durable:false,Applied:false}&&requests.All(t=>!t.IsCompleted),
            "Exactly32 outstanding operations including the blocked writer are bounded; overflow rejects without waiting");
        check((await journal.LoadAsync()).Available==false,"Queue-full LoadAsync returns a failed snapshot rather than blocking the driving caller");
        // F9/disable merely retires the caller's token; it cannot cancel a
        // transaction already committing. Dispose also must not wait on IO.
        string activeToken="epoch2:other-request";
        watch.Restart();journal.Dispose();watch.Stop();
        check(watch.ElapsedMilliseconds<200&&!pending.IsCompleted,"Stop/Dispose is nonblocking even while a reservation writer is stuck");
        await Task.Delay(275);
        check(!pending.IsCompleted&&bytes==null,"More than250ms of storage delay produces no durable acknowledgement or physical activation");
        release.Set();
        var results=await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        check(results.All(a=>a.Durable&&a.Applied)&&results[0].RequestToken!=activeToken,
            "An old request may complete durably after F9, but its opaque token cannot authorize the new caller context");
        check((await journal.ReserveAsync("closed",Vector3.Zero,"closed-request")) is {Durable:false,Applied:false},
            "A closed journal rejects newly queued work without throwing or authorizing payment");
        using var reloaded=new PaymentJournal(()=>bytes,_=>throw new Exception("read only"));
        check((await reloaded.LoadAsync()).Entries.Count==PaymentJournal.Capacity,
            "All accepted late reservations remain persistent consumed gates after caller shutdown");

        byte[]? delayedBytes=null;
        using var removing=new ManualResetEventSlim();using var finishRemove=new ManualResetEventSlim();int writes=0;
        using var delayed=new PaymentJournal(()=>delayedBytes,data=> {
            if(++writes==2){removing.Set();if(!finishRemove.Wait(TimeSpan.FromSeconds(5)))throw new TimeoutException();}
            delayedBytes=data.ToArray();
        });
        await delayed.ReserveAsync("departure",Vector3.Zero,"visit-1");
        var removal=delayed.RemoveAsync("departure","visit-1","departure-request");
        check(removing.Wait(TimeSpan.FromSeconds(2))&&!removal.IsCompleted,
            "A departure remains pending while its durable removal is blocked");
        using(var before=new PaymentJournal(()=>delayedBytes,_=>{}))
            check((await before.LoadAsync()).Entries.Count==1,"Before the removal ACK, storage still retains the consumed gate and the caller must not clear locally");
        finishRemove.Set();check((await removal).Applied,"Only the completed durable remove permits local encounter retirement");
    }
    static async Task Corruption(Action<bool,string> check)
    {
        foreach(string text in new[]{"","null","{}","{\"Version\":2,\"Entries\":[]}",
            "{\"Version\":1,\"Version\":1,\"Entries\":[]}",
            "[{\"Gate\":\"a\",\"X\":1,\"Y\":2,\"Z\":3},{\"Gate\":\"a\",\"X\":1,\"Y\":2,\"Z\":3}]",
            "[{\"Gate\":\"a\",\"X\":1e100,\"Y\":2,\"Z\":3}]",
            "{\"Version\":1,\"Entries\":[{\"Gate\":\"a\",\"X\":1,\"Y\":2,\"Z\":3}]}",
            "[{\"Gate\":\"a\",\"Gate\":\"b\",\"X\":1,\"Y\":2,\"Z\":3}]"}) {
            int writes=0;using var journal=new PaymentJournal(()=>Encoding.UTF8.GetBytes(text),_=>writes++);
            var snapshot=await journal.LoadAsync();
            var ack=await journal.ReserveAsync("a",Vector3.Zero,"request");
            check(!snapshot.Available&&!ack.Durable&&!ack.Applied&&writes==0,
                "Corrupt/unknown/duplicate journal data fails closed without overwriting its evidence: "+text);
        }
        int failures=0;using var failed=new PaymentJournal(()=>null,_=>{failures++;throw new IOException("disk unavailable");});
        check(!(await failed.ReserveAsync("a",Vector3.Zero,"request-1")).Durable&&!(await failed.LoadAsync()).Available&&
            !(await failed.ReserveAsync("b",Vector3.Zero,"request-2")).Applied&&failures==1,
            "An uncertain storage failure revokes this writer instead of automatically retrying an unknown commit");
        using var invalid=new PaymentJournal(()=>null,_=>throw new Exception("must not write"));
        check(!(await invalid.ReserveAsync("a",new(float.NaN,0,0),"r")).Applied&&
            !(await invalid.ReserveAsync("a",Vector3.Zero,"")).Applied,
            "Malformed gate anchors and missing opaque tokens reject before storage access");
    }
    static async Task SerializedTransactions(Action<bool,string> check)
    {
        byte[]? bytes=null;object gate=new();
        using var writer1=new PaymentJournal(()=>bytes,data=>bytes=data.ToArray(),()=>new GateLease(gate));
        using var writer2=new PaymentJournal(()=>bytes,data=>bytes=data.ToArray(),()=>new GateLease(gate));
        await writer1.LoadAsync();await writer2.LoadAsync();
        var results=await Task.WhenAll(writer1.ReserveAsync("a",Vector3.Zero,"old-instance"),writer2.ReserveAsync("b",Vector3.One,"new-instance"));
        var snapshot=await writer1.LoadAsync();
        check(results.All(r=>r.Applied)&&snapshot.Entries.Select(e=>e.Gate).SequenceEqual(new[]{"a","b"}),
            "Each instance rereads under a shared transaction before mutation, retaining the other instance's newer tombstones");
        var clash=await Task.WhenAll(writer1.ReserveAsync("same",Vector3.Zero,"request-A"),writer2.ReserveAsync("same",Vector3.Zero,"request-B"));
        check(clash.Count(r=>r.Applied)==1&&clash.All(r=>r.Durable),"Competing instances can durably create at most one reservation for the same gate");
    }
    sealed class GateLease:IDisposable
    {
        readonly object gate;
        public GateLease(object gate) {this.gate=gate;Monitor.Enter(gate);}
        public void Dispose()=>Monitor.Exit(gate);
    }
}
