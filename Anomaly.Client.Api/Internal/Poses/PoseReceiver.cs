using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Anomaly.Shared.Poses;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Engine-free receiver. Bind is called only after the native role receipt has been verified.</summary>
internal sealed class PoseReceiver
{
    private sealed class Sample
    {
        internal ulong Sequence;
        internal double Time;
        internal PoseFrame Frame;
    }
    private sealed class Assembly
    {
        internal double ReceivedAt;
        internal double ServerTime;
        internal List<PoseBone>[] Parts;
    }
    private sealed class Stream
    {
        internal PoseBinding Binding;
        internal ulong Generation;
        internal bool Active;
        internal double ClockOffset;
        internal readonly List<Sample> Samples = new();
        internal readonly Dictionary<ulong, Assembly> Pending = new();
    }

    private const int MaxSubjects = 512;
    private readonly Dictionary<uint, Stream> _streams = new();
    internal uint RoundEpoch { get; private set; }
    internal int PendingAssemblyCount => _streams.Values.Sum(s => s.Pending.Count);

    internal void Reset(uint epoch) { _streams.Clear(); RoundEpoch = epoch; }

    internal bool Bind(PoseBinding binding)
    {
        if (RoundEpoch == 0 || binding == null || !PoseRigCatalog.TryGet(binding.RigId, binding.SchemaVersion, out _)) return false;
        binding.Validate();
        if (_streams.TryGetValue(binding.NetId, out var stream))
        {
            if (stream.Binding.Matches(binding)) return true;
            if (binding.ModelGeneration < stream.Binding.ModelGeneration || binding.BindingRevision <= stream.Binding.BindingRevision) return false;
        }
        else if (_streams.Count >= MaxSubjects) return false;
        _streams[binding.NetId] = new Stream { Binding = binding.Clone() };
        return true;
    }

    internal void Invalidate(uint netId)
    {
        if (_streams.TryGetValue(netId, out var stream))
        {
            stream.Active = false; stream.Samples.Clear(); stream.Pending.Clear();
        }
    }

    // Only after native hub destruction has removed its receipt. Subsequent samples and
    // controls cannot create a stream; a new native receipt must authorize another Bind.
    internal void Forget(uint netId) => _streams.Remove(netId);

    internal bool Apply(PoseControl control, double receivedTime)
    {
        if (control == null || !PoseWireCodec.ValidTime(receivedTime)) return false;
        // Bind/ResetAll require the runtime's ordered receipt and epoch checks, not a wire control alone.
        if (control.Operation != PoseControlOperation.Activate && control.Operation != PoseControlOperation.Clear) return false;
        if (control.Binding == null || !_streams.TryGetValue(control.Binding.NetId, out var stream) || !stream.Binding.Matches(control.Binding)) return false;
        if (control.OverrideGeneration == 0 || control.OverrideGeneration < stream.Generation) return false;
        if (control.Operation == PoseControlOperation.Clear)
        {
            stream.Generation = control.OverrideGeneration; stream.Active = false;
            stream.Samples.Clear(); stream.Pending.Clear(); return true;
        }
        if (control.OverrideGeneration == stream.Generation && !stream.Active || control.Sequence == 0 ||
            !PoseWireCodec.ValidTime(control.ServerTime) || !ValidFrame(stream.Binding, control.Frame)) return false;
        if (control.OverrideGeneration == stream.Generation && stream.Samples.Count > 0 &&
            control.Sequence <= stream.Samples[stream.Samples.Count - 1].Sequence) return false;
        stream.Generation = control.OverrideGeneration; stream.Active = true;
        stream.ClockOffset = receivedTime - control.ServerTime;
        stream.Pending.Clear(); stream.Samples.Clear();
        stream.Samples.Add(new Sample { Sequence = control.Sequence, Time = control.ServerTime, Frame = control.Frame.Clone() });
        return true;
    }

