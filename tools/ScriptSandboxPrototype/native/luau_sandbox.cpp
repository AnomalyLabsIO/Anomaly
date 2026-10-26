#include "lua.h"
#include "lualib.h"
#include "Luau/Compiler.h"

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <limits>
#include <string>

#ifdef _WIN32
#define EXPORT extern "C" __declspec(dllexport)
#define CALL __cdecl
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#define CALL
#endif

// No Lua API is called while managed code is on the stack. Managed failures are
// encoded in the result buffer, then raised here after the callback has returned.
using HostCallback = int(CALL*)(void*, int, const char*, size_t, const char*, size_t,
    double, double, double, char*, size_t, size_t*);

namespace
{
enum Outcome { Completed, ScriptError, CpuLimit, MemoryLimit, SourceRejected, NativeMemoryFailure };
using Clock = std::chrono::steady_clock;

struct Context
{
    HostCallback host = nullptr;
    void* hostData = nullptr;
    size_t current = 0;
    size_t peak = 0;
    size_t memoryLimit = 0;
    bool memoryDenied = false;
    bool nativeMemoryFailed = false;
    bool cpuDenied = false;
    bool running = false;
    uint64_t chargedSteps = 0;
    uint64_t nextSlice = 0;
    int resumes = 0;
    int hostAttempts = 0;
    int maxCommands = 0;
    Clock::time_point deadline;
};

void copyMessage(char* buffer, size_t capacity, const char* message) noexcept
{
    if (!buffer || capacity == 0) return;
    const size_t count = std::min(std::strlen(message), capacity - 1);
    std::memcpy(buffer, message, count);
    buffer[count] = '\0';
}

void* allocator(void* userdata, void* pointer, size_t oldSize, size_t newSize) noexcept
{
    auto& c = *static_cast<Context*>(userdata);
    // For fresh allocations the allocator's old-size argument is not a live block.
    const size_t previous = pointer ? oldSize : 0;
    if (newSize == 0)
    {
        std::free(pointer);
        c.current -= std::min(previous, c.current);
        return nullptr;
    }
    const size_t retained = c.current - std::min(previous, c.current);
    if (newSize > c.memoryLimit || retained > c.memoryLimit - newSize ||
        (c.memoryDenied && newSize > previous))
    {
        c.memoryDenied = true;
        return nullptr;
    }
    void* replacement = std::realloc(pointer, newSize);
    if (!replacement)
    {
        c.nativeMemoryFailed = true;
        return nullptr;
    }
    // A failed realloc retains the old allocation: account only after success.
    c.current = retained + newSize;
    c.peak = std::max(c.peak, c.current);
    return replacement;
}

Context& context(lua_State* L)
{
    return *static_cast<Context*>(lua_callbacks(L)->userdata);
}

void stop(lua_State* L)
{
    // LUA_BREAK suspends the whole resume, including pcall, rather than becoming
    // a catchable Lua error. At a non-yieldable C boundary use a sticky error;
    // the next safe VM step breaks, and the final status cannot become success.
    if (lua_isyieldable(L))
        lua_break(L);
    else
        luaL_error(L, "sandbox execution budget exhausted");
}

void interrupt(lua_State* L, int gc)
{
    auto& c = context(L);
    if (!c.running || gc >= 0) return; // Do not raise from a GC callback.
    if (Clock::now() >= c.deadline) c.cpuDenied = true;
    if (c.cpuDenied || c.memoryDenied || c.nativeMemoryFailed) stop(L);
}

void step(lua_State* L, lua_Debug*)
{
    auto& c = context(L);
    if (++c.chargedSteps >= c.nextSlice || Clock::now() >= c.deadline)
    {
        if (Clock::now() >= c.deadline) c.cpuDenied = true;
        stop(L);
    }
    else if (c.cpuDenied || c.memoryDenied || c.nativeMemoryFailed)
        stop(L);
}

const char* stringArgument(lua_State* L, int index, size_t& size)
{
    // Check types before conversion (lua_tolstring would also accept numbers).
    luaL_checktype(L, index, LUA_TSTRING);
    const char* value = lua_tolstring(L, index, &size);
    // Host API strings are small. Never marshal a potentially multi-MB VM string.
    if (size > 4096) luaL_error(L, "host string exceeds 4096 UTF-8 bytes");
    return value;
}

int callHost(lua_State* L, int operation)
{
    auto& c = context(L);
    // Charge attempts even if argument validation rejects the call.
    if (++c.hostAttempts > c.maxCommands) luaL_error(L, "object command budget reached");
    if (c.cpuDenied || c.memoryDenied || c.nativeMemoryFailed) { stop(L); return 0; }
    const int expected = operation == 2 ? 4 : operation == 3 ? 2 : 1;
    if (lua_gettop(L) != expected) luaL_error(L, "invalid object command argument count");
    size_t firstSize = 0, secondSize = 0;
    const char* first = stringArgument(L, 1, firstSize);
    const char* second = operation == 3 ? stringArgument(L, 2, secondSize) : "";
    double x = 0, y = 0, z = 0;
    if (operation == 2)
    {
        luaL_checktype(L, 2, LUA_TNUMBER);
        luaL_checktype(L, 3, LUA_TNUMBER);
        luaL_checktype(L, 4, LUA_TNUMBER);
        x = lua_tonumber(L, 2); y = lua_tonumber(L, 3); z = lua_tonumber(L, 4);
    }
    char response[1024] = {};
    size_t responseSize = 0;
    const int status = c.host(c.hostData, operation, first, firstSize, second, secondSize,
        x, y, z, response, sizeof(response), &responseSize);
    if (responseSize >= sizeof(response)) luaL_error(L, "invalid host response length");
    response[responseSize] = '\0';
    if (status != 0) luaL_error(L, "%s", response);
    if (operation == 1)
    {
        lua_pushlstring(L, response, responseSize);
        return 1;
    }
    return 0;
}

int spawn(lua_State* L) { return callHost(L, 1); }
int setPosition(lua_State* L) { return callHost(L, 2); }
int setLabel(lua_State* L) { return callHost(L, 3); }
int destroy(lua_State* L) { return callHost(L, 4); }

void openLibrary(lua_State* L, lua_CFunction function, const char* name)
{
    lua_pushcfunction(L, function, name);
    lua_pushstring(L, name);
    lua_call(L, 1, 0);
}

void initialize(lua_State* L)
{
    openLibrary(L, luaopen_base, "");
    openLibrary(L, luaopen_string, "string");
    openLibrary(L, luaopen_table, "table");
    openLibrary(L, luaopen_math, "math");

    // A positive list prevents a new upstream base-library function from being
    // exposed automatically. No OS, debug, require, coroutine, dynamic loader,
    // print, environment manipulation, or arbitrary CLR object access exists.
    lua_newtable(L);
    const char* allowed[] = {"assert", "error", "ipairs", "pairs", "next", "pcall", "xpcall",
        "select", "tonumber", "tostring", "type", "typeof", "rawequal", "rawget", "rawset",
        "rawlen", "getmetatable", "setmetatable", "string", "table", "math", "_VERSION"};
    for (const char* name : allowed)
    {
        lua_getglobal(L, name);
        lua_setfield(L, -2, name);
    }
    lua_replace(L, LUA_GLOBALSINDEX);
    lua_pushvalue(L, LUA_GLOBALSINDEX);
    lua_setglobal(L, "_G");
    lua_newtable(L);
    lua_pushcfunction(L, spawn, "spawn"); lua_setfield(L, -2, "spawn");
    lua_pushcfunction(L, setPosition, "set_position"); lua_setfield(L, -2, "set_position");
    lua_pushcfunction(L, setLabel, "set_label"); lua_setfield(L, -2, "set_label");
    lua_pushcfunction(L, destroy, "destroy"); lua_setfield(L, -2, "destroy");
    lua_setglobal(L, "objects");
    luaL_sandbox(L);
    luaL_sandboxthread(L);
}
}

