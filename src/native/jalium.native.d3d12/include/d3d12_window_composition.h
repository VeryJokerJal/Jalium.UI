#pragma once
#include <windows.h>
#include <dxgi1_2.h>
#include <memory>
namespace jalium {
class D3D12WindowComposition {
public:
    static std::unique_ptr<D3D12WindowComposition> Create(HWND window, IDXGISwapChain1* swapChain, UINT width, UINT height);
    ~D3D12WindowComposition();
    bool SetPlacement(bool right, bool bottom, UINT width, UINT height);
    void SetResizePresentationEnabled(bool enabled);
private:
    struct State;
    explicit D3D12WindowComposition(std::shared_ptr<State> state);
    std::shared_ptr<State> state_;
};
}
