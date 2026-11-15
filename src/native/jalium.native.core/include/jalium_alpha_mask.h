#pragma once
#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace jalium {
// Borrowed immutable alpha pixels. Sampling is in destination pixel space and
// preserves the mask's local coordinate system under arbitrary 2-D transforms.
class AlphaMaskSampler {
public:
    static bool Valid(const uint8_t* pixels,uint32_t bytes,uint32_t width,uint32_t height,uint32_t stride,
        float x,float y,float w,float h) {
        return pixels&&width&&height&&stride>=width&&uint64_t(width)*height<=16777216u&&
            uint64_t(height-1)*stride+width<=bytes&&std::isfinite(x)&&std::isfinite(y)&&
            std::isfinite(w)&&std::isfinite(h)&&w>0&&h>0;
    }
    AlphaMaskSampler(const uint8_t* pixels,uint32_t width,uint32_t height,uint32_t stride,
        double x,double y,double w,double h,const float* matrix)
        : pixels_(pixels),width_(width),height_(height),stride_(stride),x_(x),y_(y),w_(w),h_(h) {
        double det=double(matrix[0])*matrix[3]-double(matrix[1])*matrix[2];
        valid_=std::isfinite(det)&&std::abs(det)>1e-30;
        for(int i=0;i<6;++i)valid_=valid_&&std::isfinite(matrix[i]);
        if(valid_){a_=matrix[3]/det;b_=-matrix[1]/det;c_=-matrix[2]/det;d_=matrix[0]/det;tx_=matrix[4];ty_=matrix[5];}
    }
    uint8_t At(double screenX,double screenY) const {
        if(!valid_)return 0;
        double dx=screenX-tx_,dy=screenY-ty_;
        double u=(a_*dx+c_*dy-x_)/w_,v=(b_*dx+d_*dy-y_)/h_;
        if(!(u>=0&&u<1&&v>=0&&v<1))return 0;
        double px=std::clamp(u*width_-.5,0.,double(width_-1)),py=std::clamp(v*height_-.5,0.,double(height_-1));
        auto ix=uint32_t(px),iy=uint32_t(py);auto nx=std::min(ix+1,width_-1),ny=std::min(iy+1,height_-1);
        auto value=[&](uint32_t x,uint32_t y){return double(pixels_[size_t(y)*stride_+x]);};
        double fx=px-ix,fy=py-iy;
        double upper=value(ix,iy)*(1-fx)+value(nx,iy)*fx,lower=value(ix,ny)*(1-fx)+value(nx,ny)*fx;
        return uint8_t(std::clamp(std::lround(upper*(1-fy)+lower*fy),0l,255l));
    }
private:
    const uint8_t* pixels_;uint32_t width_,height_,stride_;
    double x_,y_,w_,h_,a_=0,b_=0,c_=0,d_=0,tx_=0,ty_=0;bool valid_=false;
};
}
