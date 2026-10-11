#include "jalium_font_resource.h"
#include "jalium_string_util.h"
#include "jalium_abi_guard.h"
#include <algorithm>
#include <cstring>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_map>

#ifdef __APPLE__
#include <CoreText/CoreText.h>
#include <CoreGraphics/CoreGraphics.h>
#endif

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
#elif defined(__APPLE__)
    CGFontRef appleFont = nullptr;
    ~FontResource() { if (appleFont) CGFontRelease(appleFont); }
    bool Initialize() {
        CFDataRef data = CFDataCreate(kCFAllocatorDefault, bytes->data(), bytes->size());
        CGDataProviderRef provider = data ? CGDataProviderCreateWithCFData(data) : nullptr;
        appleFont = provider ? CGFontCreateWithDataProvider(provider) : nullptr;
        if (provider) CGDataProviderRelease(provider);
        if (data) CFRelease(data);
        return appleFont != nullptr;
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
#ifdef __APPLE__
thread_local std::vector<std::string> appleFamilies;
thread_local bool appleFamiliesInitialized = false;
bool MatchesAppleName(CFStringRef name, CFStringRef attribute)
{
    const void* key = attribute; const void* value = name;
    CFDictionaryRef attributes = CFDictionaryCreate(kCFAllocatorDefault, &key, &value, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CTFontDescriptorRef request = attributes ? CTFontDescriptorCreateWithAttributes(attributes) : nullptr;
    CFSetRef required = CFSetCreate(kCFAllocatorDefault, &key, 1, &kCFTypeSetCallBacks);
    CTFontDescriptorRef match = request && required ? CTFontDescriptorCreateMatchingFontDescriptor(request, required) : nullptr;
    bool found = match != nullptr;
    if (match) CFRelease(match); if (required) CFRelease(required);
    if (request) CFRelease(request); if (attributes) CFRelease(attributes);
    return found;
}

bool MatchesResolvedAppleName(CFStringRef name)
{
    // CoreText accepts case-insensitive family and face names. Mandatory
    // descriptor matching can be stricter, but a substituted font must never
    // make an unknown family look installed.
    CTFontRef font = CTFontCreateWithName(name, 12, nullptr);
    if (!font) return false;
    bool found = false;
    for (CFStringRef resolved : {CTFontCopyFamilyName(font), CTFontCopyPostScriptName(font),
        CTFontCopyFullName(font), CTFontCopyDisplayName(font)}) {
        if (resolved) {
            found |= CFStringCompare(name, resolved, kCFCompareCaseInsensitive) == kCFCompareEqualTo;
            CFRelease(resolved);
        }
    }
    CFRelease(font);
    return found;
}
#endif

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

JALIUM_API int32_t jalium_font_family_is_available(const wchar_t* family)
{
#ifdef __APPLE__
    try {
        uint32_t length = 0;
        if (!family || !jalium::ManagedUtf16LengthBounded(family, jalium::kMaxManagedFontFamilyCodeUnits, &length) || !length) return 0;
        const auto wide = jalium::ManagedToWString(family, length);
        if (Find(wide.c_str())) return 1;
        CFStringRef name = CFStringCreateWithCharacters(kCFAllocatorDefault, reinterpret_cast<const UniChar*>(family), length);
        if (!name) return -1;
        // Keep these aliases consistent with Metal's actual font creation.
        bool found = false;
        for (CFStringRef alias : {CFSTR("SF Pro"), CFSTR(".AppleSystemUIFont"), CFSTR("system-ui"),
            CFSTR("ui-sans-serif"), CFSTR("sans-serif"), CFSTR("serif"), CFSTR("ui-serif"),
            CFSTR("monospace"), CFSTR("ui-monospace")})
            found |= CFStringCompare(name, alias, kCFCompareCaseInsensitive) == kCFCompareEqualTo;
        if (!found) found = MatchesAppleName(name, kCTFontFamilyNameAttribute) || MatchesAppleName(name, kCTFontNameAttribute) ||
            MatchesResolvedAppleName(name);
        CFRelease(name);
        return found ? 1 : 0;
    } catch (...) { return -1; }
#else
    (void)family; return -1;
#endif
}

JALIUM_API int32_t jalium_font_get_system_family_count(void)
{
#ifdef __APPLE__
    try {
        std::vector<std::string> names;
        CFArrayRef families = CTFontManagerCopyAvailableFontFamilyNames();
        if (!families) return 0;
        struct ReleaseArray { CFArrayRef value; ~ReleaseArray() { CFRelease(value); } } release{families};
        for (CFIndex i = 0; i < CFArrayGetCount(families); ++i) {
            auto name = static_cast<CFStringRef>(CFArrayGetValueAtIndex(families, i));
            CFIndex maximum = CFStringGetMaximumSizeForEncoding(CFStringGetLength(name), kCFStringEncodingUTF8) + 1;
            if (maximum <= 1 || maximum > 65536) continue;
            std::vector<char> buffer(static_cast<size_t>(maximum));
            if (CFStringGetCString(name, buffer.data(), maximum, kCFStringEncodingUTF8)) names.emplace_back(buffer.data());
        }
        std::sort(names.begin(), names.end()); names.erase(std::unique(names.begin(), names.end()), names.end());
        appleFamilies = std::move(names); appleFamiliesInitialized = true;
        return static_cast<int32_t>(appleFamilies.size());
    } catch (...) { return 0; }
#else
    return 0;
#endif
}

JALIUM_API int32_t jalium_font_copy_system_family(int32_t index, char* buffer, int32_t capacity)
{
    if (buffer && capacity > 0) buffer[0] = 0;
#ifdef __APPLE__
    try {
        if (!appleFamiliesInitialized) jalium_font_get_system_family_count();
        if (index < 0 || static_cast<size_t>(index) >= appleFamilies.size() || capacity < 0 || (!buffer && capacity)) return 0;
        const auto& name = appleFamilies[index]; int32_t required = static_cast<int32_t>(name.size() + 1);
        if (!buffer && !capacity) return required;
        if (!buffer || capacity < required) return 0;
        std::memcpy(buffer, name.c_str(), required); return required;
    } catch (...) { return 0; }
#else
    (void)index; (void)capacity; return 0;
#endif
}

JALIUM_API void* jalium_font_resource_get_collection(JaliumFontResource* resource)
{
#ifdef _WIN32
    return resource ? resource->font->collection.Get() : nullptr;
#else
    (void)resource; return nullptr;
#endif
}
}
