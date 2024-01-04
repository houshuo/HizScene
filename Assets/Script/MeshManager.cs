using System.Collections.Generic;
using UnityEngine;

public struct Vertex
{
    public Vector3 position;
    public Vector3 normal;
    public Vector3 tangent;
}

public class MeshManager : MonoBehaviour
{
    public ComputeShader uploadComputeCS;
    private int uploadVertexKernel;
    private int uploadIndexKernel;

    public ComputeShader CopyComputeCS;
    private int copyVertexKernel;
    private int copyIndexKernel;
    private int copyArgumentsAndAABBKernel;

    private int VertexUploadBufferSize = 1024;
    private ComputeBuffer VertexPositionUploadBuffer;
    private ComputeBuffer VertexNormalUploadBuffer;
    private ComputeBuffer VertexTangentnUploadBuffer;
    private int IndexUploadBufferSize = 1024;
    private ComputeBuffer IndexUploadBuffer;

    private int VertexBufferCapacity = 1000000;
    public ComputeBuffer VertexBuffer { private set; get; }
    private int IndexBufferCapacity = 1000000;
    public GraphicsBuffer IndexBuffer { private set; get; }
    private int SlotCapacity = 4800;
    public ComputeBuffer ArgumentBuffer { private set; get; }
    public ComputeBuffer AABBBuffer { private set; get; }

    private int vertexBaseLocation;
    public int indexBaseLocation { private set; get; }
    public int slot { private set; get; }

    public static Queue<GameObject> JobQueue = new Queue<GameObject>();

    

    private bool TryGetKernels()
    {
        return Utils.TryGetKernel("CSUploadVertexMain", ref uploadComputeCS, ref uploadVertexKernel) &&
                Utils.TryGetKernel("CSUploadIndexMain", ref uploadComputeCS, ref uploadIndexKernel) &&
                Utils.TryGetKernel("CopyVertexBuffer", ref CopyComputeCS, ref copyVertexKernel) &&
                Utils.TryGetKernel("CopyIndexBuffer", ref CopyComputeCS, ref copyIndexKernel) &&
                Utils.TryGetKernel("CopyArgumentAndAABBBuffer", ref CopyComputeCS, ref copyArgumentsAndAABBKernel);
    }

