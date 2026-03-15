#pragma once

#include "jalium_api.h"
#include "jalium_backend.h"
#include <atomic>
#include <memory>
#include <string>

namespace jalium {

/// Singleton registry for rendering backends.
/// Uses atomic operations for thread-safety to avoid loader lock issues when called from DllMain.
class BackendRegistry {
public:
    static BackendRegistry& Instance() {
        static BackendRegistry instance;
        return instance;
    }

    JaliumResult Register(JaliumBackend backend, JaliumBackendFactory factory) {
        return Register(backend, factory, nullptr);
    }

    JaliumResult Register(
        JaliumBackend backend,
        JaliumBackendFactory factory,
        JaliumBackendAvailabilityCallback availability)
    {
        // Use simple array-based storage with atomic writes to avoid mutex in DllMain context
        // This is safe because we only have a small fixed number of backend types
        int index = static_cast<int>(backend);
        if (index >= 0 && index < MAX_BACKENDS) {
            factories_[index].store(factory, std::memory_order_release);
            availability_[index].store(availability, std::memory_order_release);
            return JALIUM_OK;
        }
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    JaliumBackendFactory GetFactory(JaliumBackend backend) {
        int index = static_cast<int>(backend);
        if (index >= 0 && index < MAX_BACKENDS) {
            return factories_[index].load(std::memory_order_acquire);
        }
        return nullptr;
    }

    JaliumBackendAvailabilityCallback GetAvailability(JaliumBackend backend) {
        int index = static_cast<int>(backend);
        if (index >= 0 && index < MAX_BACKENDS) {
            return availability_[index].load(std::memory_order_acquire);
        }
        return nullptr;
    }

    bool IsAvailable(JaliumBackend backend) {
        auto factory = GetFactory(backend);
        if (!factory) {
            return false;
        }

        auto availability = GetAvailability(backend);
        return availability == nullptr || availability() != 0;
    }

private:
    BackendRegistry() {
        for (int i = 0; i < MAX_BACKENDS; ++i) {
            factories_[i].store(nullptr, std::memory_order_relaxed);
            availability_[i].store(nullptr, std::memory_order_relaxed);
        }
    }
    ~BackendRegistry() = default;
    BackendRegistry(const BackendRegistry&) = delete;
    BackendRegistry& operator=(const BackendRegistry&) = delete;

    static constexpr int MAX_BACKENDS = 16;
    std::atomic<JaliumBackendFactory> factories_[MAX_BACKENDS];
    std::atomic<JaliumBackendAvailabilityCallback> availability_[MAX_BACKENDS];
};

inline BackendRegistry& GetBackendRegistry() {
    return BackendRegistry::Instance();
}

/// Internal context implementation.
class Context {
public:
    Context(JaliumBackend backend, std::unique_ptr<IRenderBackend> backendImpl)
        : backend_(backend)
        , backendImpl_(std::move(backendImpl))
        , lastError_(JALIUM_OK)
    {}

    JaliumBackend GetBackend() const { return backend_; }
    IRenderBackend* GetBackendImpl() const { return backendImpl_.get(); }

    JaliumResult GetLastError() const { return lastError_; }
    void SetLastError(JaliumResult error, const std::wstring& message = L"") {
        lastError_ = error;
        errorMessage_ = message;
    }

    const wchar_t* GetErrorMessage() const {
        return errorMessage_.empty() ? nullptr : errorMessage_.c_str();
    }

private:
    JaliumBackend backend_;
    std::unique_ptr<IRenderBackend> backendImpl_;
    JaliumResult lastError_;
    std::wstring errorMessage_;
};

// Helper to get backend from context
inline IRenderBackend* GetBackendFromContext(JaliumContext* ctx) {
    if (!ctx) return nullptr;
    return reinterpret_cast<Context*>(ctx)->GetBackendImpl();
}

} // namespace jalium
