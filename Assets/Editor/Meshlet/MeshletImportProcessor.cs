using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct Subset
{
    public uint Offset;
    public uint Count;
}

[StructLayout(LayoutKind.Sequential)]
public struct Meshlet
{
    public uint VertCount;
    public uint VertOffset;
    public uint PrimCount;
    public uint PrimOffset;
}

[StructLayout(LayoutKind.Sequential)]
public struct PackedTriangle
{
    uint triangle;
    public uint i0 { get { return triangle & 0x3FF; } }
    public uint i1 { get { return (triangle >> 10) & 0x3FF; } }
    public uint i2 { get { return (triangle >> 20) & 0x3FF; } }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct CullData
{
    public Vector4 BoundingSphere;
    public fixed byte NormalCone[4];
    public float ApexOffset;
}


public unsafe class MeshletImportProcessor
{
    [DllImport("UnityMeshletGenerator")]
    public static extern void Clear();

    [DllImport("UnityMeshletGenerator")]
    public static extern bool ComputeMeshlets_index16(uint maxVerts, uint maxPrims, ushort[] indices, uint indicesCount, Subset[] indexSubsets, uint indexSubsetsCount, Vector3[] positions, uint positionsCount,
        Subset** meshletSubsets, out uint meshletSubsetsCount, Meshlet** meshlets, out uint meshletsCount, ushort** uniqueVertexIndices, out uint uniqueVertexIndicesCount, PackedTriangle** primitiveIndices, out uint primitiveIndicesCount, uint flag, CullData** cullData);

    [DllImport("UnityMeshletGenerator")]
    public static extern bool ComputeMeshlets_index32(uint maxVerts, uint maxPrims, uint[] indices, uint indicesCount, Subset[] indexSubsets, uint indexSubsetsCount, Vector3[] positions, uint positionsCount,
        Subset** meshletSubsets, out uint meshletSubsetsCount, Meshlet** meshlets, out uint meshletsCount, uint** uniqueVertexIndices, out uint uniqueVertexIndicesCount, PackedTriangle** primitiveIndices, out uint primitiveIndicesCount, uint flag, CullData** cullData);

    [MenuItem("Assets/Make Meshlets", priority = 10)]
    static void MakeMeshletPrefab()
    {
        if (Selection.activeObject.GetType() != typeof(GameObject))
            return;
        GameObject copy = GameObject.Instantiate(Selection.activeObject) as GameObject;
        ProcessObject(copy);
    }

    static void ProcessObject(GameObject go)
    {
        MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters == null)
            return;
        foreach(var meshFilter in meshFilters)
        {
            Mesh meshletMesh = meshFilter.mesh; //故意拷贝一份mesh
            Vector3[] positions = meshletMesh.vertices;
            List<Subset> subsets = new List<Subset>();
            int currentBaseIndex = 0;
            List<uint> indices = new List<uint>();
            for (int i = 0; i < meshletMesh.subMeshCount; i++)
            {
                int[] subIndices = meshletMesh.GetIndices(i);
                subsets.Add(new Subset { Count = (uint)subIndices.Length, Offset = (uint)currentBaseIndex });
                foreach (var subIndex in subIndices)
                    indices.Add((uint)subIndex);
            }
            Subset* meshletSubsets;
            uint meshletSubsetCount;
            Meshlet* meshlets;
            uint meshletsCount;
            uint* uniqueVertexIndices;
            uint uniqueVertexIndicesCount;
            PackedTriangle* primitiveIndices;
            uint primitiveIndicesCount;
            CullData* cullData;

            ComputeMeshlets_index32(64, 126, indices.ToArray(), (uint)indices.Count, subsets.ToArray(), (uint)subsets.Count, positions, (uint)positions.Length,
                &meshletSubsets, out meshletSubsetCount, &meshlets, out meshletsCount, &uniqueVertexIndices, out uniqueVertexIndicesCount, &primitiveIndices, out primitiveIndicesCount, 4, &cullData);

            meshletMesh.subMeshCount = (int)meshletsCount;
            Meshlet* currentMeshlet = meshlets;
            CullData* currentCullData = cullData;
            for (int i = 0; i < meshletsCount; i++, currentMeshlet++, currentCullData++)
            {
                int[] meshletIndices = new int[currentMeshlet->PrimCount * 3];
                for (int j = 0; j < currentMeshlet->PrimCount; j++)
                {
                    PackedTriangle primitive = primitiveIndices[currentMeshlet->PrimOffset + j];
                    meshletIndices[3 * j] = (int)uniqueVertexIndices[(int)(primitive.i0 + currentMeshlet->VertOffset)];
                    meshletIndices[3 * j + 1] = (int)uniqueVertexIndices[(int)(primitive.i1 + currentMeshlet->VertOffset)];
                    meshletIndices[3 * j + 2] = (int)uniqueVertexIndices[(int)(primitive.i2 + currentMeshlet->VertOffset)];
                }
                meshletMesh.SetIndices(meshletIndices, MeshTopology.Triangles, i, true);
                Debug.Log(currentCullData->BoundingSphere);
            }
            Clear();

            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] materials = new Material[meshletsCount];
            for (int i = 0; i < meshletsCount; i++)
            {
                materials[i] = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                Color color = new Color(UnityEngine.Random.Range(0f, 1f), UnityEngine.Random.Range(0f, 1f), UnityEngine.Random.Range(0f, 1f), 1f);
                materials[i].SetColor("_BaseColor", color);
            }
            meshRenderer.sharedMaterials = materials;
        }
    }
}
