using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Anomaly.Shared.Scripting;

/// <summary>Immutable data-only record. Wire data never selects or constructs a CLR type.</summary>
public sealed class ScriptMessagePayload
{
    public const int MaxEncodedBytes = ScriptMessageLimits.MaxPayloadBytes;
    public const int MaxStringBytes = ScriptMessageLimits.MaxStringBytes;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly byte[] _encoded;
    public IReadOnlyDictionary<string, object> Values { get; }
    public int EncodedBytes => _encoded.Length;

    private ScriptMessagePayload(Dictionary<string, object> values, byte[] encoded)
    {
        Values = new ReadOnlyDictionary<string, object>(values);
        _encoded = encoded;
    }

    public static ScriptMessagePayload FromValues(IReadOnlyDictionary<string, object> values)
    {
        if (values == null || values.Count > ScriptMessageSchema.MaxFields)
            throw new InvalidDataException("Script message record exceeds its field limit.");
        var copy = new Dictionary<string, object>(StringComparer.Ordinal);
        int size = 1;
        foreach (var pair in values)
        {
            if (copy.Count >= ScriptMessageSchema.MaxFields || !ScriptMessageSchema.IsName(pair.Key) || copy.ContainsKey(pair.Key))
                throw new InvalidDataException("Invalid script message field name.");
            object value = Normalize(pair.Value);
            int valueSize = value is bool ? 1 : value is int || value is uint ? 4 : value is double ? 8 : 2 + StringBytes((string)value);
            size = checked(size + 1 + pair.Key.Length + 1 + valueSize);
            if (size > MaxEncodedBytes) throw new InvalidDataException("Script message record exceeds its byte limit.");
            copy.Add(pair.Key, value);
        }
        using (var stream = new MemoryStream(size))
        using (var writer = new BinaryWriter(stream, Utf8))
        {
            writer.Write((byte)copy.Count);
            foreach (var pair in copy.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                WriteText(writer, pair.Key, false);
                if (pair.Value is bool boolean) { writer.Write((byte)1); writer.Write(boolean); }
                else if (pair.Value is int integer) { writer.Write((byte)2); writer.Write(integer); }
                else if (pair.Value is uint unsigned) { writer.Write((byte)3); writer.Write(unsigned); }
                else if (pair.Value is double number) { writer.Write((byte)4); writer.Write(number); }
                else { writer.Write((byte)5); WriteText(writer, (string)pair.Value, true); }
            }
            writer.Flush();
            return new ScriptMessagePayload(copy, stream.ToArray());
        }
    }

    /// <summary>Reads trusted application DTO members once; only primitive values are retained.</summary>
    public static ScriptMessagePayload Create<T>(T data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var members = Members(typeof(T));
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (MemberInfo member in members)
            values.Add(member.Name, member is FieldInfo field ? field.GetValue(data) : ((PropertyInfo)member).GetValue(data, null));
        return FromValues(values);
    }

    /// <summary>Maps only an exact primitive record to a caller-selected trusted DTO.</summary>
    public bool TryRead<T>(out T data) where T : new()
    {
        data = default;
        try
        {
            MemberInfo[] members = Members(typeof(T));
            if (members.Length != Values.Count) return false;
            foreach (MemberInfo member in members)
            {
                Type type = MemberType(member);
                if (!Values.TryGetValue(member.Name, out object value) ||
                    (type == typeof(float) ? !(value is double number) || number < -float.MaxValue || number > float.MaxValue : value.GetType() != type)) return false;
            }
            object result = new T();
            foreach (MemberInfo member in members)
            {
                object value = Values[member.Name];
                if (MemberType(member) == typeof(float)) value = (float)(double)value;
                if (member is FieldInfo field) field.SetValue(result, value); else ((PropertyInfo)member).SetValue(result, value, null);
            }
            data = (T)result;
            return true;
        }
        catch (Exception exception) when (!IsFatal(exception)) { return false; }
    }

    internal void Write(BinaryWriter writer) => writer.Write(_encoded);

