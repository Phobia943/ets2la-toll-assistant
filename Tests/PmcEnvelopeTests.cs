using System.Buffers.Binary;
using MautAssistent;
public static class PmcEnvelopeTests
{
    public static void Bounds(Action<bool,string> assert)
    {
        byte[] Box(float center,float size) {
            var d=new byte[120];Put(d,0,6);Put(d,8,1);Put(d,36,40);Put(d,40,48);
            Put(d,48,1);Put(d,52,64);Float(d,72,center);Float(d,96,1);Float(d,100,size);Float(d,104,size);Float(d,108,size);
            Put(d,112,0);Put(d,116,-1);return d;
        }
        var bytes=Box(10,2);double radius=PmcEnvelope.Radius(bytes);
        assert(radius>10+Math.Sqrt(12),"Translated full-size boxes are enclosed, independent of rotation");
        bool Reject(byte[] d){try{PmcEnvelope.Radius(d);return false;}catch(Exception ex) when(ex is InvalidDataException or OverflowException){return true;}}
        foreach(var mutation in new Action<byte[]>[]{d=>Put(d,0,7),d=>Put(d,8,0),d=>Put(d,36,int.MaxValue),d=>Put(d,40,0),
            d=>Put(d,48,2),d=>Put(d,52,0),d=>Float(d,72,float.NaN),d=>Float(d,96,0),d=>Float(d,100,-1),d=>Put(d,116,0)}) {
            var changed=(byte[])bytes.Clone();mutation(changed);assert(Reject(changed),"Unknown versions, forms, malformed offsets, coordinates and missing ends cannot grant a bound");
        }
        for(int length=0;length<bytes.Length;length++)assert(Reject(bytes[..length]),"Every truncated valid file is rejected");
        // A second variant contains a farther box. Neither the first variant
        // nor a missing convex reference may hide collision geometry.
        var two=new byte[216];Array.Copy(bytes,two,bytes.Length);Put(two,8,2);Put(two,36,200);Put(two,200,48);Put(two,204,120);
        Array.Copy(bytes,48,two,120,72);Float(two,144,30);
        assert(PmcEnvelope.Radius(two)>30+Math.Sqrt(12),"Every variant contributes to the conservative enclosure");
        var convex=new byte[200];Put(convex,0,6);Put(convex,8,1);Put(convex,16,1);Put(convex,32,40);Put(convex,36,56);
        Put(convex,40,3);Put(convex,44,3);Put(convex,48,64);Put(convex,52,100);Put(convex,56,112);
        Float(convex,64,2);Float(convex,80,3);Float(convex,96,4);
        BinaryPrimitives.WriteUInt16LittleEndian(convex.AsSpan(100),0);BinaryPrimitives.WriteUInt16LittleEndian(convex.AsSpan(102),1);BinaryPrimitives.WriteUInt16LittleEndian(convex.AsSpan(104),2);
        Put(convex,112,8);Put(convex,116,56);Float(convex,160,1);Put(convex,164,0);Put(convex,172,-1);
        assert(PmcEnvelope.Radius(convex)>4,"Convex vertices bound the full physical mesh");
        Put(convex,164,1);assert(Reject(convex),"Missing convex pieces cannot be treated as empty");
    }
    static void Put(byte[] bytes,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at),value);
    static void Float(byte[] bytes,int at,float value)=>Put(bytes,at,BitConverter.SingleToInt32Bits(value));
}
