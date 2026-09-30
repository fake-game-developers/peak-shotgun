using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PeakShotgun;

internal static class ObjMeshLoader
{
    internal static Mesh? Load(Stream stream, string meshName)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var outPositions = new List<Vector3>();
        var outUvs = new List<Vector2>();
        var outNormals = new List<Vector3>();
        var triangles = new List<int>();
        var vertexKey = new Dictionary<(int, int, int), int>();

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    // FBX/Assimp is right-handed; Unity is left-handed — flip X.
                    positions.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "vt" when parts.Length >= 3:
                    // Assimp/Unity both use V=0 at the bottom — do not flip.
                    uvs.Add(new Vector2(Parse(parts[1]), Parse(parts[2])));
                    break;
                case "vn" when parts.Length >= 4:
                    normals.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "f" when parts.Length >= 4:
                    int first = AddFaceVertex(parts[1], positions, uvs, normals, outPositions, outUvs, outNormals, vertexKey);
                    int prev = AddFaceVertex(parts[2], positions, uvs, normals, outPositions, outUvs, outNormals, vertexKey);
                    for (int i = 3; i < parts.Length; i++)
                    {
                        int next = AddFaceVertex(parts[i], positions, uvs, normals, outPositions, outUvs, outNormals, vertexKey);
                        // Reverse winding to match the X flip.
                        triangles.Add(first);
                        triangles.Add(next);
                        triangles.Add(prev);
                        prev = next;
                    }

                    break;
            }
        }

        if (outPositions.Count == 0 || triangles.Count == 0)
        {
            return null;
        }

        // Center on the AABB (Unity / PEAK bounds.center), not the vertex centroid — the mesh is
        // asymmetric along the barrel, so a centroid pivot leaves the rendered bounds off-origin
        // and luggage floats or clips when PEAK centers on mainRenderer.bounds.
        Vector3 min = outPositions[0];
        Vector3 max = outPositions[0];
        for (int i = 1; i < outPositions.Count; i++)
        {
            Vector3 p = outPositions[i];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 aabbCenter = (min + max) * 0.5f;
        float maxExtent = 0f;
        for (int i = 0; i < outPositions.Count; i++)
        {
            Vector3 centered = outPositions[i] - aabbCenter;
            outPositions[i] = centered;
            maxExtent = Mathf.Max(maxExtent, Mathf.Abs(centered.x), Mathf.Abs(centered.y), Mathf.Abs(centered.z));
        }

        // Normalize so Model.Scale is approximately "item size in meters".
        if (maxExtent > 0.0001f)
        {
            float inv = 1f / maxExtent;
            for (int i = 0; i < outPositions.Count; i++)
            {
                outPositions[i] *= inv;
            }
        }

        var mesh = new Mesh { name = meshName };
        if (outPositions.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetVertices(outPositions);
        if (outUvs.Count == outPositions.Count)
        {
            mesh.SetUVs(0, outUvs);
        }

        mesh.SetTriangles(triangles, 0);
        // Always rebuild normals from triangles so winding/X-flip can't hide the mesh.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static int AddFaceVertex(
        string token,
        List<Vector3> positions,
        List<Vector2> uvs,
        List<Vector3> normals,
        List<Vector3> outPositions,
        List<Vector2> outUvs,
        List<Vector3> outNormals,
        Dictionary<(int, int, int), int> vertexKey)
    {
        string[] bits = token.Split('/');
        int vi = ParseIndex(bits[0], positions.Count);
        int ti = bits.Length > 1 && bits[1].Length > 0 ? ParseIndex(bits[1], uvs.Count) : -1;
        int ni = bits.Length > 2 && bits[2].Length > 0 ? ParseIndex(bits[2], normals.Count) : -1;
        var key = (vi, ti, ni);
        if (vertexKey.TryGetValue(key, out int existing))
        {
            return existing;
        }

        int index = outPositions.Count;
        outPositions.Add(positions[vi]);
        outUvs.Add(ti >= 0 ? uvs[ti] : Vector2.zero);
        outNormals.Add(ni >= 0 ? normals[ni] : Vector3.up);
        vertexKey[key] = index;
        return index;
    }

    private static int ParseIndex(string raw, int count)
    {
        int index = int.Parse(raw, CultureInfo.InvariantCulture);
        return index < 0 ? count + index : index - 1;
    }

    private static float Parse(string raw) => float.Parse(raw, CultureInfo.InvariantCulture);
}
