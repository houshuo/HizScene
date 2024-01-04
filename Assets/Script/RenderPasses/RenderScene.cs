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
    public int hizCullingFirstPassKernel;
    [HideInInspector]
    public int hizCullingSecondPassKernel;

    class SceneRenderPass : ScriptableRenderPass
    {
        private int passKernel;
        public RenderScene renderScene;

        public SceneRenderPass(int passKernel)
        {
            this.passKernel = passKernel;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            Matrix4x4 matrixVP = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;

            CommandBuffer cmd = CommandBufferPool.Get("RenderScene");
            foreach (MeshManager mesh in meshes)
            {
                //Hiz Const
                cmd.SetComputeIntParam(renderScene.HizCullingCS,"_TotalCount", mesh.slot);
                cmd.SetComputeMatrixParam(renderScene.HizCullingCS,"_UNITY_MATRIX_VP", matrixVP);
                cmd.SetComputeFloatParam(renderScene.HizCullingCS,"_HizTextureSize", renderScene.m_textureSize);
                cmd.SetComputeFloatParam(renderScene.HizCullingCS,"_LODCount", renderScene.m_LODCount);
                //HiZ Texture
                cmd.SetComputeTextureParam(renderScene.HizCullingCS, passKernel, "_HiZMap", renderScene.m_HiZDepthTexture);

                cmd.SetComputeBufferParam(renderScene.HizCullingCS, passKernel, "_InstanceAABBBuffer", mesh.AABBBuffer);
                cmd.SetComputeBufferParam(renderScene.HizCullingCS, passKernel, "_InstanceArgumentBuffer", mesh.ArgumentBuffer);

                int cullingGroupX = Mathf.CeilToInt(mesh.slot / 64f);
                cmd.DispatchCompute(renderScene.HizCullingCS, passKernel, cullingGroupX, 1, 1);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                //Draw
                cmd.SetGlobalBuffer("_Vertices", mesh.VertexBuffer);
                for (int i = 0; i < mesh.slot; i++)
                {
                    cmd.DrawProceduralIndirect(mesh.IndexBuffer, Matrix4x4.identity, renderScene.renderMaterial, 0, MeshTopology.Triangles, mesh.ArgumentBuffer, i * 20);
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
            CommandBufferPool.Release(cmd);
        }
    }
    SceneRenderPass sceneRenderFirstPass;
    SceneRenderPass sceneRenderSecondPass;

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
            RenderTargetIdentifier lastRenderTarget = renderingData.cameraData.targetTexture;
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
            cmd.SetRenderTarget(lastRenderTarget);
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
    HizTextureRenderPass hizTextureFirstPass;
    HizTextureRenderPass hizTextureSecondPass;

    public override void Create()
    {
        Utils.TryGetKernel("CSFirstPass", ref HizCullingCS, ref hizCullingFirstPassKernel);
        Utils.TryGetKernel("CSSecondPass", ref HizCullingCS, ref hizCullingSecondPassKernel);
        m_generateBufferMaterial = new Material(generateBufferShader);
        
        //设置Hiz贴图的格式
        m_HiZDepthTexture = new RenderTexture(m_textureSize, m_textureSize, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
        m_HiZDepthTexture.filterMode = FilterMode.Point;
        m_HiZDepthTexture.useMipMap = true;
        m_HiZDepthTexture.autoGenerateMips = false;
        m_HiZDepthTexture.Create();
        m_HiZDepthTexture.hideFlags = HideFlags.HideAndDontSave;

        //第一个渲染Pass
        sceneRenderFirstPass = new SceneRenderPass(hizCullingFirstPassKernel);
        sceneRenderFirstPass.renderScene = this;
        sceneRenderFirstPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        
        //生成渲染之后的深度图
        hizTextureFirstPass = new HizTextureRenderPass();
        hizTextureFirstPass.renderScene = this;
        hizTextureFirstPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        
        //第二个渲染Pass
        sceneRenderSecondPass = new SceneRenderPass(hizCullingSecondPassKernel);
        sceneRenderSecondPass.renderScene = this;
        sceneRenderSecondPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        
        //这一帧最后一个深度生成下一帧使用
        hizTextureSecondPass = new HizTextureRenderPass();
        hizTextureSecondPass.renderScene = this;
        hizTextureSecondPass.renderPassEvent = RenderPassEvent.AfterRendering;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(sceneRenderFirstPass);
        renderer.EnqueuePass(hizTextureFirstPass);
        renderer.EnqueuePass(sceneRenderSecondPass);
        renderer.EnqueuePass(hizTextureSecondPass);
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