using System.Collections.ObjectModel;

namespace Anomaly.Client.Scripting.Catalog;

/// <summary>Editor declarations for the standard-library subset installed by <see cref="ScriptSession"/>.</summary>
internal static class LuaSandboxDefinitions
{
    // The builtin group supplies primitive types, not runtime globals. Every other LuaLS
    // library is replaced here so its ordinary Lua functions cannot widen the sandbox.
    internal static IReadOnlyDictionary<string, string> Builtins { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["basic"] = "disable",
            ["bit"] = "disable",
            ["bit32"] = "disable",
            ["builtin"] = "enable",
            ["coroutine"] = "disable",
            ["debug"] = "disable",
            ["ffi"] = "disable",
            ["io"] = "disable",
            ["jit"] = "disable",
            ["jit.profile"] = "disable",
            ["jit.util"] = "disable",
            ["math"] = "disable",
            ["os"] = "disable",
            ["package"] = "disable",
            ["string"] = "disable",
            ["string.buffer"] = "disable",
            ["table"] = "disable",
            ["table.clear"] = "disable",
            ["table.new"] = "disable",
            ["utf8"] = "disable"
        });

    internal const string Text = """

-- Standard-library subset installed by the Anomaly Lua sandbox.
-- Use the generated runtime.builtin settings with these references.
---@class _G
_G = {}
---@type string
_VERSION = ""

---Returns its arguments when value is truthy; otherwise raises a Lua error.
---@generic T
---@param value T
---@param message? any
---@param ... any
---@return T
---@return any ...
function assert(value, message, ...) end

---@param message string|number
---@param level? integer
function error(message, level) end

---@generic V
---@param list V[]
---@return fun(list: V[], index: integer): integer, V
---@return V[] list
---@return integer index
function ipairs(list) end

---@generic K, V
---@param value table<K, V>
---@param index? K
---@return K|nil index
---@return V|nil value
function next(value, index) end

---@generic K, V
---@param value table<K, V>
---@return fun(value: table<K, V>, index?: K): K, V
---@return table<K, V> value
function pairs(value) end

---@param callback function
---@param ... any
---@return boolean success
---@return any ... resultsOrError
function pcall(callback, ...) end

---@overload fun(index: "#", ...: any): integer
---@param index integer
---@param ... any
---@return any ...
function select(index, ...) end

---@param value any
---@param base? integer
---@return number|nil
function tonumber(value, base) end

---@param value any
---@return string
function tostring(value) end

---@param value any
---@return "nil"|"number"|"string"|"boolean"|"table"|"function"|"thread"|"userdata"
function type(value) end

---@param callback function
---@param handler? fun(error: any): any
---@param ... any
---@return boolean success
---@return any ... resultsOrError
function xpcall(callback, handler, ...) end

---@class mathlib
---@field pi number
---@field huge number # Largest finite double in this runtime.
math = {}

---@param value number
---@return number
function math.abs(value) end
---@param value number
---@return number
function math.acos(value) end
---@param value number
---@return number
function math.asin(value) end
---@param value number
---@return number
function math.atan(value) end
---@param y number
---@param x number
---@return number
function math.atan2(y, x) end
---@param value number
---@return integer
function math.ceil(value) end
---@param value number
---@return number
function math.cos(value) end
---@param value number
---@return number
function math.cosh(value) end
---@param value number
---@return number
function math.deg(value) end
---@param value number
---@return number
function math.exp(value) end
---@param value number
---@return integer
function math.floor(value) end
---@param value number
---@param divisor number
---@return number
function math.fmod(value, divisor) end
---@param value number
---@return number mantissa
---@return integer exponent
function math.frexp(value) end
---@param mantissa number
---@param exponent number
---@return number
function math.ldexp(mantissa, exponent) end
---@param value number
---@param base? number
---@return number
function math.log(value, base) end
---@param value number
---@param ... number
---@return number
function math.max(value, ...) end
---@param value number
---@param ... number
---@return number
function math.min(value, ...) end
---@param value number
---@return integer integral
---@return number fractional
function math.modf(value) end
---@param value number
---@param exponent number
---@return number
function math.pow(value, exponent) end
---@param value number
---@return number
function math.rad(value) end
---@overload fun(upper: integer): integer
---@overload fun(lower: integer, upper: integer): integer
---@return number
function math.random() end
---@param seed number
function math.randomseed(seed) end
---@param value number
---@return number
function math.sin(value) end
---@param value number
---@return number
function math.sinh(value) end
---@param value number
---@return number
function math.sqrt(value) end
---@param value number
---@return number
function math.tan(value) end
---@param value number
---@return number
function math.tanh(value) end

---@class stringlib
string = {}

---@param value string|number
---@param first? integer
---@param last? integer
---@return integer ...
function string.byte(value, first, last) end
---@param ... integer
---@return string
function string.char(...) end
---@param value? string|number
---@param part? string|number
---@return boolean
function string.contains(value, part) end
---@param value? string|number
---@param suffix? string|number
---@return boolean
function string.endsWith(value, suffix) end
---@param value string|number
---@param pattern string|number
---@param first? integer
---@param plain? boolean
---@return integer|nil first
---@return integer|nil last
---@return any ... captures
function string.find(value, pattern, first, plain) end
---@param format string|number
---@param ... any
---@return string
function string.format(format, ...) end
---@param value string|number
---@param pattern string|number
---@return fun(): any, ...
function string.gmatch(value, pattern) end
---@param value string|number
---@param pattern string|number
---@param replacement string|number|table|function
---@param limit? integer
---@return string
---@return integer count
function string.gsub(value, pattern, replacement, limit) end
---@param value string|number
---@return integer
function string.len(value) end
---@param value string|number
---@return string
function string.lower(value) end
---@param value string|number
---@param pattern string|number
---@param first? integer
---@return any ... captures
function string.match(value, pattern, first) end
---@param value string|number
---@param count number
---@param separator? string|number
---@return string
function string.rep(value, count, separator) end
---@param value string|number
---@return string
function string.reverse(value) end
---@param value? string|number
---@param prefix? string|number
---@return boolean
function string.startsWith(value, prefix) end
---@param value string|number
---@param first? integer
---@param last? integer
---@return string
function string.sub(value, first, last) end
---Returns UTF-16 code units from the selected string range.
---@param value string|number
---@param first? integer
---@param last? integer
---@return integer ...
function string.unicode(value, first, last) end
---@param value string|number
---@return string
function string.upper(value) end

---@class tablelib
table = {}

---@param list (string|number)[]
---@param separator? string|number
---@param first? integer
---@param last? integer
---@return string
function table.concat(list, separator, first, last) end
---@generic T
---@overload fun(list: T[], value: T): T[]
---@param list T[]
---@param position integer
---@param value T
---@return T[]
function table.insert(list, position, value) end
---@param ... any
---@return table # Array of arguments with an n field containing their count.
function table.pack(...) end
---@generic T
---@param list T[]
---@param position? integer
---@return T|nil
function table.remove(list, position) end
---@generic T
---@param list T[]
---@param compare? fun(left: T, right: T): boolean
---@return T[]
function table.sort(list, compare) end
---@generic T
---@param list T[]
---@param first? integer
---@param last? integer
---@return T ...
function table.unpack(list, first, last) end

""";
}
