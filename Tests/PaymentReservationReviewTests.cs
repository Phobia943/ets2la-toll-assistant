using MautAssistent;

public static class PaymentReservationReviewTests
{
    public static void BeforeFirstIntent(Action<bool,string> check)
    {
        foreach(bool focusGap in new[]{true,false}) {
            using var f=new ManualPaymentRuntimeIntegration.Fixture(false);
            f.Run();f.AwaitDurable();var original=f.Reservation();
            check(f.Leases.Count==0,"The interruption must precede any output lease, not merely an unobserved physical pulse");
            f.Fresh();
            if(focusGap)f.Foreground=false;else f.Set("pathAt",f.Now-.25);
            f.Run();
            check(f.Leases.Count==0&&f.Cancels.Count>0,
                "Temporary focus/route loss must revoke physical payment authority immediately");
            f.Foreground=true;f.Fresh();f.Run();
            check(ReferenceEquals(f.Reservation(),original)&&f.Leases.Count==0,
                "Recovery keeps only the same durable token and must restart the complete stopped window");
            f.UntilFirstLease();
            check(f.Leases.Select(x=>x.Attempt).Distinct().Count()==1,
                "Fresh revalidation after a pre-intent interruption permits exactly the first payment intent");
            check(f.Journal.LoadAsync().GetAwaiter().GetResult().Entries.Count==1,
                "Passive suspension does not erase or replace durable encounter history");
        }
    }

    public static void IssuedAndSessionRevocation(Action<bool,string> check)
    {
        using(var issued=new ManualPaymentRuntimeIntegration.Fixture(false)) {
            issued.UntilFirstLease();int before=issued.Leases.Count;
            issued.Foreground=false;issued.Fresh();issued.Run();
            issued.Foreground=true;
            for(int i=0;i<24;i++){issued.Fresh();issued.Run();Thread.Sleep(25);}
            check(issued.Leases.Count==before,
                "An issued intent stays consumed across focus recovery, even without payment acknowledgement");
        }
        foreach(bool changeSource in new[]{true,false}) {
            using var f=new ManualPaymentRuntimeIntegration.Fixture(false);
            f.Run();f.AwaitDurable();
            if(changeSource)f.SourceEpoch++;else f.Epoch++;
            for(int i=0;i<24;i++){f.Fresh();f.Run();Thread.Sleep(25);}
            check(f.Leases.Count==0&&f.Get<ManualPayment>("manualPayment").ConsumedGateIds.Length==1,
                "Changing source or assist epoch cancels even an unissued reservation permanently for this encounter");
        }
    }
}
