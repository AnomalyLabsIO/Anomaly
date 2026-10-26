using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Assets;

// A deliberately small JSON data reader. No type activation, reflection, callbacks,
// external references, or serializer extensions are available to a pack index.
internal sealed class BoundedPackJson
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly string _text;
    private readonly int _maxDepth, _maxTokens, _maxArrayItems, _maxObjectProperties, _maxStringLength;
    private int _position;
    private int _tokens;
    private BoundedPackJson(byte[] bytes, int maxDepth, int maxTokens, int maxArrayItems, int maxObjectProperties, int maxStringLength)
    {
        _text = Utf8.GetString(bytes);
        _maxDepth = maxDepth;
        _maxTokens = maxTokens;
        _maxArrayItems = maxArrayItems;
        _maxObjectProperties = maxObjectProperties;
        _maxStringLength = maxStringLength;
    }

    internal static Dictionary<string, object> Read(byte[] bytes) => Read(bytes, 16, 32768, 4096, int.MaxValue, 1024);

    // A caller with a smaller data shape can reject oversized trees while they
    // are being built, before retaining nodes it will subsequently reject.
    internal static Dictionary<string, object> Read(byte[] bytes, int maxDepth, int maxTokens, int maxArrayItems,
        int maxObjectProperties, int maxStringLength)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length > AssetPackIndexCodec.MaxIndexBytes)
            throw new InvalidDataException("Pack index size is outside its limit.");
        var parser = new BoundedPackJson(bytes, maxDepth, maxTokens, maxArrayItems, maxObjectProperties, maxStringLength);
        object value = parser.Value(0);
        parser.White();
        if (parser._position != parser._text.Length || !(value is Dictionary<string, object> result))
            throw new InvalidDataException("A pack index must contain exactly one JSON object.");
        return result;
    }

    private object Value(int depth)
    {
        if (depth > _maxDepth || ++_tokens > _maxTokens) throw Bad("JSON nesting or item count exceeded its limit");
        White();
        if (_position >= _text.Length) throw Bad("Truncated value");
        char next = _text[_position];
        if (next == '"') return String();
        if (next == '{')
        {
            _position++;
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            White();
            if (Take('}')) return map;
            do
            {
                if (map.Count >= _maxObjectProperties) throw Bad("JSON object property count exceeded its limit");
                White();
                string key = String();
                if (map.ContainsKey(key)) throw Bad("Duplicate JSON property");
                White(); Need(':');
                map.Add(key, Value(depth + 1));
                White();
                if (Take('}')) return map;
                Need(',');
            } while (true);
        }
        if (next == '[')
        {
            _position++;
            var list = new List<object>();
            White();
            if (Take(']')) return list;
            do
            {
                if (list.Count >= _maxArrayItems) throw Bad("JSON array exceeded its limit");
                list.Add(Value(depth + 1));
                White();
                if (Take(']')) return list;
                Need(',');
            } while (true);
        }
        if (next == 'n') { Literal("null"); return null; }
        if (next == 't') { Literal("true"); return true; }
        if (next == 'f') { Literal("false"); return false; }
        int start = _position;
        Take('-');
        if (!Take('0')) Digits();
        if (Take('.')) Digits();
        if (Take('e') || Take('E')) { if (!Take('+')) Take('-'); Digits(); }
        int length = _position - start;
        if (length == 0 || length > 64 || !double.TryParse(_text.Substring(start, length),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
            double.IsNaN(number) || double.IsInfinity(number)) throw Bad("Invalid JSON number");
        return number;
    }

    private string String()
    {
        Need('"');
        var result = new StringBuilder();
        while (_position < _text.Length)
        {
            char character = _text[_position++];
            if (character == '"')
            {
                string value = result.ToString();
                try { Utf8.GetByteCount(value); }
                catch (EncoderFallbackException) { throw Bad("Invalid Unicode string"); }
                return value;
            }
            if (character < 32) throw Bad("Unescaped control character");
            if (character == '\\')
            {
                if (_position >= _text.Length) throw Bad("Truncated escape");
                switch (_text[_position++])
                {
                    case '"': character = '"'; break;
                    case '\\': character = '\\'; break;
                    case '/': character = '/'; break;
                    case 'b': character = '\b'; break;
                    case 'f': character = '\f'; break;
                    case 'n': character = '\n'; break;
                    case 'r': character = '\r'; break;
                    case 't': character = '\t'; break;
                    case 'u':
                        if (_position > _text.Length - 4 || !ushort.TryParse(_text.Substring(_position, 4),
                            NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Bad("Invalid Unicode escape");
                        _position += 4;
                        character = (char)code;
                        break;
                    default: throw Bad("Invalid string escape");
                }
            }
            if (result.Length >= _maxStringLength) throw Bad("JSON string exceeded its limit");
            result.Append(character);
        }
        throw Bad("Unterminated string");
    }

    private void Digits()
    {
        int start = _position;
        while (_position < _text.Length && _text[_position] >= '0' && _text[_position] <= '9') _position++;
        if (_position == start) throw Bad("Expected digit");
    }
    private void Literal(string value)
    {
        foreach (char character in value) Need(character);
    }
    private void White()
    {
        while (_position < _text.Length && (_text[_position] == ' ' || _text[_position] == '\r' ||
               _text[_position] == '\n' || _text[_position] == '\t')) _position++;
    }
    private bool Take(char character)
    {
        if (_position >= _text.Length || _text[_position] != character) return false;
        _position++;
        return true;
    }
    private void Need(char character) { if (!Take(character)) throw Bad("Unexpected JSON token"); }
    private InvalidDataException Bad(string reason) => new InvalidDataException(reason + " at character " + _position + ".");
}
