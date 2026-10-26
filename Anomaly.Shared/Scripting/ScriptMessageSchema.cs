using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Networking;

namespace Anomaly.Shared.Scripting;

/// <summary>A bounded flat record declared in the exact approved pack index.</summary>
public sealed class ScriptMessageSchema
{
    public const int MaxSchemas = ScriptMessageLimits.MaxSchemasPerPack;
    public const int MaxFields = ScriptMessageLimits.MaxFields;
    public const int MaxNameLength = 64;
    public string Name = string.Empty;
    public AnomalyMessageDirection Direction;
    public ushort Version = 1;
    public List<ScriptMessageField> Fields = new List<ScriptMessageField>();

    public void Validate()
    {
        if (!IsName(Name) || Version != 1 ||
            (Direction != AnomalyMessageDirection.ClientToServer && Direction != AnomalyMessageDirection.ServerToClient) ||
            Fields == null || Fields.Count > MaxFields)
            throw new InvalidDataException("Invalid script message schema.");
        // At most 32 fields: a bounded scan avoids allocating a HashSet on every
        // payload authorization and on every VM's repeated contract validation.
        for (int index = 0; index < Fields.Count; index++)
        {
            ScriptMessageField field = Fields[index];
            if (field == null || !IsName(field.Name))
                throw new InvalidDataException("Invalid or duplicate script message field.");
            for (int previous = 0; previous < index; previous++)
                if (Fields[previous].Name == field.Name)
                    throw new InvalidDataException("Invalid or duplicate script message field.");
            switch (field.Type)
            {
                case "Boolean": case "Int32": case "UInt32": case "Number":
                    if (field.MaxBytes != 0) throw new InvalidDataException("Only strings declare MaxBytes.");
                    break;
                case "String":
                    if (field.MaxBytes < 1 || field.MaxBytes > ScriptMessagePayload.MaxStringBytes)
                        throw new InvalidDataException("Script message string limit is invalid.");
                    break;
                default: throw new InvalidDataException("Unsupported script message field type.");
            }
        }
    }

    /// <summary>Returns a validated independent copy for retaining an approved contract.</summary>
    public ScriptMessageSchema Clone()
    {
        Validate();
        var clone = new ScriptMessageSchema { Name = Name, Direction = Direction, Version = Version,
            Fields = new List<ScriptMessageField>(Fields.Count) };
        foreach (ScriptMessageField field in Fields)
            clone.Fields.Add(new ScriptMessageField { Name = field.Name, Type = field.Type, MaxBytes = field.MaxBytes });
        return clone;
    }

    public void ValidatePayload(ScriptMessagePayload payload)
    {
        Validate();
        if (payload == null || payload.Values.Count != Fields.Count)
            throw new InvalidDataException("Script message fields do not match their schema.");
        foreach (ScriptMessageField field in Fields)
        {
            if (!payload.Values.TryGetValue(field.Name, out object value) ||
                (field.Type == "Boolean" ? !(value is bool) :
                 field.Type == "Int32" ? !(value is int) :
                 field.Type == "UInt32" ? !(value is uint) :
                 field.Type == "Number" ? !(value is double) : !(value is string)))
                throw new InvalidDataException("Script message field type does not match its schema.");
            if (value is string text && ScriptMessagePayload.StringBytes(text) > field.MaxBytes)
                throw new InvalidDataException("Script message string exceeds its schema limit.");
        }
    }

    public static bool IsName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxNameLength || value[0] < 'A' || value[0] > 'Z') return false;
        foreach (char c in value)
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_') return false;
        return true;
    }
}
