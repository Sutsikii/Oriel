using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Oriel;

namespace Viewer;

/// <summary>
/// 3D models for the page, as glTF binary files (.glb): bytes, through the
/// binary channel, which three.js reads as they come.
/// </summary>
[Service]
public sealed class Models
{
    /// <summary>
    /// A torus knot made here, of <paramref name="segments"/> rings of
    /// <paramref name="sides"/> vertices: from a few thousand triangles to
    /// over a million.
    /// </summary>
    public ReadOnlyMemory<byte> Knot(int segments = 512, int sides = 64) =>
        TorusKnot(Math.Clamp(segments, 16, 4096), Math.Clamp(sides, 3, 256));

    /// <summary>A .glb file the person picks, or nothing when they pick none.</summary>
    public async Task<ReadOnlyMemory<byte>> Open(OrielWindow window)
    {
        var path = await window.Dialogs.OpenFileAsync(new OpenFileOptions
        {
            Title = "Open a model",
            Filters = [new FileFilter("glTF binary", "*.glb")],
        });
        return path is null ? ReadOnlyMemory<byte>.Empty : await File.ReadAllBytesAsync(path);
    }

    /// <summary>A (2, 3) torus knot with its normals, as a .glb of one mesh.</summary>
    internal static byte[] TorusKnot(int segments, int sides)
    {
        const float Radius = 1f, Tube = 0.3f;
        var vertices = (segments + 1) * (sides + 1);
        var positions = new float[vertices * 3];
        var normals = new float[vertices * 3];
        var indices = new uint[segments * sides * 6];
        var v = 0;
        for (var i = 0; i <= segments; i++)
        {
            var u = (float)i / segments * MathF.PI * 4; // p = 2 turns
            var center = Curve(u);
            var ahead = Curve(u + 0.01f);
            var tangent = Normalize(ahead - center);
            var binormal = Normalize(Cross(tangent, ahead + center));
            var normal = Cross(binormal, tangent);
            for (var j = 0; j <= sides; j++)
            {
                var angle = (float)j / sides * MathF.PI * 2;
                var direction = normal * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                var point = center + direction * Tube;
                (positions[v], positions[v + 1], positions[v + 2]) = (point.X, point.Y, point.Z);
                (normals[v], normals[v + 1], normals[v + 2]) = (direction.X, direction.Y, direction.Z);
                v += 3;
            }
        }
        var k = 0;
        for (var i = 0; i < segments; i++)
        {
            for (var j = 0; j < sides; j++)
            {
                uint a = (uint)(i * (sides + 1) + j), b = (uint)((i + 1) * (sides + 1) + j);
                (indices[k], indices[k + 1], indices[k + 2]) = (a, b, a + 1);
                (indices[k + 3], indices[k + 4], indices[k + 5]) = (b, b + 1, a + 1);
                k += 6;
            }
        }
        return Glb(positions, normals, indices);

        static System.Numerics.Vector3 Curve(float u) =>
            new System.Numerics.Vector3(MathF.Cos(2 * u) * (2 + MathF.Cos(3 * u / 2)), MathF.Sin(2 * u) * (2 + MathF.Cos(3 * u / 2)), MathF.Sin(3 * u / 2)) * (Radius / 2);
        static System.Numerics.Vector3 Normalize(System.Numerics.Vector3 x) => System.Numerics.Vector3.Normalize(x);
        static System.Numerics.Vector3 Cross(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => System.Numerics.Vector3.Cross(a, b);
    }

    /// <summary>A glTF binary file of one mesh: its JSON chunk, then its buffer.</summary>
    private static byte[] Glb(float[] positions, float[] normals, uint[] indices)
    {
        var (min, max) = Bounds(positions);
        int positionBytes = positions.Length * 4, normalBytes = normals.Length * 4, indexBytes = indices.Length * 4;
        using var json = new MemoryStream();
        using (var w = new Utf8JsonWriter(json))
        {
            w.WriteStartObject();
            w.WriteStartObject("asset");
            w.WriteString("version", "2.0");
            w.WriteString("generator", "Oriel 3D Viewer");
            w.WriteEndObject();
            w.WriteNumber("scene", 0);
            Raw(w, "scenes", """[{"nodes":[0]}]""");
            Raw(w, "nodes", """[{"mesh":0,"name":"Torus knot"}]""");
            Raw(w, "meshes", """[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"indices":2,"material":0}]}]""");
            Raw(w, "materials", """[{"pbrMetallicRoughness":{"baseColorFactor":[0.15,0.4,0.95,1],"metallicFactor":0.3,"roughnessFactor":0.35}}]""");
            w.WriteStartArray("accessors");
            Accessor(w, 0, positions.Length / 3, 5126, "VEC3", min, max);
            Accessor(w, 1, normals.Length / 3, 5126, "VEC3", null, null);
            Accessor(w, 2, indices.Length, 5125, "SCALAR", null, null);
            w.WriteEndArray();
            w.WriteStartArray("bufferViews");
            View(w, 0, positionBytes, 34962);
            View(w, positionBytes, normalBytes, 34962);
            View(w, positionBytes + normalBytes, indexBytes, 34963);
            w.WriteEndArray();
            w.WriteStartArray("buffers");
            w.WriteStartObject();
            w.WriteNumber("byteLength", positionBytes + normalBytes + indexBytes);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var jsonChunk = Pad(json.ToArray(), (byte)' ');
        var bin = new byte[positionBytes + normalBytes + indexBytes];
        Buffer.BlockCopy(positions, 0, bin, 0, positionBytes);
        Buffer.BlockCopy(normals, 0, bin, positionBytes, normalBytes);
        Buffer.BlockCopy(indices, 0, bin, positionBytes + normalBytes, indexBytes);
        var binChunk = Pad(bin, 0);

        var glb = new byte[12 + 8 + jsonChunk.Length + 8 + binChunk.Length];
        var span = glb.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67); // "glTF"
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)jsonChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 0x4E4F534A); // "JSON"
        jsonChunk.CopyTo(span[20..]);
        var at = 20 + jsonChunk.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(span[at..], (uint)binChunk.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 4)..], 0x004E4942); // "BIN\0"
        binChunk.CopyTo(span[(at + 8)..]);
        return glb;

        static void Raw(Utf8JsonWriter w, string name, string json)
        {
            w.WritePropertyName(name);
            w.WriteRawValue(json);
        }

        static void Accessor(Utf8JsonWriter w, int view, int count, int componentType, string type, float[]? min, float[]? max)
        {
            w.WriteStartObject();
            w.WriteNumber("bufferView", view);
            w.WriteNumber("componentType", componentType);
            w.WriteNumber("count", count);
            w.WriteString("type", type);
            if (min is not null && max is not null)
            {
                w.WriteStartArray("min");
                foreach (var x in min)
                    w.WriteNumberValue(x);
                w.WriteEndArray();
                w.WriteStartArray("max");
                foreach (var x in max)
                    w.WriteNumberValue(x);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }

        static void View(Utf8JsonWriter w, int offset, int length, int target)
        {
            w.WriteStartObject();
            w.WriteNumber("buffer", 0);
            w.WriteNumber("byteOffset", offset);
            w.WriteNumber("byteLength", length);
            w.WriteNumber("target", target);
            w.WriteEndObject();
        }

        static byte[] Pad(byte[] chunk, byte with)
        {
            var padded = new byte[(chunk.Length + 3) & ~3];
            chunk.CopyTo(padded, 0);
            padded.AsSpan(chunk.Length).Fill(with);
            return padded;
        }
    }

    private static (float[] Min, float[] Max) Bounds(float[] xyz)
    {
        float[] min = [float.MaxValue, float.MaxValue, float.MaxValue], max = [float.MinValue, float.MinValue, float.MinValue];
        for (var i = 0; i < xyz.Length; i++)
        {
            min[i % 3] = Math.Min(min[i % 3], xyz[i]);
            max[i % 3] = Math.Max(max[i % 3], xyz[i]);
        }
        return (min, max);
    }
}