    internal bool Apply(PoseSampleChunk chunk, double receivedTime)
    {
        if (chunk == null || !PoseWireCodec.ValidTime(receivedTime) || !_streams.TryGetValue(chunk.NetId, out var stream) || !stream.Active ||
            stream.Binding.ModelGeneration != chunk.ModelGeneration || stream.Binding.BindingRevision != chunk.BindingRevision ||
            stream.Generation != chunk.OverrideGeneration) return false;
        try { PoseWireCodec.ValidateChunk(chunk); } catch (ArgumentException) { return false; } catch (System.IO.InvalidDataException) { return false; }
        var latest = stream.Samples[stream.Samples.Count - 1];
        if (chunk.Sequence <= latest.Sequence || chunk.ServerTime < latest.Time || receivedTime - stream.ClockOffset - chunk.ServerTime > 1) return false;
        ExpireAssemblies(stream, receivedTime);
        if (!stream.Pending.TryGetValue(chunk.Sequence, out var assembly))
        {
            if (stream.Pending.Count >= 2)
            {
                ulong oldest = stream.Pending.Keys.Min();
                if (chunk.Sequence < oldest) return false;
                stream.Pending.Remove(oldest);
            }
            assembly = new Assembly { ReceivedAt = receivedTime, ServerTime = chunk.ServerTime, Parts = new List<PoseBone>[chunk.ChunkCount] };
            stream.Pending.Add(chunk.Sequence, assembly);
        }
        if (assembly.Parts.Length != chunk.ChunkCount || assembly.ServerTime != chunk.ServerTime || assembly.Parts[chunk.ChunkIndex] != null) return false;
        if (assembly.Parts.Sum(p => p?.Count ?? 0) + chunk.Bones.Count > PoseFrame.MaxBones)
        { stream.Pending.Remove(chunk.Sequence); return false; }
        assembly.Parts[chunk.ChunkIndex] = new List<PoseBone>(chunk.Bones);
        if (assembly.Parts.Any(p => p == null)) return false;
        stream.Pending.Remove(chunk.Sequence);
        PoseFrame frame;
        var combined = new List<PoseBone>();
        foreach (var part in assembly.Parts) combined.AddRange(part);
        try { frame = new PoseFrame(stream.Binding.RigId, stream.Binding.SchemaVersion, combined); }
        catch (ArgumentException) { return false; }
        if (!ValidFrame(stream.Binding, frame) || !SameShape(latest.Frame, frame)) return false;
        // Minimum observed transit offset maps server time without turning late packets into fresh samples.
        stream.ClockOffset = Math.Min(stream.ClockOffset, receivedTime - chunk.ServerTime);
        stream.Samples.Add(new Sample { Sequence = chunk.Sequence, Time = chunk.ServerTime, Frame = frame });
        while (stream.Samples.Count > 8) stream.Samples.RemoveAt(0);
        foreach (ulong stale in stream.Pending.Keys.Where(s => s <= chunk.Sequence).ToArray()) stream.Pending.Remove(stale);
        return true;
    }

    // Combat authorization expires instead of fading hitboxes towards animation. No visual stream can call this receiver.
    internal bool TryGetForCombat(uint netId, double now, out PoseFrame frame, out PoseBinding binding)
    {
        frame = null; binding = null;
        if (!_streams.TryGetValue(netId, out var stream) || !stream.Active || stream.Samples.Count == 0 ||
            now - stream.ClockOffset - stream.Samples[stream.Samples.Count - 1].Time > 0.5) return false;
        return TryGet(netId, now, out frame, out binding);
    }

    internal bool TryGet(uint netId, double now, out PoseFrame frame, out PoseBinding binding)
    {
        frame = null; binding = null;
        if (!PoseWireCodec.ValidTime(now) || !_streams.TryGetValue(netId, out var stream) || !stream.Active || stream.Samples.Count == 0) return false;
        ExpireAssemblies(stream, now);
        var latest = stream.Samples[stream.Samples.Count - 1];
        double age = Math.Max(0, now - stream.ClockOffset - latest.Time);
        float fade = (float)Math.Max(0, Math.Min(1, 1 - (age - 0.5) / 0.15));
        if (fade == 0) return false;
        double target = now - stream.ClockOffset - 0.1;
        Sample before = stream.Samples[0], after = before;
        foreach (var sample in stream.Samples)
        {
            after = sample;
            if (sample.Time >= target) break;
            before = sample;
        }
        float t = after.Time > before.Time ? (float)Math.Max(0, Math.Min(1, (target - before.Time) / (after.Time - before.Time))) : 0;
        var bones = new PoseBone[before.Frame.Bones.Count];
        for (int i = 0; i < bones.Length; i++)
        {
            var a = before.Frame.Bones[i]; var b = after.Frame.Bones[i];
            Quaternion q = Quaternion.Slerp(new Quaternion(a.Qx, a.Qy, a.Qz, a.Qw), new Quaternion(b.Qx, b.Qy, b.Qz, b.Qw), t);
            bones[i] = new PoseBone(a.BoneId, a.Components, Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t),
                q.X, q.Y, q.Z, q.W, Lerp(a.Weight, b.Weight, t) * fade, a.IncludeLocalPlayer);
        }
        frame = new PoseFrame(stream.Binding.RigId, stream.Binding.SchemaVersion, bones); binding = stream.Binding.Clone();
        return bones.Length > 0;
    }

    private static bool ValidFrame(PoseBinding binding, PoseFrame frame) => frame != null && frame.RigId == binding.RigId &&
        frame.SchemaVersion == binding.SchemaVersion && PoseRigCatalog.TryGet(binding.RigId, binding.SchemaVersion, out var rig) &&
        frame.Bones.All(b => rig.Bones.Any(d => d.Id == b.BoneId));
    private static bool SameShape(PoseFrame a, PoseFrame b)
    {
        if (a.Bones.Count != b.Bones.Count) return false;
        for (int i = 0; i < a.Bones.Count; i++)
            if (a.Bones[i].BoneId != b.Bones[i].BoneId || a.Bones[i].Components != b.Bones[i].Components || a.Bones[i].IncludeLocalPlayer != b.Bones[i].IncludeLocalPlayer) return false;
        return true;
    }
    private static void ExpireAssemblies(Stream stream, double now)
    {
        foreach (ulong sequence in stream.Pending.Where(p => now - p.Value.ReceivedAt >= 0.25).Select(p => p.Key).ToArray()) stream.Pending.Remove(sequence);
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