EXPORT uint32_t CALL al_abi_version() noexcept { return 1; }

EXPORT int CALL al_execute(const char* source, size_t sourceSize, size_t maxSourceSize,
    HostCallback host, void* hostData, uint64_t memoryLimit, uint64_t instructionSlice,
    int maxResumes, int timeoutMs, int maxCommands, uint64_t* peak, int* resumes, char* message, size_t messageCapacity) noexcept
{
    Context c;
    lua_State* L = nullptr;
    int outcome = ScriptError;
    if (peak) *peak = 0;
    if (resumes) *resumes = 0;
    copyMessage(message, messageCapacity, "");
    try
    {
        if ((!source && sourceSize != 0) || sourceSize > maxSourceSize || sourceSize > 65536 ||
            !host || memoryLimit == 0 || memoryLimit > SIZE_MAX || instructionSlice == 0 ||
            instructionSlice > UINT64_MAX / static_cast<uint64_t>(std::max(1, maxResumes)) || maxResumes <= 0 || timeoutMs <= 0 || maxCommands < 0)
        {
            copyMessage(message, messageCapacity, "invalid source size or sandbox limits");
            return SourceRejected;
        }
        // Only source enters the compiler. Server bytecode is never accepted.
        // Compiler allocations are outside the VM allocator; the worker process
        // watchdog/memory ceiling bound this stage separately.
        Luau::CompileOptions options;
        options.optimizationLevel = 0;
        options.debugLevel = 1;
        const std::string bytecode = Luau::compile(std::string(source ? source : "", sourceSize), options);
        c.host = host; c.hostData = hostData; c.memoryLimit = static_cast<size_t>(memoryLimit); c.maxCommands = maxCommands;
        c.deadline = Clock::now() + std::chrono::milliseconds(timeoutMs);
        L = lua_newstate(allocator, &c);
        if (!L)
        {
            outcome = c.memoryDenied ? MemoryLimit : NativeMemoryFailure;
            copyMessage(message, messageCapacity, c.memoryDenied ? "VM allocation limit reached during initialization" :
                "native VM allocator failed below the configured quota");
        }
        else
        {
            lua_callbacks(L)->userdata = &c;
            lua_callbacks(L)->interrupt = interrupt;
            lua_callbacks(L)->debugstep = step;
            initialize(L);
            const int loadStatus = luau_load(L, "server-script", bytecode.data(), bytecode.size(), 0);
            if (loadStatus != 0)
            {
                copyMessage(message, messageCapacity, lua_tostring(L, -1) ? lua_tostring(L, -1) : "script load failed");
                outcome = c.memoryDenied ? MemoryLimit : c.nativeMemoryFailed ? NativeMemoryFailure : ScriptError;
            }
            else
            {
                lua_singlestep(L, 1);
                c.running = true;
                int status = LUA_BREAK;
                while (c.resumes < maxResumes)
                {
                    ++c.resumes;
                    c.nextSlice = c.chargedSteps + instructionSlice;
                    status = lua_resume(L, nullptr, 0);
                    if (status != LUA_BREAK || c.cpuDenied || c.memoryDenied || c.nativeMemoryFailed) break;
                }
                c.running = false;
                if (Clock::now() >= c.deadline) c.cpuDenied = true;
                if (c.memoryDenied)
                {
                    outcome = MemoryLimit;
                    copyMessage(message, messageCapacity, "VM allocation limit reached");
                }
                else if (c.nativeMemoryFailed || status == LUA_ERRMEM)
                {
                    outcome = NativeMemoryFailure;
                    copyMessage(message, messageCapacity, "native VM allocator failed below the configured quota");
                }
                else if (c.cpuDenied || status == LUA_BREAK || status == LUA_YIELD)
                {
                    outcome = CpuLimit;
                    copyMessage(message, messageCapacity, "VM step or elapsed-time budget reached");
                }
                else if (status != 0)
                {
                    outcome = ScriptError;
                    copyMessage(message, messageCapacity, lua_tostring(L, -1) ? lua_tostring(L, -1) : "script failed");
                }
                else
                {
                    outcome = Completed;
                    copyMessage(message, messageCapacity, "completed");
                }
            }
        }
    }
    catch (const std::bad_alloc&)
    {
        outcome = c.memoryDenied ? MemoryLimit : NativeMemoryFailure;
        copyMessage(message, messageCapacity, c.memoryDenied ? "VM allocation limit reached" :
            "native allocation failed outside the VM allocator quota (for example during compilation)");
    }
    catch (const std::exception& error)
    {
        outcome = c.memoryDenied ? MemoryLimit : c.nativeMemoryFailed ? NativeMemoryFailure : c.cpuDenied ? CpuLimit : ScriptError;
        copyMessage(message, messageCapacity, error.what());
    }
    catch (...)
    {
        outcome = c.memoryDenied ? MemoryLimit : c.nativeMemoryFailed ? NativeMemoryFailure : c.cpuDenied ? CpuLimit : ScriptError;
        copyMessage(message, messageCapacity, "native exception contained");
    }
    // Every exception, including cleanup failures, stays inside the native ABI.
    try { if (L) lua_close(L); } catch (...) { }
    if (peak) *peak = static_cast<uint64_t>(c.peak);
    if (resumes) *resumes = c.resumes;
    return outcome;
}
