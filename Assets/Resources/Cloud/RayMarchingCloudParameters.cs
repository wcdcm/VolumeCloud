using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

//定义体积云的参数
[Serializable]
[PostProcess(typeof(RayMarchingCloudRenderer), PostProcessEvent.BeforeStack, "Custom/RayMarchingCloud")]
public sealed class RayMarchingCloudParameters : PostProcessEffectSettings
{
    #region Texture
    /// <summary>
    /// 三维噪声贴图（基础体积噪声，决定云的大体形状）。
    /// </summary>
    public TextureParameter noise3D = new TextureParameter { value = null };
    
    /// <summary>
    /// 三维细节噪声贴图（给云加小尺度的细节纹理）。
    /// </summary>
    public TextureParameter noiseDetail3D = new TextureParameter { value = null };

    /// <summary>
    /// 控制基础噪声的平铺频率（影响云形态的整体重复度）。
    /// </summary>
    public FloatParameter shapeTiling = new FloatParameter { value = 0.01f };
    
    /// <summary>
    /// 控制细节噪声的平铺频率（影响小细节的密度）。
    /// </summary>
    public FloatParameter detailTiling = new FloatParameter { value = 0.1f };

    /// <summary>
    /// 天气图，用于控制云分布（例如：多云、晴天区域）。
    /// </summary>
    public TextureParameter weatherMap = new TextureParameter { value = null };
    
    /// <summary>
    /// 遮罩噪声，用于打破云层的统一性，让分布更自然。
    /// </summary>
    public TextureParameter maskNoise = new TextureParameter { value = null };
    
    /// <summary>
    /// 蓝噪声，用于减少采样时的条纹伪影（Dithering 抖动）。
    /// </summary>
    public TextureParameter blueNoise = new TextureParameter { value = null };
    
    #endregion

    #region Light
    /// <summary>
    /// 用于云的散射渐变
    /// </summary>
    public ColorParameter colA = new ColorParameter { value = Color.white };
    /// <summary>
    /// 用于云的散射渐变
    /// </summary>
    public ColorParameter colB = new ColorParameter { value = Color.white };
    /// <summary>
    /// 控制颜色插值的偏移值，决定云的色彩过渡。
    /// </summary>
    public FloatParameter colorOffset1 = new FloatParameter { value = 0.59f };
    /// <summary>
    /// 控制颜色插值的偏移值，决定云的色彩过渡。
    /// </summary>
    public FloatParameter colorOffset2 = new FloatParameter { value = 1.02f };
    /// <summary>
    /// 云粒子朝向太阳的吸光系数。
    /// </summary>
    public FloatParameter lightAbsorptionTowardSun = new FloatParameter { value = 0.1f };
    /// <summary>
    /// 光穿过云层的吸收系数（厚度越大，透光率越低）。
    /// </summary>
    public FloatParameter lightAbsorptionThroughCloud = new FloatParameter { value = 1 };
    /// <summary>
    /// : 控制相函数（Phase Function），模拟云中光散射的方向性（如向前散射）。
    /// </summary>
    public Vector4Parameter phaseParams = new Vector4Parameter { value = new Vector4(0.72f, 1, 0.5f, 1.58f) };
    
    #endregion

    #region Density
    
    /// <summary>
    /// 云密度的基础偏移（整体变厚或变薄）。
    /// </summary>
    public FloatParameter densityOffset = new FloatParameter { value = 4.02f };
    /// <summary>
    /// 密度倍率，调节整体浓度。
    /// </summary>
    public FloatParameter densityMultiplier = new FloatParameter { value = 2.31f };
    /// <summary>
    /// Ray Marching 的采样步长（影响渲染精度与性能）。
    /// </summary>
    public FloatParameter step = new FloatParameter { value = 1.2f };
    /// <summary>
    /// 次级光线步长（用于阴影或二次散射）。
    /// </summary>
    public FloatParameter rayStep = new FloatParameter { value = 1.2f };
    /// <summary>
    /// 光线步进偏移的强度（避免采样伪影）。
    /// </summary>
    public FloatParameter rayOffsetStrength = new FloatParameter { value = 1.5f };
    /// <summary>
    /// 降采样因子（性能优化，降低分辨率渲染云）。
    /// </summary>
    [Range(1, 16)] public IntParameter Downsample = new IntParameter { value = 4 };
    /// <summary>
    /// 云层的高度权重，控制随高度变化的密度。
    /// </summary>
    [Range(0, 1)] public FloatParameter heightWeights = new FloatParameter { value = 1 };
    /// <summary>
    /// 基础噪声权重（X,Y,Z,W 分别对应 shape noise 的不同贡献）。
    /// </summary>
    public Vector4Parameter shapeNoiseWeights = new Vector4Parameter { value = new Vector4(-0.17f, 27.17f, -3.65f, -0.08f) };
    /// <summary>
    /// 细节噪声权重（调节云纹理细节强度）。
    /// </summary>
    public FloatParameter detailWeights = new FloatParameter { value = -3.76f };
    /// <summary>
    /// 细节噪声与基础噪声的混合比例。
    /// </summary>
    public FloatParameter detailNoiseWeight = new FloatParameter { value = 0.12f };
    /// <summary>
    /// 控制云的流动速度和噪声扭曲强度：(x, y): XY 平面的流动速度 z: warp 扭曲强度 w: warp 频率
    /// </summary>
    public Vector4Parameter xy_Speed_zw_Warp = new Vector4Parameter { value = new Vector4(0.05f, 1, 1, 10) };
    