    void Start()
    {
        vertexBaseLocation = 0;
        indexBaseLocation = 0;
        slot = 0;

        VertexPositionUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 3, ComputeBufferType.Structured);
        VertexNormalUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 3, ComputeBufferType.Structured);
        VertexTangentnUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 4, ComputeBufferType.Structured);
        IndexUploadBuffer = new ComputeBuffer(IndexUploadBufferSize, sizeof(uint), ComputeBufferType.Structured);

        VertexBuffer = new ComputeBuffer(VertexBufferCapacity, sizeof(float) * 9, ComputeBufferType.Structured);
        IndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Raw, IndexBufferCapacity, sizeof(uint));
        ArgumentBuffer = new ComputeBuffer(SlotCapacity, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
        AABBBuffer = new ComputeBuffer(SlotCapacity, sizeof(float) * 6, ComputeBufferType.Structured);

        TryGetKernels();

        RenderScene.meshes.Add(this);
    }

    // Update is called once per frame
    void Update()
    {
        if (JobQueue.Count > 0)
        {
            GameObject go = JobQueue.Dequeue();
            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshFilter != null && meshRenderer != null)
            {
                DoUpload(meshFilter, meshRenderer);
            }
        }
    }

    private void OnDestroy()
    {
        RenderScene.meshes.Remove(this);

        VertexPositionUploadBuffer.Dispose();
        VertexNormalUploadBuffer.Dispose();
        VertexTangentnUploadBuffer.Dispose();
        IndexUploadBuffer.Dispose();

        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
        ArgumentBuffer.Dispose();
        AABBBuffer.Dispose();
    }


    void DoUpload(MeshFilter meshFilter, MeshRenderer meshRenderer)
    {
        Matrix4x4 m = meshRenderer.transform.localToWorldMatrix;
        uploadComputeCS.SetMatrix("_UNITY_MATRIX_M", m);
        Bounds bound = meshRenderer.bounds;
        
        Mesh mesh = meshFilter.mesh;
        int vertexCount = mesh.vertexCount;
        uploadComputeCS.SetFloat("_VertexBaseIndex", vertexBaseLocation);
        if(vertexBaseLocation + vertexCount > VertexBufferCapacity)
        {
            VertexBufferCapacity = Mathf.Max(2 * VertexBufferCapacity, vertexBaseLocation + vertexCount);
            ComputeBuffer newVertexBuffer = new ComputeBuffer(VertexBufferCapacity, sizeof(float) * 9, ComputeBufferType.Structured);
            CopyComputeCS.SetFloat("_VertexCopyCount", vertexBaseLocation);
            CopyComputeCS.SetBuffer(copyVertexKernel, "_VertexSource", VertexBuffer);
            CopyComputeCS.SetBuffer(copyVertexKernel, "_VertexDest", newVertexBuffer);
            int copyGroupX = Mathf.Max(1, Mathf.CeilToInt(vertexBaseLocation / 128f));
            CopyComputeCS.Dispatch(copyVertexKernel, copyGroupX, 1, 1);
            VertexBuffer.Dispose();
            VertexBuffer = newVertexBuffer;
        }
        vertexBaseLocation += vertexCount;
        uploadComputeCS.SetFloat("_VertexCount", vertexCount);

        if(vertexCount > VertexUploadBufferSize)
        {
            VertexUploadBufferSize = Mathf.Max(VertexUploadBufferSize * 2 / 1024 * 1024, vertexCount);
            VertexPositionUploadBuffer.Dispose();
            VertexPositionUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 3, ComputeBufferType.Structured);
            VertexNormalUploadBuffer.Dispose();
            VertexNormalUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 3, ComputeBufferType.Structured);
            VertexTangentnUploadBuffer.Dispose();
            VertexTangentnUploadBuffer = new ComputeBuffer(VertexUploadBufferSize, sizeof(float) * 4, ComputeBufferType.Structured);
        }
        VertexPositionUploadBuffer.SetData(mesh.vertices);
        VertexNormalUploadBuffer.SetData(mesh.normals);
        VertexTangentnUploadBuffer.SetData(mesh.tangents);

        int uploadVertexGroupX = Mathf.Max(1, Mathf.CeilToInt(vertexCount / 64f));
        uploadComputeCS.SetBuffer(uploadVertexKernel, "_InputVertexPosition", VertexPositionUploadBuffer);
        uploadComputeCS.SetBuffer(uploadVertexKernel, "_InputVertexNormal", VertexNormalUploadBuffer);
        uploadComputeCS.SetBuffer(uploadVertexKernel, "_InputVertexTangent", VertexTangentnUploadBuffer);
        uploadComputeCS.SetBuffer(uploadVertexKernel, "_VertexBuffer", VertexBuffer);
        uploadComputeCS.Dispatch(uploadVertexKernel, uploadVertexGroupX, 1, 1);

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            if (slot >= SlotCapacity)
            {
                SlotCapacity = SlotCapacity * 2 / 1024 * 1024;
                ComputeBuffer newArgumentBuffer = new ComputeBuffer(SlotCapacity, sizeof(uint) * 5, ComputeBufferType.IndirectArguments);
                ComputeBuffer newAABBBuffer = new ComputeBuffer(SlotCapacity, sizeof(float) * 6, ComputeBufferType.Structured);
                CopyComputeCS.SetFloat("_SlotCopyCount", slot);
                CopyComputeCS.SetBuffer(copyArgumentsAndAABBKernel, "_ArgumentBufferSource", ArgumentBuffer);
                CopyComputeCS.SetBuffer(copyArgumentsAndAABBKernel, "_AABBBufferSource", AABBBuffer);
                CopyComputeCS.SetBuffer(copyArgumentsAndAABBKernel, "_ArgumentBufferDest", newArgumentBuffer);
                CopyComputeCS.SetBuffer(copyArgumentsAndAABBKernel, "_AABBBufferDest", newAABBBuffer);
                int copyGroupX = Mathf.Max(1, Mathf.CeilToInt(SlotCapacity / 128f));
                CopyComputeCS.Dispatch(copyArgumentsAndAABBKernel, copyGroupX, 1, 1);
                ArgumentBuffer.Dispose();
                AABBBuffer.Dispose();
                ArgumentBuffer = newArgumentBuffer;
                AABBBuffer = newAABBBuffer;
            }
            float[] aabbArray = { bound.center.x, bound.center.y, bound.center.z, bound.extents.x, bound.extents.y, bound.extents.z };
            AABBBuffer.SetData(aabbArray, 0, 6 * slot, 6);
            int indexCount = (int)mesh.GetIndexCount(i);
            int[] argumentArray = { indexCount, 0 , indexBaseLocation, 0, 0};
            ArgumentBuffer.SetData(argumentArray, 0, 5 * slot, 5);

            uploadComputeCS.SetFloat("_IndexBaseIndex", indexBaseLocation);
            uploadComputeCS.SetFloat("_IndexCount", indexCount);
            if (indexBaseLocation + indexCount > IndexBufferCapacity)
            {
                IndexBufferCapacity = Mathf.Max(2 * IndexBufferCapacity, indexBaseLocation + indexCount);
                GraphicsBuffer newIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Raw, IndexBufferCapacity, sizeof(uint));
                CopyComputeCS.SetFloat("_IndexCopyCount", indexBaseLocation);
                CopyComputeCS.SetBuffer(copyIndexKernel, "_IndexSource", IndexBuffer);
                CopyComputeCS.SetBuffer(copyIndexKernel, "_IndexDest", newIndexBuffer);
                int copyGroupX = Mathf.Max(1, Mathf.CeilToInt(indexBaseLocation / 128f));
                CopyComputeCS.Dispatch(copyIndexKernel, copyGroupX, 1, 1);
                IndexBuffer.Dispose();
                IndexBuffer = newIndexBuffer;
            }
            indexBaseLocation += indexCount;

            if (indexCount > IndexUploadBufferSize)
            {
                IndexUploadBufferSize = Mathf.Max(IndexUploadBufferSize * 2 / 1024 * 1024, indexCount);
                IndexUploadBuffer.Dispose();
                IndexUploadBuffer = new ComputeBuffer(IndexUploadBufferSize, sizeof(uint), ComputeBufferType.Structured);
            }
            int[] indices = mesh.GetIndices(i);
            IndexUploadBuffer.SetData(indices);
            int uploadIndexGroupX = Mathf.Max(1, Mathf.CeilToInt(indexCount / 64f));
            uploadComputeCS.SetBuffer(uploadIndexKernel, "_InputIndex", IndexUploadBuffer);
            uploadComputeCS.SetBuffer(uploadIndexKernel, "_IndexBuffer", IndexBuffer);
            uploadComputeCS.Dispatch(uploadIndexKernel, uploadIndexGroupX, 1, 1);

            slot += 1;
        }
    }
}
