namespace MautAssistent;

public enum TollPayment { Unknown, Automatic, Manual }

public static class PaymentPolicy
{
    public static string Key(string route,int gate)=>$"{route}/gate/{gate}";
    public static int? ChooseAutomatic(string route,IEnumerable<int> reachable,int? remembered,
        IReadOnlyDictionary<string,TollPayment> payments)
    {
        var automatic=reachable.Where(g=>payments.TryGetValue(Key(route,g),out var mode)&&mode==TollPayment.Automatic).ToArray();
        if(remembered.HasValue&&automatic.Contains(remembered.Value))return remembered;
        return automatic.Length>0?automatic[0]:null;
    }
}
