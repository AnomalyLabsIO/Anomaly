#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>

namespace {
constexpr int MaxRegions = 8;
constexpr size_t MaxBytes = 32;
struct Region {
    const unsigned char* address;
    unsigned char bytes[MaxBytes];
    size_t length;
    DWORD protection;
};
SRWLOCK lock = SRWLOCK_INIT;
Region regions[MaxRegions]{};
int count = 0;

bool ReadRegion(const void* address, void* output, size_t length, DWORD* protection) {
    MEMORY_BASIC_INFORMATION page{};
    if (!VirtualQuery(address, &page, sizeof(page)) || page.State != MEM_COMMIT)
        return false;
    auto start = reinterpret_cast<uintptr_t>(address);
    auto end = reinterpret_cast<uintptr_t>(page.BaseAddress) + page.RegionSize;
    if (start > end || length > end - start)
        return false;
    DWORD executable = page.Protect & 0xff;
    if (executable != PAGE_EXECUTE && executable != PAGE_EXECUTE_READ &&
        executable != PAGE_EXECUTE_READWRITE && executable != PAGE_EXECUTE_WRITECOPY)
        return false;
    SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), address, output, length, &read) || read != length)
        return false;
    *protection = page.Protect;
    return true;
}
}

extern "C" __declspec(dllexport) int __cdecl AnomalyGuardAdd(const void* address, size_t length) {
    if (!address || length == 0 || length > MaxBytes) return -1;
    AcquireSRWLockExclusive(&lock);
    if (count == MaxRegions) { ReleaseSRWLockExclusive(&lock); return -1; }
    Region next{};
    next.address = static_cast<const unsigned char*>(address);
    next.length = length;
    if (!ReadRegion(address, next.bytes, length, &next.protection)) {
        ReleaseSRWLockExclusive(&lock);
        return -1;
    }
    int index = count;
    regions[count++] = next;
    ReleaseSRWLockExclusive(&lock);
    return index;
}

// 0: unchanged, 1: changed/unreadable, -1: invalid index.
extern "C" __declspec(dllexport) int __cdecl AnomalyGuardCheck(int index) {
    AcquireSRWLockShared(&lock);
    if (index < 0 || index >= count) { ReleaseSRWLockShared(&lock); return -1; }
    const Region& expected = regions[index];
    unsigned char current[MaxBytes]{};
    DWORD protection = 0;
    bool same = ReadRegion(expected.address, current, expected.length, &protection) &&
        protection == expected.protection && std::memcmp(current, expected.bytes, expected.length) == 0;
    ReleaseSRWLockShared(&lock);
    return same ? 0 : 1;
}
