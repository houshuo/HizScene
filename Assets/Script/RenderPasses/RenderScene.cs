using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class RenderScene : ScriptableRendererFeature
{
    public static List<MeshManager> meshes = new List<MeshManager>();

    /// <summary>
    /// RenderScene
    /// </summary>
    public Material renderMaterial;
    //Hiz
    public ComputeShader HizCullingCS;
    [HideInInspector]
    public int hizCullingKernel;
    [HideInInspector]
    public ComputeBuffer HizResultBuffer;
    [HideInInspector]
    public ComputeBuffer HizResultCount;
    [HideInInspector]
    public uint[] HizResultCountBuffer;

    class SceneRenderPass : ScriptableRenderPass
    {
        public RenderScene renderScene;
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            Matrix4x4 matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

            CommandBuffer cmd = CommandBufferPool.Get("RenderScene");
            foreach (MeshManager mesh in meshes)
            {
                int meshCount = 0;
                //Hiz Const
                if (renderScene.HizResultBuffer.count < mesh.slot)
                {
                    renderScene.HizResultBuffer.Dispose();
                    renderScene.HizResultBuffer = new ComputeBuffer(Mathf.Max(renderScene.HizResultBuffer.count * 2, mesh.slot), sizeof(uint) * 5, ComputeBufferType.Structured);
                }
                renderScene.HizCullingCS.SetFloat("_TotalCount", mesh.slot);
                renderScene.HizCullingCS.SetMatrix("_UNITY_MATRIX_VP", matrixVP);
                renderScene.HizCullingCS.SetTexture(renderScene.hizCullingKernel, "_HiZMap", renderScene.m_HiZDepthTexture);
                renderScene.HizCullingCS.SetFloat("_HizTextureSize", renderScene.m_textureSize);
                renderScene.HizCullingCS.SetFloat("_LODCount", renderScene.m_LODCount);

                renderScene.HizCullingCS.SetBuffer(renderScene.hizCullingKernel, "_InstanceAABBBuffer", mesh.AABBBuffer);
                renderScene.HizCullingCS.SetBuffer(renderScene.hizCullingKernel, "_InstanceArgumentBuffer", mesh.ArgumentBuffer);

                renderScene.HizResultCountBuffer[0] = 0;
                renderScene.HizResultCount.SetData(renderScene.HizResultCountBuffer);
                renderScene.HizCullingCS.SetBuffer(renderScene.hizCullingKernel, "_CountBuffer", renderScene.HizResultCount);
                renderScene.HizCullingCS.SetBuffer(renderScene.hizCullingKernel, "_CullingResult", renderScene.HizResultBuffer);

                int cullingGroupX = Mathf.CeilToInt(mesh.slot / 64f);
                renderScene.HizCullingCS.Dispatch(renderScene.hizCullingKernel, cullingGroupX, 1, 1);
                renderScene.HizResultCount.GetData(renderScene.HizResultCountBuffer);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                meshCount = (int)renderScene.HizResultCountBuffer[0];
                ////No Hiz
                //cmd.SetGlobalBuffer("_IndirectArguments", mesh.ArgumentBuffer);
                //meshCount = mesh.slot;
                //Set Global Buffer
                cmd.SetGlobalBuffer("_Vertices", mesh.VertexBuffer);

                for (int i = 0; i < meshCount; i++)
                {
                    cmd.DrawProceduralIndirect(mesh.IndexBuffer, Matrix4x4.identity, renderScene.renderMaterial, 0, MeshTopology.Triangles, mesh.ArgumentBuffer, i * 20);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
    SceneRenderPass sceneRenderPass;

    /// <summary>
    /// Hiz Texture
    /// </summary>
    //Shader
    public Shader generateBufferShader;
    [HideInInspector]
    public Material m_generateBufferMaterial;
    //Textures
    [HideInInspector]
    public RenderTexture m_HiZDepthTexture = null;
    [HideInInspector]
    public int m_textureSize = 1024;
    [HideInInspector]
    public int m_LODCount = 10;

    class HizTextureRenderPass : ScriptableRenderPass
    {
        public RenderScene renderScene;
        private enum GenerateBufferPass
        {
            Blit,
            Reduce
        }
        //temporary render targets
        int[] m_Temporaries;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            m_Temporaries = new int[renderScene.m_LODCount - 1];
            //逐步生成每个Mip的贴图
            int size = 1 << renderScene.m_LODCount;
            for (int i = 0; i < renderScene.m_LODCount - 1; ++i)
            {
                m_Temporaries[i] = Shader.PropertyToID("Hiz_Temporary" + i.ToString());
                size >>= 1;
                size = Mathf.Max(size, 1);
                cmd.GetTemporaryRT(m_Temporaries[i], size, size, 0, FilterMode.Point, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("RenderHizBuffer");
            RenderTargetIdentifier id = new RenderTargetIdentifier(renderScene.m_HiZDepthTexture);
            cmd.Blit(null, id, renderScene.m_generateBufferMaterial, (int)GenerateBufferPass.Blit);
            int size = 1 << renderScene.m_LODCount;
            for (int i = 0; i < renderScene.m_LODCount - 1; ++i)
            {
                if (i == 0)
                {
                    cmd.Blit(id, m_Temporaries[0], renderScene.m_generateBufferMaterial, (int)GenerateBufferPass.Reduce);
                }
                else
                {
                    cmd.Blit(m_Temporaries[i - 1], m_Temporaries[i], renderScene.m_generateBufferMaterial, (int)GenerateBufferPass.Reduce);
                }

                cmd.CopyTexture(m_Temporaries[i], 0, 0, id, 0, i + 1);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            for (int i = 0; i < renderScene.m_LODCount - 1; ++i)
            {
                cmd.ReleaseTemporaryRT(m_Temporaries[i]);
            }
            base.FrameCleanup(cmd);
        }
    }
    HizTextureRenderPass hizTexturePass;

    public override void Create()
    {
        Utils.TryGetKernel("CSMain", ref HizCullingCS, ref hizCullingKernel);
        HizResultBuffer = new ComputeBuffer(10000, sizeof(uint) * 4, ComputeBufferType.Structured);
        HizResultCount = new ComputeBuffer(1, sizeof(uint));
        HizResultCountBuffer = new uint[1];
        m_generateBufferMaterial = new Material(generateBufferShader);

        sceneRenderPass = new SceneRenderPass();
        sceneRenderPass.renderScene = this;
        sceneRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

        //设置Hiz贴图的格式
        m_HiZDepthTexture = new RenderTexture(m_textureSize, m_textureSize, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
        m_HiZDepthTexture.filterMode = FilterMode.Point;
        m_HiZDepthTexture.useMipMap = true;
        m_HiZDepthTexture.autoGenerateMips = false;
        m_HiZDepthTexture.Create();
        m_HiZDepthTexture.hideFlags = HideFlags.HideAndDontSave;

        hizTexturePass = new HizTextureRenderPass();
        hizTexturePass.renderScene = this;
        hizTexturePass.renderPassEvent = RenderPassEvent.AfterRendering;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(sceneRenderPass);
        renderer.EnqueuePass(hizTexturePass);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (m_HiZDepthTexture != null)
        {
            m_HiZDepthTexture.Release();
            m_HiZDepthTexture = null;
        }
    }
}