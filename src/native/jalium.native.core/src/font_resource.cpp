#include "jalium_font_resource.h"
#include "jalium_string_util.h"
#include <mutex>
#include <string>
#include <unordered_map>

#ifdef _WIN32
#include <Windows.h>
#include <dwrite_3.h>
#include <wrl/client.h>
#endif

namespace {
struct FontResource {
    std::shared_ptr<const std::vector<uint8_t>> bytes;
#ifdef _WIN32
    HANDLE gdi = nullptr;
    Microsoft::WRL::ComPtr<IDWriteFactory5> factory;
    Microsoft::WRL::ComPtr<IDWriteInMemoryFontFileLoader> loader;
    Microsoft::WRL::ComPtr<IDWriteFontFile> file;
    Microsoft::WRL::ComPtr<IDWriteFontCollection1> collection;
    bool registered = false;
    ~FontResource() {
        collection.Reset(); file.Reset();
        if (registered) factory->UnregisterFontFileLoader(loader.Get());
        if (gdi) RemoveFontMemResourceEx(gdi);
    }
    bool Initialize() {
        if (FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory5),
            reinterpret_cast<IUnknown**>(factory.GetAddressOf()))) ||
            FAILED(factory->CreateInMemoryFontFileLoader(&loader)) ||
            FAILED(factory->RegisterFontFileLoader(loader.Get()))) return false;
        registered = true;
        if (FAILED(loader->CreateInMemoryFontFileReference(factory.Get(), bytes->data(),
            static_cast<UINT32>(bytes->size()), nullptr, &file))) return false;
        Microsoft::WRL::ComPtr<IDWriteFontSetBuilder1> builder;
        Microsoft::WRL::ComPtr<IDWriteFontSet> set;
        if (FAILED(factory->CreateFontSetBuilder(&builder)) || FAILED(builder->AddFontFile(file.Get())) ||
            FAILED(builder->CreateFontSet(&set)) || set->GetFontCount() == 0 ||
            FAILED(factory->CreateFontCollectionFromFontSet(set.Get(), &collection))) return false;
        DWORD count = 0;
        gdi = AddFontMemResourceEx(const_cast<uint8_t*>(bytes->data()), static_cast<DWORD>(bytes->size()), nullptr, &count);
        return gdi && count;
    }
#else
    bool Initialize() { return true; }
#endif
};
std::mutex registryMutex;
std::unordered_map<std::wstring, std::weak_ptr<FontResource>> registry;

std::shared_ptr<FontResource> Find(const wchar_t* family)
{
    if (!family) return {};
    std::lock_guard<std::mutex> lock(registryMutex);
    const auto entry = registry.find(family);
    return entry == registry.end() ? nullptr : entry->second.lock();
}
}

struct JaliumFontResource { std::shared_ptr<FontResource> font; };

namespace jalium {
FontResourceReference AcquireFontResource(const wchar_t* family)
{
    auto font = Find(family);
    return font ? FontResourceReference(new JaliumFontResource{std::move(font)}, jalium_font_resource_release) : nullptr;
}

std::shared_ptr<const std::vector<uint8_t>> AcquireRegisteredFontData(const wchar_t* family)
{
    auto font = Find(family);
    return font ? font->bytes : nullptr;
}
}

extern "C" {
JALIUM_API JaliumFontResource* jalium_font_resource_register(const wchar_t* family, const uint8_t* data, uint32_t size)
{
    if (!family || !data || size < 12 || size > 512u * 1024u * 1024u) return nullptr;
    try {
        uint32_t length = 0;
        if (!jalium::ManagedUtf16LengthBounded(family, 30, &length) || !length) return nullptr;
        const auto name = jalium::ManagedToWString(family, length);
        auto font = std::make_shared<FontResource>();
        font->bytes = std::make_shared<const std::vector<uint8_t>>(data, data + size);
        if (!font->Initialize()) return nullptr;
        std::lock_guard<std::mutex> lock(registryMutex);
        auto existing = registry.find(name);
        if (existing != registry.end() && !existing->second.expired()) return nullptr;
        for (auto entry = registry.begin(); entry != registry.end(); )
            if (entry->second.expired()) entry = registry.erase(entry); else ++entry;
        registry[name] = font;
        return new JaliumFontResource{std::move(font)};
    } catch (...) { return nullptr; }
}

JALIUM_API JaliumFontResource* jalium_font_resource_acquire(const wchar_t* family)
{
    try {
        uint32_t length = 0;
        if (!family || !jalium::ManagedUtf16LengthBounded(family, 30, &length)) return nullptr;
        auto font = Find(jalium::ManagedToWString(family, length).c_str());
        return font ? new JaliumFontResource{std::move(font)} : nullptr;
    } catch (...) { return nullptr; }
}

JALIUM_API void jalium_font_resource_release(JaliumFontResource* resource) { delete resource; }

JALIUM_API void* jalium_font_resource_get_collection(JaliumFontResource* resource)
{
#ifdef _WIN32
    return resource ? resource->font->collection.Get() : nullptr;
#else
    (void)resource; return nullptr;
#endif
}
}
