// Offline format research only; not a live clearance authority.
using System.Buffers.Binary;
using System.Numerics;
namespace MautAssistent;

// Geometry-only PMC v6 enclosure. Format fields independently implemented from
// ConverterPIX's published pmc.h; no game data or third-party parser is bundled.
// Include every variant and rotation; unsupported shapes never yield a bound.
public static class PmcEnvelope
{
    public static double Radius(byte[] data)
    {
        if(data.Length>64*1024*1024)throw new InvalidDataException("PMC zu groß");
        void Range(long at,long bytes){if(at<0||bytes<0||at>data.Length-bytes)throw new InvalidDataException("Ungültiger PMC-Datenbereich");}
        int I(int at){Range(at,4);return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));}
        float F(int at){float value=BitConverter.Int32BitsToSingle(I(at));if(!float.IsFinite(value))throw new InvalidDataException("Ungültige PMC-Zahl");return value;}
        Vector3 V(int at)=>new(F(at),F(at+4),F(at+8));
        void Block(int at,long bytes){if(bytes>0&&at<40)throw new InvalidDataException("PMC-Geometrie im Header");Range(at,bytes);}
        Range(0,40);if(I(0)!=6)throw new InvalidDataException("Nicht unterstützte PMC-Version");
        int variants=I(8),pieces=I(16),pieceTable=I(32),variantTable=I(36);
        if(variants is <1 or >256||pieces is <0 or >4096)throw new InvalidDataException("Ungültige PMC-Anzahlen");
        Block(pieceTable,16L*pieces);Block(variantTable,4L*variants);
        var radii=new double[pieces];long vertices=0,indices=0;
        for(int p=0;p<pieces;p++) {
            int at=pieceTable+16*p,count=I(at+4),edgeCount=I(at),points=I(at+8),faces=I(at+12);
            if(count is <1 or >100000||edgeCount is <3 or >300000||edgeCount%3!=0||
                (vertices+=count)>1000000||(indices+=edgeCount)>3000000)throw new InvalidDataException("PMC-Prüfgrenze überschritten");
            Block(points,12L*count);Block(faces,2L*edgeCount);
            for(int i=0;i<count;i++)radii[p]=Math.Max(radii[p],Length(V(points+12*i)));
            for(int i=0;i<edgeCount;i++)if(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(faces+2*i))>=count)
                throw new InvalidDataException("Ungültiger PMC-Dreiecksindex");
        }
        double radius=0;int shapes=0;
        for(int v=0;v<variants;v++) {
            int cursor=I(variantTable+4*v),local=0;
            for(;;) {
                Block(cursor,8);int type=I(cursor),size=I(cursor+4);if(size==-1)break;
                if(++local>4096||++shapes>65536)throw new InvalidDataException("PMC-Locatorgrenze überschritten");
                if((type,size) is not ((1,64) or (8,56)))throw new InvalidDataException("Nicht unterstützte PMC-Form");
                Block(cursor,size);var center=V(cursor+24);double norm=0;
                for(int i=0;i<4;i++){double q=F(cursor+36+4*i);norm+=q*q;}
                if(Math.Abs(norm-1)>.0001)throw new InvalidDataException("Ungültige PMC-Rotation");
                double reach;
                if(type==8) {int piece=I(cursor+52);if(piece<0||piece>=pieces)throw new InvalidDataException("Ungültige PMC-Convexreferenz");reach=radii[piece];}
                else {var scale=V(cursor+52);if(scale.X<0||scale.Y<0||scale.Z<0)throw new InvalidDataException("Ungültige PMC-Box");reach=Length(scale);}
                // Full scale overbounds both half/full-extent conventions.
                // Small quaternion and arithmetic differences are overbounded.
                radius=Math.Max(radius,Length(center)+reach*1.001+.01);
                cursor=checked(cursor+size);
            }
        }
        if(shapes==0||!double.IsFinite(radius)||radius<=0||radius>100)throw new InvalidDataException("Keine begrenzbare PMC-Hülle");
        return radius;
    }
    static double Length(Vector3 p)=>Math.Sqrt((double)p.X*p.X+(double)p.Y*p.Y+(double)p.Z*p.Z);
}
