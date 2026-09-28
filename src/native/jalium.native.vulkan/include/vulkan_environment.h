#pragma once
#include <cstdlib>
#include <cstring>

namespace jalium {
template <size_t Capacity>
inline bool ReadVulkanEnvironment(const char* name, char (&buffer)[Capacity])
{
#ifdef _WIN32
    size_t length = 0;
    return getenv_s(&length, buffer, Capacity, name) == 0 && length > 0;
#else
    const char* value = std::getenv(name);
    if (!value || std::strlen(value) >= Capacity) { buffer[0] = 0; return false; }
    std::memcpy(buffer, value, std::strlen(value) + 1);
    return true;
#endif
}
}
