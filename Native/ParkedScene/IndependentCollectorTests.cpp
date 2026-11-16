// Pure fake native objects using the pinned real SDK class layouts.
// No SDK initialization, process memory access, mappings, or game callbacks.
#include "SceneCollector.hpp"
#include <cstdio>
#include <memory>
#include <stdexcept>

namespace ets2la_plugin::prism {
void traffic_object_t::destructor() {}
ETrafficObjectType traffic_object_t::get_type() const {return traffic_parked_vehicle;}
void traffic_obstacle_t::destructor() {}
void traffic_model_object_t::destructor() {}
void traffic_physics_object_t::destructor() {}
void traffic_physics_t::destructor() {}
template<class T> void array_t<T>::destructor() {}
}

namespace {
unsigned checks=0;
void Check(bool okay,const char* message){++checks;if(!okay)throw std::runtime_error(message);}
bool Near(double a,double b){return std::abs(a-b)<.0001;}
using maut_scene::prism::traffic_parked_actor_t;
using maut_scene::prism::traffic_parked_trailer_t;
void Pose(traffic_parked_actor_t& actor,float x,float z) {
 actor.placement={};actor.placement.pos={x,0,z};actor.placement.rot={1,0,0,0};
 actor.aabox.start={-1,0,-4};actor.aabox.end={1,4,4};
}
}
int main() {
 try {
  using namespace maut_scene;
  static_assert(sizeof(Header)==160&&sizeof(Body)==64&&sizeof(Snapshot)==41120);
  static_assert(offsetof(Header,ownPosition)==56&&offsetof(Header,omittedMinSurfaceDistance)==96);
  static_assert(offsetof(Body,center)==16&&offsetof(Body,rotationXYZW)==28&&offsetof(Body,halfExtents)==44&&offsetof(Body,group)==56);
  traffic_parked_actor_t root{};traffic_parked_trailer_t first{},second{};
  auto* firstBase=static_cast<traffic_parked_actor_t*>(&first);auto* secondBase=static_cast<traffic_parked_actor_t*>(&second);
  Check(reinterpret_cast<const char*>(firstBase)-reinterpret_cast<const char*>(&first)==0x38,"Real Win64 multiple-inheritance actor offset must be +0x38");
  Pose(root,1,2);Pose(*firstBase,20,30);Pose(*secondBase,-40,50);
  root.slave_trailer=&first;firstBase->slave_trailer=&second;
  std::vector<const traffic_parked_actor_t*> visited;std::vector<Body> bodies;
  auto readable=[](const void* p,std::size_t){return p!=nullptr;};
  auto emit=[&](const traffic_parked_actor_t* p,bool trailer){
   visited.push_back(p);Body body{};if(!MakeBody(p,trailer,visited.size(),1,body))return false;bodies.push_back(body);return true;
  };
  Check(WalkParkedChain(&root,readable,emit),"Valid real-layout truck + two trailers must enumerate");
  Check(visited.size()==3&&visited[0]==&root&&visited[1]==firstBase&&visited[2]==secondBase,"A trailer cannot be replaced by another copy of its tractor pointer");
  Check(bodies.size()==3&&Near(bodies[1].center[0],20)&&Near(bodies[1].center[2],30)&&Near(bodies[2].center[0],-40),"Each slave publishes its own distinct actual placement");
  Check(bodies[0].flags==0&&bodies[1].flags==Trailer&&bodies[2].flags==Trailer,"Trailer semantics must survive real base-pointer adjustment");
  Check(Near(bodies[0].halfExtents[0],1.05)&&Near(bodies[0].halfExtents[1],2.05)&&Near(bodies[0].rotationXYZW[3],1),"Physical half-extents and XYZW ordering match the wire contract");
  secondBase->slave_trailer=&first;
  Check(!WalkParkedChain(&root,readable,[](const auto*,bool){return true;}),"A repeated trailer in the same chain cannot establish completeness");
  secondBase->slave_trailer=nullptr;
  Check(!WalkParkedChain(&root,[&](const void* p,std::size_t){return p!=&first&&p!=firstBase;},[](const auto*,bool){return true;}),"Unreadable derived or base memory cannot be silently dropped");
  Check(!WalkParkedChain(&root,readable,[](const auto*,bool){return false;}),"A failed body conversion propagates incomplete enumeration");
  std::vector<std::unique_ptr<traffic_parked_trailer_t>> longChain;
  for(unsigned i=0;i<32;++i){longChain.push_back(std::make_unique<traffic_parked_trailer_t>());Pose(*static_cast<traffic_parked_actor_t*>(longChain.back().get()),i,0);}
  root.slave_trailer=longChain[0].get();
  for(unsigned i=0;i<31;++i)static_cast<traffic_parked_actor_t*>(longChain[i].get())->slave_trailer=longChain[i+1].get();
  Check(!WalkParkedChain(&root,readable,[](const auto*,bool){return true;}),"A chain above the explicit depth bound cannot claim complete coverage");
  root.slave_trailer=nullptr;
  root.placement.cx=2;root.placement.cz=-3;root.placement.rot={float(std::sqrt(.5)),0,float(std::sqrt(.5)),0};
  root.aabox.start={0,0,0};root.aabox.end={2,4,8};Body rotated{};
  Check(MakeBody(&root,false,1,1,rotated),"Nontrivial unit rotation and cell coordinates are supported");
  Check(Near(rotated.center[0],1029)&&Near(rotated.center[1],2)&&Near(rotated.center[2],-1535),"Model-origin offset is rotated before world-cell placement");
  root.placement.rot.w=2;Check(!MakeBody(&root,false,1,1,rotated),"A nonunit pose cannot shrink a physical enclosure");
  Pose(root,0,0);root.aabox.end.x=std::numeric_limits<float>::quiet_NaN();Check(!MakeBody(&root,false,1,1,rotated),"Nonfinite native dimensions fail closed");
  Pose(root,0,0);root.aabox.end.x=root.aabox.start.x;Check(!MakeBody(&root,false,1,1,rotated),"Degenerate native dimensions fail closed");
  Pose(root,0,0);Body motion{};MakeBody(&root,false,1,1,motion);
  Check(!ObserveMotion(&root,readable,motion)&&motion.speed==0&&(motion.flags&MotionKnown)==0,"An absent physics link is unknown, not a measured stationary actor");
  ets2la_plugin::prism::traffic_chassis_physics_t physics{};auto* physicsPointer=&physics;root.physics=&physicsPointer;
  physics.bounding_box={2,4,8};physics.center_point={0,2,0};physics.speed=-3;physics.acceleration=0;
  Check(ObserveMotion(&root,readable,motion)&&Near(motion.speed,3)&&(motion.flags&MotionKnown)!=0,"Motion comes from this exact actor's two-level physics member, conservatively without sign");
  root.physics=nullptr;Check(!ObserveMotion(&root,readable,motion)&&motion.speed==0&&(motion.flags&MotionKnown)==0,"A reused previously-known body loses motion authority when its actual physics disappears");root.physics=&physicsPointer;
  auto UnknownMotion=[&](auto canRead){Body unknown{};MakeBody(&root,false,1,1,unknown);return !ObserveMotion(&root,canRead,unknown)&&(unknown.flags&MotionKnown)==0&&unknown.speed==0;};
  Check(UnknownMotion([&](const void* p,std::size_t){return p!=root.physics;}),"An unreadable outer physics pointer cannot yield motion evidence");
  Check(UnknownMotion([&](const void* p,std::size_t){return p!=physicsPointer;}),"An unreadable actual physics object cannot yield motion evidence");
  physicsPointer=nullptr;Check(UnknownMotion([](const void* p,std::size_t){return p!=nullptr;}),"A readable outer link containing null remains unknown");physicsPointer=&physics;
  for(float invalid:{std::numeric_limits<float>::quiet_NaN(),std::numeric_limits<float>::infinity(),101.f}){
   physics.speed=invalid;Check(UnknownMotion(readable),"Nonfinite or implausible scalar velocity is not known motion");
  }
  physics.speed=0;physics.acceleration=51;Check(UnknownMotion(readable),"Implausible acceleration invalidates the correlated physics observation");
  physics.acceleration=0;physics.bounding_box.x=0;Check(UnknownMotion(readable),"An uninitialized physics object cannot mimic a valid stationary body");
  physics.bounding_box.x=2;physics.center_point.x=std::numeric_limits<float>::quiet_NaN();Check(UnknownMotion(readable),"Invalid physics reference data cannot confer motion authority");
  physics.center_point.x=0;Body still{};MakeBody(&root,false,1,1,still);
  Check(ObserveMotion(&root,readable,still)&&still.speed==0&&(still.flags&MotionKnown)!=0,"Actual finite stationary physics is distinguished from missing physics");
  root.physics=nullptr;firstBase->physics=&physicsPointer;physics.speed=7;Body slaveMotion{};MakeBody(firstBase,true,2,1,slaveMotion);
  Check(ObserveMotion(firstBase,readable,slaveMotion)&&Near(slaveMotion.speed,7),"A trailer uses its own correctly adjusted actor physics even when the tractor has none");
  Snapshot snapshot{};std::vector<Body> crowded;
  for(unsigned i=0;i<512;++i){Body b{};b.id=i+1;b.center[0]=10;b.halfExtents[0]=b.halfExtents[1]=b.halfExtents[2]=1;crowded.push_back(b);}
  Body longTrailer{};longTrailer.id=1000;longTrailer.center[0]=30;longTrailer.halfExtents[0]=40;longTrailer.halfExtents[1]=2;longTrailer.halfExtents[2]=1;crowded.push_back(longTrailer);
  StoreBodies(snapshot,crowded);
  Check(snapshot.header.bodyCount==512&&snapshot.header.bodyTotal==513,"Capacity overflow preserves both exported and enumerated counts");
  Check(snapshot.bodies[0].id==1000,"A far center with a nearby long body surface cannot be omitted behind closer origins");
  Check(std::isfinite(snapshot.header.omittedMinSurfaceDistance)&&snapshot.header.omittedMinSurfaceDistance<10,"A saturated scene supplies a finite conservative omitted-surface bound");
  StoreBodies(snapshot,bodies);Check(std::isinf(snapshot.header.omittedMinSurfaceDistance)&&snapshot.header.bodyTotal==3,"An uncapped list explicitly represents no omitted bodies");
  std::printf("PASS %u independent native collector assertions; real SDK layout, no game or shared memory.\n",checks);return 0;
 } catch(const std::exception& e){std::printf("FAIL native collector after %u checks: %s\n",checks,e.what());return 1;}
}