    #endregion
}



public sealed class RayMarchingCloudRenderer : PostProcessEffectRenderer<RayMarchingCloudParameters>//PostProcessEffectRenderer<T>中T一般传入用来描述效果的参数集
{
    GameObject findCloudBox;
    Transform cloudTransform;
    Vector3 boundsMin;//云体积的最小点，传递给 Shader。
    Vector3 boundsMax;//云体积的最大点，传递给 Shader。
    [HideInInspector]
    public Material DownscaleDepthMaterial;//用于深度降采样的材质
    public override DepthTextureMode GetCameraFlags()
    {
        return DepthTextureMode.Depth; 
    }

    public override void Init()
    {
        findCloudBox = GameObject.Find("CloudBox");

        if (findCloudBox != null)
        {
            cloudTransform = findCloudBox.GetComponent<Transform>();
            
        }
    }
    public override void Render(PostProcessRenderContext context)
    {
        /*
         * context 代表当前的后处理渲染上下文（包含相机、RT、命令缓冲等信息）。
         * context.propertySheets.Get(...)
         * propertySheets.Get(shader) 会返回一个 PropertySheet，它是 Unity PostProcessing v2 提供的包装类。
         * 
         * PropertySheet 的作用就是：
         * 管理 Shader 与 Pass
         * 提供 sheet.properties.SetXXX(...) 来设置 uniform 参数（比如贴图、颜色、矩阵）。
         * 
         * 这段代码获取了一个基于 "Hidden/Custom/RayMarchingCloud" 的 PropertySheet。
         * 之后就能用 sheet.properties.SetXXX(...) 把脚本里的参数传进去，然后在 cmd.BlitFullscreenTriangle(...) 时调用这个 Shader 来绘制云。
         */
        var sheet = context.propertySheets.Get(Shader.Find("Hidden/Custom/RayMarchingCloud"));
         
        //把 Unity 相机的投影矩阵转换成 GPU 实际要用的投影矩阵（考虑图形 API 差异），然后存在 projectionMatrix 变量里。
        Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(context.camera.projectionMatrix, false);
        
        sheet.properties.SetMatrix(Shader.PropertyToID("_InverseProjectionMatrix"), projectionMatrix.inverse);//将投影矩阵的逆矩阵传入Shader，让 Shader 能从屏幕空间 (NDC) 反推出视锥中的射线方向，用来做 Ray Marching 体积采样。
        sheet.properties.SetMatrix(Shader.PropertyToID("_InverseViewMatrix"), context.camera.cameraToWorldMatrix);//将视图矩阵的逆矩阵传入Shader，让顶点能从相机空间转换回世界空间
        sheet.properties.SetVector(Shader.PropertyToID("_CameraDir"), context.camera.transform.forward);//将相机方向传入Shader

        if (cloudTransform != null){
            boundsMin = cloudTransform.position - cloudTransform.localScale / 2;
            boundsMax = cloudTransform.position + cloudTransform.localScale / 2;

            sheet.properties.SetVector(Shader.PropertyToID("_boundsMin"), boundsMin);
            sheet.properties.SetVector(Shader.PropertyToID("_boundsMax"), boundsMax);
        }

        #region 传入3D纹理、细节纹理、噪声遮罩等
        
        if (settings.noise3D.value != null)
        {
            sheet.properties.SetTexture(Shader.PropertyToID("_noiseTex"), settings.noise3D.value);
        }
        if (settings.noiseDetail3D.value != null)
        {
            sheet.properties.SetTexture(Shader.PropertyToID("_noiseDetail3D"), settings.noiseDetail3D.value);
        }
        if (settings.weatherMap.value != null)
        {
            sheet.properties.SetTexture(Shader.PropertyToID("_weatherMap"), settings.weatherMap.value);
        }
        if (settings.maskNoise.value != null)
        {
            sheet.properties.SetTexture(Shader.PropertyToID("_maskNoise"), settings.maskNoise.value);
        }
        #endregion

        #region 蓝噪声
        /*
         * 蓝噪声的作用：
         * 在体积云（Ray Marching）里，每个像素会进行多次体积采样，容易产生条纹状伪影。
         * 蓝噪声贴图用来 打乱采样位置，让伪影变成高频噪点。
         * 高频噪点更容易被 TAA（Temporal Anti-Aliasing） 或其他滤波方法平滑掉，看起来更自然。
         */
        if (settings.blueNoise.value != null)
        {
            Vector4 screenUv = new Vector4(
            (float)context.screenWidth / (float)settings.blueNoise.value.width,
            (float)context.screenHeight / (float)settings.blueNoise.value.height,0,0);
            sheet.properties.SetVector(Shader.PropertyToID("_BlueNoiseCoords"), screenUv);
            sheet.properties.SetTexture(Shader.PropertyToID("_BlueNoise"), settings.blueNoise.value);
        }
        #endregion

        #region 其他必要数据传递
        
        sheet.properties.SetFloat(Shader.PropertyToID("_shapeTiling"), settings.shapeTiling.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_detailTiling"), settings.detailTiling.value);

        sheet.properties.SetFloat(Shader.PropertyToID("_step"), settings.step.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_rayStep"), settings.rayStep.value);
        
        sheet.properties.SetFloat(Shader.PropertyToID("_densityOffset"), settings.densityOffset.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_densityMultiplier"), settings.densityMultiplier.value);

        

        sheet.properties.SetColor(Shader.PropertyToID("_colA"), settings.colA.value);
        sheet.properties.SetColor(Shader.PropertyToID("_colB"), settings.colB.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_colorOffset1"), settings.colorOffset1.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_colorOffset2"), settings.colorOffset2.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_lightAbsorptionTowardSun"), settings.lightAbsorptionTowardSun.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_lightAbsorptionThroughCloud"), settings.lightAbsorptionThroughCloud.value);

        
        sheet.properties.SetFloat(Shader.PropertyToID("_rayOffsetStrength"), settings.rayOffsetStrength.value);
        sheet.properties.SetVector(Shader.PropertyToID("_phaseParams"), settings.phaseParams.value);
        sheet.properties.SetVector(Shader.PropertyToID("_xy_Speed_zw_Warp"), settings.xy_Speed_zw_Warp.value);
        
        sheet.properties.SetVector(Shader.PropertyToID("_shapeNoiseWeights"), settings.shapeNoiseWeights.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_heightWeights"), settings.heightWeights.value);

        
        sheet.properties.SetFloat(Shader.PropertyToID("_detailWeights"), settings.detailWeights.value);
        sheet.properties.SetFloat(Shader.PropertyToID("_detailNoiseWeight"), settings.detailNoiseWeight.value);

        #endregion

        /*把云体积盒 (cloudTransform) 的世界空间变换矩阵（带缩放修正）传到 Shader，让 Shader 在做 Ray Marching 时能正确判断光线和体积盒的关系。*/
        Quaternion rotation = Quaternion.Euler(cloudTransform.eulerAngles);
        Vector3 scaleMatrix = cloudTransform.localScale * 0.1f;
        scaleMatrix = new Vector3(1 / scaleMatrix.x, 1 / scaleMatrix.y, 1 / scaleMatrix.z);
        Matrix4x4 TRSMatrix = Matrix4x4.TRS(cloudTransform.position, rotation, scaleMatrix);//Matrix4x4.TRS(平移, 旋转, 缩放) → 得到一个 4×4 变换矩阵。这里得到的矩阵可以把点/射线从世界空间变换到云盒局部空间（因为用了逆缩放）。
        sheet.properties.SetMatrix(Shader.PropertyToID("_TRSMatrix"), TRSMatrix);

        
        var cmd = context.command;//CommandBuffer，用于发送 GPU 渲染指令。后续所有 Blit / SetGlobalTexture 都是往 GPU 命令缓冲里写命令。
        
        //降深度采样
        var DownsampleDepthID = Shader.PropertyToID("_DownsampleTemp");
        context.GetScreenSpaceTemporaryRT(cmd, DownsampleDepthID, 0, context.sourceFormat, RenderTextureReadWrite.Default, FilterMode.Point, context.screenWidth / settings.Downsample.value, context.screenHeight / settings.Downsample.value);
        cmd.BlitFullscreenTriangle(context.source, DownsampleDepthID, sheet, 1);
       
        /*
         * 设置全局纹理：
         * 跨 Pass 共享数据
         * 例如你先渲染了低分辨率云到 _DownsampleColor，Pass 2 需要读取它来合成。
         * 用全局纹理可以不必在每个 Material 或 PropertySheet 中单独绑定。
         * 跨 Shader 共享数据
         * 多个不同 Shader 需要读取同一个深度图、光照纹理或噪声纹理。
         */
        cmd.SetGlobalTexture(Shader.PropertyToID("_LowDepthTexture"), DownsampleDepthID);

        //降cloud分辨率 并使用第0个pass 渲染云
        var DownsampleColorID = Shader.PropertyToID("_DownsampleColor");
        context.GetScreenSpaceTemporaryRT(cmd, DownsampleColorID, 0, context.sourceFormat, RenderTextureReadWrite.Default, FilterMode.Trilinear, context.screenWidth / settings.Downsample.value, context.screenHeight / settings.Downsample.value);
        cmd.BlitFullscreenTriangle(context.source, DownsampleColorID, sheet,0);

        //降分辨率后的云设置回_DownsampleColor
        cmd.SetGlobalTexture(Shader.PropertyToID("_DownsampleColor"), DownsampleColorID);

        //使用第2个Pass 合成
        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

        //释放临时RT
        cmd.ReleaseTemporaryRT(DownsampleColorID);
        cmd.ReleaseTemporaryRT(DownsampleDepthID);

    }
}
