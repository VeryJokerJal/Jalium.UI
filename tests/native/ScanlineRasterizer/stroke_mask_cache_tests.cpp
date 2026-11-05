#include "d3d12_stroke_mask_cache.h"
#include "d3d12_coverage_rect.h"
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <new>
#include <stdexcept>

static bool counting=false;
static size_t allocations=0;
void* operator new(size_t n){if(counting)++allocations;if(auto p=std::malloc(n?n:1))return p;throw std::bad_alloc();}
void* operator new[](size_t n){return ::operator new(n);}
void operator delete(void* p)noexcept{std::free(p);}
void operator delete[](void* p)noexcept{std::free(p);}
void operator delete(void* p,size_t)noexcept{std::free(p);}
void operator delete[](void* p,size_t)noexcept{std::free(p);}
using namespace jalium; using Cache = D3D12StrokeMaskCache<D3D12CoverageMask>;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
std::shared_ptr<D3D12CoverageMask> Mask(size_t count,int tag,size_t reserve=0){
    auto result=std::make_shared<D3D12CoverageMask>();
    if(reserve)result->rects.reserve(reserve);
    result->rects.resize(count);
    for(auto& rect:result->rects)rect={tag,tag+1,3,4,.25f};
    if(count)result->UpdateBounds();
    return result;
}
int main(){try{
    Cache::Limits limits;limits.smallCount=2;limits.largeCount=2;
    {
        Cache cache(limits);
        cache.Insert(100,Mask(128,100));cache.Insert(101,Mask(128,101));
        for(uint64_t key=1;key<100;++key)cache.Insert(key,Mask(5,int(key)));
        Check(cache.Find(100)&&cache.Find(101),"Small churn evicted expensive masks");
        Check(cache.small_size()==2&&cache.large_size()==2,"Lane count limit differs");
        cache.Find(100);cache.Insert(102,Mask(128,102));
        Check(cache.Find(100)&&!cache.Find(101)&&cache.Find(102),"Large lane LRU order differs");
    }
    {
        auto storage=Mask(128,7,1024);
        auto limited=limits;limited.largeBytes=Cache::AllocatedBytes(*storage)-1;
        Cache cache(limited);
        cache.Insert(1,std::move(storage));
        Check(!cache.Find(1)&&cache.large_bytes()==0,"Budget used logical size instead of vector capacity");
    }
    {
        auto first=Mask(128,11),second=Mask(128,12);
        auto limited=limits;limited.largeCount=20;
        limited.largeBytes=Cache::AllocatedBytes(*first)+Cache::AllocatedBytes(*second);
        Cache cache(limited);
        cache.Insert(1,std::move(first));cache.Insert(2,std::move(second));cache.Insert(3,Mask(128,13));
        Check(cache.large_bytes()<=limited.largeBytes&&!cache.Find(1),"Byte budget did not evict the oldest mask");
    }
    {
        Cache cache(limits);
        cache.Insert(1,Mask(128,41));
        auto borrowed=cache.Find(1);auto original=borrowed->rects;
        cache.Insert(2,Mask(128,42));cache.Insert(3,Mask(128,43));
        for(int i=0;i<50;++i){auto next=cache.Acquire();Check(next.get()!=borrowed.get(),"Borrowed mask recycled");next->rects.resize(128,{i,i,1,1,.5f});cache.Insert(100+i,std::move(next));}
        Check(original.size()==borrowed->rects.size()&&std::memcmp(original.data(),borrowed->rects.data(),original.size()*sizeof(PixelRect))==0,"Borrowed pending draw changed");
    }
    {
        auto single=limits;single.smallCount=1;
        Cache cache(single);
        auto old=Mask(5,22,256);auto pointer=old.get();
        cache.Insert(1,std::move(old));cache.Insert(2,Mask(5,23));
        auto reused=cache.Acquire();
        Check(reused.get()==pointer&&reused->rects.empty()&&!reused->exactBounds,"Unique allocation was not safely reset");
        Check(reused->rects.capacity()>=256,"Recycling discarded vector capacity");
    }
    {
        Cache cache(limits);
        cache.Insert(1,Mask(5,1));cache.Insert(1,Mask(128,2));
        Check(cache.size()==1&&cache.small_size()==0&&cache.large_size()==1&&cache.Find(1)->rects[0].x==2,"Replacement did not move lanes");
        cache.Insert(1,Mask(0,0));
        Check(cache.size()==1&&cache.large_size()==0&&cache.Find(1)->rects.empty(),"Negative cache replacement differs");
    }
    {
        auto base=Mask(128,1),large=Mask(384,4);
        auto limited=limits;limited.largeCount=20;
        limited.largeBytes=Cache::AllocatedBytes(*large);
        Check(3*Cache::AllocatedBytes(*base)<=limited.largeBytes,"Unexpected test allocation size");
        Cache cache(limited);
        cache.Insert(1,std::move(base));cache.Insert(2,Mask(128,2));cache.Insert(3,Mask(128,3));
        auto borrowed=cache.Find(2);
        Check(cache.Insert(4,std::move(large))==3,"Insert did not report every byte-budget eviction");
        Check(cache.size()==1&&cache.large_size()==1&&cache.large_bytes()==limited.largeBytes,"Multiple eviction accounting differs");
        Check(!cache.Find(1)&&!cache.Find(2)&&!cache.Find(3)&&cache.Find(4),"Multiple eviction left stale map entries");
        auto scratch=cache.Acquire();
        Check(scratch.get()!=borrowed.get()&&borrowed->rects[0].x==2,"Multiple eviction changed a pending borrower");
    }
    {
        auto limited=limits;limited.largeBytes=128*sizeof(PixelRect);
        Cache cache(limited);
        cache.Insert(1,Mask(128,7));auto borrowed=cache.Find(1);
        cache.Insert(1,Mask(256,8));
        Check(!cache.Find(1)&&cache.size()==0&&cache.large_bytes()==0,"Oversized replacement left stale data or accounting");
        auto scratch=cache.Acquire();
        Check(scratch.get()!=borrowed.get()&&borrowed->rects.size()==128&&borrowed->rects[0].x==7,"Oversized replacement recycled a borrower");
    }
    {
        auto old=Mask(5,9);std::weak_ptr<D3D12CoverageMask> lifetime=old;
        auto limited=limits;limited.smallCount=1;limited.recycledBytes=Cache::AllocatedBytes(*old)-1;
        Cache cache(limited);
        cache.Insert(1,std::move(old));cache.Insert(2,Mask(5,10));
        Check(lifetime.expired()&&cache.recycled_bytes()==0,"Oversized scratch allocation was retained");
    }
    {
        auto disabled=limits;disabled.smallCount=0;disabled.largeCount=0;
        Cache cache(disabled);
        cache.Insert(1,Mask(0,0));cache.Insert(2,Mask(128,1));
        Check(!cache.Find(1)&&!cache.Find(2)&&cache.size()==0&&cache.small_bytes()==0&&cache.large_bytes()==0,"Disabled lanes admitted masks");
    }
    {
        auto protectedOnly=limits;protectedOnly.smallCount=0;protectedOnly.smallBytes=0;
        Cache cache(protectedOnly);
        cache.Insert(1000,Mask(128,71));
        for(uint64_t key=1;key<500;++key)cache.Insert(key,Mask(5,int(key)));
        Check(cache.size()==1&&cache.small_size()==0&&cache.large_size()==1&&cache.Find(1000)->rects[0].x==71,"Disabled small lane disturbed protected masks");
    }
    {
        auto protectedOnly=limits;protectedOnly.smallCount=0;protectedOnly.smallBytes=0;protectedOnly.largeCount=1;
        Cache cache(protectedOnly);
        cache.Insert(1,Mask(128,81));
        auto primaryOwner=cache.Find(1);std::weak_ptr<const D3D12CoverageMask> lifetime=primaryOwner;
        cache.Insert(2,Mask(128,82));
        auto next=cache.Acquire();
        Check(next.get()!=primaryOwner.get()&&primaryOwner->rects[0].x==81,"Secondary eviction recycled a primary-cache owner");
        primaryOwner.reset();Check(lifetime.expired(),"Evicted secondary mask leaked after primary owner released it");
    }
    {
        Cache cache(limits);
        auto install=[&](uint64_t key){auto entry=cache.Acquire();entry->rects.reserve(256);entry->rects.resize(5,{int(key),0,1,1,1});cache.Insert(key,std::move(entry));};
        for(uint64_t key=1;key<10;++key)install(key);
        counting=true;allocations=0;
        for(uint64_t key=10;key<1010;++key)install(key);
        counting=false;Check(allocations==0,"Warm churn allocated list/map/mask storage");
        cache.ReleaseScratch();Check(cache.recycled_bytes()==0,"Scratch reclaim kept recycled storage");
    }
    std::cout<<"{\"cases\":13,\"borrowedMasksImmutable\":true,\"capacityBudgetChecked\":true,\"warmAllocations\":"<<allocations<<"}\n";
    return 0;
}catch(const std::exception& error){counting=false;std::cerr<<error.what()<<'\n';return 1;}}
