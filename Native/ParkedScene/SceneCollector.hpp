#pragma once
#include "Protocol.h"
#include "prism/traffic/objects/traffic_parked_trailer.hpp"
#include <algorithm>
#include <cmath>
#include <limits>
#include <unordered_set>
#include <vector>

namespace maut_scene {
namespace prism = ets2la_plugin::prism;

// Called only from the SDK simulation thread, on current validated actor memory.
// The static_cast is necessary: parked trailers' actor base is +0x38 on Win64.
template<class Readable, class Emit>
bool WalkParkedChain(const prism::traffic_parked_actor_t* root, Readable readable, Emit emit) {
 std::unordered_set<const prism::traffic_parked_actor_t*> chain;
 const auto* actor=root;
 for(unsigned depth=0; actor; ++depth) {
  if(depth>=32 || !readable(actor,sizeof(*actor)) || !chain.insert(actor).second) return false;
  if(!emit(actor,depth!=0)) return false;
  auto* trailer=actor->slave_trailer;
  if(trailer && !readable(trailer,sizeof(*trailer))) return false;
  actor=trailer ? static_cast<const prism::traffic_parked_actor_t*>(trailer) : nullptr;
 }
 return true;
}

inline bool MakeBody(const prism::traffic_parked_actor_t* actor, bool trailer, std::uint64_t id, std::uint64_t group, Body& out) {
 const auto& a=actor->aabox;
 const float lo[3]={a.start.x,a.start.y,a.start.z}, hi[3]={a.end.x,a.end.y,a.end.z};
 const auto& q=actor->placement.rot;
 const double norm=double(q.x)*q.x+double(q.y)*q.y+double(q.z)*q.z+double(q.w)*q.w;
 if(!std::isfinite(norm) || std::abs(norm-1)>0.002) return false;
 // Game rotation must already be unit length. No expansion/shrink ambiguity.
 if(std::abs(norm-1)>0.00001) return false;
 auto root=actor->placement.to_global_position();
 float x[3]={root.x,root.y,root.z};
 ets2la_plugin::float3_t midpoint{(lo[0]+hi[0])*.5f,(lo[1]+hi[1])*.5f,(lo[2]+hi[2])*.5f};
 auto offset=midpoint.rotate(q);
 float delta[3]={offset.x,offset.y,offset.z};
 out={}; out.id=id; out.group=group; out.flags=trailer ? Trailer:0;
 out.rotationXYZW[0]=q.x;out.rotationXYZW[1]=q.y;out.rotationXYZW[2]=q.z;out.rotationXYZW[3]=q.w;
 for(unsigned k=0;k<3;++k) {
  if(!std::isfinite(lo[k])||!std::isfinite(hi[k])||hi[k]<=lo[k]||hi[k]-lo[k]>100||std::abs(lo[k])>100||std::abs(hi[k])>100||!std::isfinite(x[k])) return false;
  out.center[k]=x[k]+delta[k];
  if(!std::isfinite(out.center[k])||std::abs(out.center[k])>262144) return false;
  // Model boxes, not collision proof; 5 cm covers float transforms/normalization.
  out.halfExtents[k]=(hi[k]-lo[k])*.5f+0.05f;
 }
 return true;
}

template<class Readable>
bool ObserveMotion(const prism::traffic_parked_actor_t* actor,Readable readable,Body& body) {
 body.flags&=~MotionKnown;body.speed=0;
 // This is the physics link of this adjusted actor, not its root truck or a
 // distance-sorted registry neighbor. Missing physics is unknown, never zero.
 if(!actor->physics || !readable(actor->physics,sizeof(*actor->physics))) return false;
 const auto* physics=*actor->physics;
 if(!readable(physics,sizeof(*physics)))return false;
 if(!std::isfinite(physics->speed)||std::abs(physics->speed)>100||
    !std::isfinite(physics->acceleration)||std::abs(physics->acceleration)>50)return false;
 const float size[3]={physics->bounding_box.x,physics->bounding_box.y,physics->bounding_box.z};
 const float center[3]={physics->center_point.x,physics->center_point.y,physics->center_point.z};
 for(unsigned k=0;k<3;++k)if(!std::isfinite(size[k])||size[k]<=0||size[k]>200||!std::isfinite(center[k])||std::abs(center[k])>200)return false;
 body.speed=std::abs(physics->speed);body.flags|=MotionKnown;return true;
}

inline double SurfaceDistance(const Body& b,const double own[3]) {
 // Conservative lower bound using the OBB's enclosing sphere, actual body center.
 double r=0,d=0;
 for(unsigned k=0;k<3;++k){r+=double(b.halfExtents[k])*b.halfExtents[k];double x=b.center[k]-own[k];d+=x*x;}
 return std::max(0.0,std::sqrt(d)-std::sqrt(r));
}

inline void StoreBodies(Snapshot& snapshot,std::vector<Body>& bodies) {
 auto distance=[&](const Body& b){return SurfaceDistance(b,snapshot.header.ownPosition);};
 std::stable_sort(bodies.begin(),bodies.end(),[&](const Body& a,const Body& b){return distance(a)<distance(b);});
 snapshot.header.bodyTotal=static_cast<std::uint32_t>(bodies.size());
 snapshot.header.bodyCount=std::min<unsigned>(Capacity,bodies.size());
 snapshot.header.omittedMinSurfaceDistance=std::numeric_limits<double>::infinity();
 for(unsigned i=0;i<bodies.size();++i) {
  if(i<Capacity) snapshot.bodies[i]=bodies[i];
  else snapshot.header.omittedMinSurfaceDistance=std::min(snapshot.header.omittedMinSurfaceDistance,distance(bodies[i]));
 }
}
}