    internal static ScriptMessagePayload Read(BinaryReader reader, int length)
    {
        if (length < 1 || length > MaxEncodedBytes) throw new InvalidDataException("Invalid script payload length.");
        byte[] encoded = reader.ReadBytes(length);
        if (encoded.Length != length) throw new EndOfStreamException();
        using (var stream = new MemoryStream(encoded, false))
        using (var input = new BinaryReader(stream, Utf8))
        {
            int count = input.ReadByte();
            if (count > ScriptMessageSchema.MaxFields) throw new InvalidDataException("Too many script message fields.");
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            string previous = null;
            for (int i = 0; i < count; i++)
            {
                string name = ReadText(input, false);
                if (!ScriptMessageSchema.IsName(name) || (previous != null && string.CompareOrdinal(previous, name) >= 0))
                    throw new InvalidDataException("Script message fields must have unique canonical names and order.");
                previous = name;
                object value;
                switch (input.ReadByte())
                {
                    case 1:
                        byte boolean = input.ReadByte();
                        if (boolean > 1) throw new InvalidDataException("Invalid script message boolean.");
                        value = boolean == 1; break;
                    case 2: value = input.ReadInt32(); break;
                    case 3: value = input.ReadUInt32(); break;
                    case 4: value = input.ReadDouble(); break;
                    case 5: value = ReadText(input, true); break;
                    default: throw new InvalidDataException("Unsupported script message value type.");
                }
                values.Add(name, Normalize(value));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing script message payload bytes.");
            return new ScriptMessagePayload(values, encoded);
        }
    }

    internal static int StringBytes(string text)
    {
        if (text == null || text.Length > MaxStringBytes || text.IndexOf('\0') >= 0)
            throw new InvalidDataException("Invalid script message string.");
        try
        {
            int count = Utf8.GetByteCount(text);
            if (count > MaxStringBytes) throw new InvalidDataException("Script message string exceeds its byte limit.");
            return count;
        }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Script message string is not Unicode scalar text.", exception); }
    }

    private static object Normalize(object value)
    {
        if (value is float single) value = (double)single;
        if (value is double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException("Script message number must be finite.");
        }
        else if (value is string text) StringBytes(text);
        else if (!(value is bool) && !(value is int) && !(value is uint))
            throw new InvalidDataException("Script message fields must be primitive nonnull values.");
        return value;
    }

    private static MemberInfo[] Members(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type.IsArray || type.IsInterface || type.IsAbstract || type.IsEnum)
            throw new InvalidDataException("Script messages require a plain DTO.");
        var result = new List<MemberInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral) throw new InvalidDataException("Script DTO fields must be writable.");
            result.Add(field);
        }
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null || property.GetSetMethod() == null)
                throw new InvalidDataException("Script DTO properties must have public getters and setters.");
            result.Add(property);
        }
        if (result.Count > ScriptMessageSchema.MaxFields) throw new InvalidDataException("Script DTO exceeds its field limit.");
        foreach (MemberInfo member in result)
        {
            Type memberType = MemberType(member);
            if (!ScriptMessageSchema.IsName(member.Name) || !names.Add(member.Name) ||
                (memberType != typeof(bool) && memberType != typeof(int) && memberType != typeof(uint) &&
                 memberType != typeof(double) && memberType != typeof(float) && memberType != typeof(string)))
                throw new InvalidDataException("Script DTO must contain only named primitive members.");
        }
        return result.ToArray();
    }

    private static Type MemberType(MemberInfo member) => member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
    private static bool IsFatal(Exception exception)
    {
        for (Exception current = exception; current != null; current = current.InnerException)
            if (current is OutOfMemoryException || current is StackOverflowException) return true;
        return false;
    }
    private static void WriteText(BinaryWriter writer, string value, bool wide)
    {
        byte[] bytes = Utf8.GetBytes(value);
        if (wide) writer.Write((ushort)bytes.Length); else writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }
    private static string ReadText(BinaryReader reader, bool wide)
    {
        int length = wide ? reader.ReadUInt16() : reader.ReadByte();
        int maximum = wide ? MaxStringBytes : ScriptMessageSchema.MaxNameLength;
        if (length > maximum || (!wide && length == 0)) throw new InvalidDataException("Invalid script message text length.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        string value;
        try { value = Utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Script message text is not UTF-8.", exception); }
        if (wide) StringBytes(value);
        return value;
    }
}
