namespace MautAssistent;
public static class NativeCommandTime
{
    public static double? AgeSeconds(long issued,long now,long frequency)
    {
        if(issued<=0||now<issued||frequency<=0)return null;
        double age=((double)now-issued)/frequency;
        return age<=.2?age:null;
    }
}
