cmake_minimum_required(VERSION 3.20)
project(AnomalyLuauSandbox LANGUAGES CXX)

if(NOT DEFINED LUAU_SOURCE_DIR)
    message(FATAL_ERROR "Set LUAU_SOURCE_DIR to the verified pinned official Luau checkout")
endif()

set(LUAU_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUAU_BUILD_SHARED OFF CACHE BOOL "" FORCE)
add_subdirectory("${LUAU_SOURCE_DIR}" luau EXCLUDE_FROM_ALL)

# Keep Luau's default C++ exception recovery. No VM exception or lua_error may
# unwind through a reverse P/Invoke; the only managed callback returns a status.
target_compile_definitions(Luau.VM PRIVATE LUA_USE_LONGJMP=0)
add_library(luau_sandbox SHARED luau_sandbox.cpp)
target_compile_features(luau_sandbox PRIVATE cxx_std_17)
target_link_libraries(luau_sandbox PRIVATE Luau.VM Luau.Compiler)
if(MSVC)
    target_compile_options(luau_sandbox PRIVATE /W4 /EHsc)
endif()
