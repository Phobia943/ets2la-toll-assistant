using System.Buffers.Binary;
using System.Numerics;
using TruckLib;
using TruckLib.Models;

namespace MautAssistent;

// Exact PMC v6 box/convex surfaces of a selected STATIC sign variant. No
// spheres around every variant and no substitution of its visible PMG mesh.
// Layout: github.com/mwl4/ConverterPIX, src/structs/pmc.h and math/quaternion.h.
// Full box dimensions: SCSSoftware/BlenderTools, exp/pic.py Parameters(dimX,Y,Z).
internal static class StaticRoadSignGeometry
{
    internal sealed record Result(RigStaticGeometry? Geometry,string? Issue);
    internal static Result Capture(Model model,byte[] bytes,string variant,Vector3 position,Quaternion rotation)
    {
        try {
            if(bytes.Length is <40 or >4*1024*1024||!Finite(position)||position.Length()>1000000||
                !float.IsFinite(rotation.LengthSquared())||Math.Abs(rotation.LengthSquared()-1)>.00001f||model.Skeleton.Count!=0)
                throw new InvalidDataException("Invalid static sign geometry or placement");
            void Range(long at,long count){if(at<0||count<0||at+count>bytes.Length)throw new InvalidDataException("PMC range");}
            var ranges=new List<(long From,long To,bool Shared)>();
            void Register(int at,long size,bool shared=false){Range(at,size);if(size==0)return;
                if(ranges.Any(r=>at<r.To&&r.From<at+size&&!(shared&&r.Shared&&at==r.From&&at+size==r.To)))throw new InvalidDataException("Overlapping PMC sections");
                ranges.Add((at,at+size,shared));}
            int I(int at){Range(at,4);return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));}
            float F(int at){float value=BitConverter.Int32BitsToSingle(I(at));if(!float.IsFinite(value))throw new InvalidDataException("PMC nonfinite");return value;}
            Vector3 V(int at){var value=new Vector3(F(at),F(at+4),F(at+8));if(value.Length()>1000)throw new InvalidDataException("PMC unbounded coordinates");return value;}
            string Token(int at){Range(at,8);return new Token(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at))).ToString();}
            int variants=I(8),pieces=I(16),names=I(24),pieceTable=I(32),defs=I(36);
            if(I(0)!=6||I(4)!=1||I(12)!=1||variants is <1 or >128||pieces is <0 or >1024)
                throw new InvalidDataException("Unsupported PMC header");
            Register(0,40);Register(I(20),8);Register(names,8L*variants);Register(I(28),4);Register(defs,4L*variants);Register(pieceTable,16L*pieces);
            if(names<40||defs<40||pieces>0&&pieceTable<40)throw new InvalidDataException("Overlapping PMC header");
            var variantNames=Enumerable.Range(0,variants).Select(i=>Token(names+i*8)).ToArray();
            var modelVariants=model.Variants.Select(v=>v.Name.ToString()).ToArray();
            if(variantNames.Distinct().Count()!=variants||!variantNames.SequenceEqual(modelVariants)||
                model.Variants.Any(v=>v.Attributes.Count!=model.Parts.Count||v.Attributes.Any(a=>a.Type!=0||a.Tag.ToString()!="visible"||a.Value>1)))
                throw new InvalidDataException("PMC variant names are not bound to the model");
            if(variant==""&&variants==1)variant=variantNames[0];
            int chosen=Array.IndexOf(variantNames,variant);if(chosen<0)throw new InvalidDataException("Unknown sign collision variant");
            var vertices=new Vector3[pieces][];var indices=new ushort[pieces][];int count=0;
            for(int p=0;p<pieces;p++) {
                int record=pieceTable+p*16,edges=I(record),size=I(record+4),at=I(record+8),faces=I(record+12);
                if(edges is <12 or >60000||edges%3!=0||size is <4 or >20000||at<40||faces<40)
                    throw new InvalidDataException("Invalid PMC convex piece");
                count=checked(count+edges/3);if(count>20000)throw new InvalidDataException("PMC mesh budget");
                Register(at,12L*size);Register(faces,2L*edges);vertices[p]=Enumerable.Range(0,size).Select(i=>V(at+12*i)).ToArray();
                indices[p]=new ushort[edges];
                for(int e=0;e<edges;e++){ushort value=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(faces+2*e));if(value>=size)throw new InvalidDataException("PMC convex index");indices[p][e]=value;}
            }
            var output=new List<StaticTriangle>();var world=Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(position);
            // Validate all variant record boundaries, but only the selected
            // variant's bodies contribute. Locator names are NOT PMG part IDs.
            for(int variantIndex=0;variantIndex<variants;variantIndex++) {
                int at=I(defs+variantIndex*4),begin=at;if(at<40)throw new InvalidDataException("Invalid PMC variant offset");
                int locators=0;
                for(;;) {
                    Range(at,8);int type=I(at),size=I(at+4);if(size==-1){Register(begin,at+8L-begin,true);break;}
                    if(++locators>512||((type,size) is not ((1,64) or (8,56))))throw new InvalidDataException("Unsupported PMC collider");
                    Range(at,size);if(I(at+8)!=0||F(at+12)<0)throw new InvalidDataException("Unknown PMC alias or weight");
                    _=Token(at+16);var location=V(at+24);
                    var q=new Quaternion(F(at+40),F(at+44),F(at+48),F(at+36)); // file WXYZ
                    if(Math.Abs(q.LengthSquared()-1)>.00001f)throw new InvalidDataException("Invalid PMC quaternion");
                    var transform=Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(location)*world;
                    void Add(Vector3 a,Vector3 b,Vector3 c){
                        if(variantIndex!=chosen)return;
                        a=Vector3.Transform(a,transform);b=Vector3.Transform(b,transform);c=Vector3.Transform(c,transform);
                        if(!Finite(a)||!Finite(b)||!Finite(c))throw new InvalidDataException("Invalid PMC world coordinates");
                        if(output.Count>=20000)throw new InvalidDataException("PMC transformed mesh budget");
                        if(Vector3.Cross(b-a,c-a).LengthSquared()<1e-12f)throw new InvalidDataException("Degenerate PMC face");
                        output.Add(new(a,b,c));
                    }
                    if(type==1) {
                        var dimensions=V(at+52);if(dimensions.X<=0||dimensions.Y<=0||dimensions.Z<=0)throw new InvalidDataException("Invalid PMC box");
                        var half=dimensions*.5f;var corners=Enumerable.Range(0,8).Select(i=>new Vector3((i&1)==0?-half.X:half.X,(i&2)==0?-half.Y:half.Y,(i&4)==0?-half.Z:half.Z)).ToArray();
                        int[] box=[0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5];
                        for(int f=0;f<box.Length;f+=3)Add(corners[box[f]],corners[box[f+1]],corners[box[f+2]]);
                    } else {
                        int piece=I(at+52);if(piece<0||piece>=pieces)throw new InvalidDataException("PMC missing convex piece");
                        for(int f=0;f<indices[piece].Length;f+=3)Add(vertices[piece][indices[piece][f]],vertices[piece][indices[piece][f+1]],vertices[piece][indices[piece][f+2]]);
                    }
                    at=checked(at+size);
                }
            }
            if(output.Count==0)throw new InvalidDataException("Selected sign collision variant is empty");
            return new(new RigStaticGeometry(output),null);
        }catch(Exception e) when(e is InvalidDataException or OverflowException or ArgumentException or IndexOutOfRangeException){return new(null,e.Message);}
    }
    static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
}
