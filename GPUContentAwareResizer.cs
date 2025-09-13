using UnityEngine;

public class GPUContentAwareResizer : MonoBehaviour
{
    public ComputeShader computeShader;
    public Texture2D sourceTexture;
    public int targetWidth;
    public Renderer outputRenderer;

    private RenderTexture _processedTexture;
    private RenderTexture _energyMap;
    private RenderTexture _cumulativeEnergyMap;

    private int currentWidth;
    private int currentHeight;

    void Start()
    {
        if (sourceTexture == null)
        {
            Debug.LogError("Source Texture is not assigned.");
            return;
        }

        currentWidth = sourceTexture.width;
        currentHeight = sourceTexture.height;

        _processedTexture = CreateRenderTexture(currentWidth, currentHeight, RenderTextureFormat.ARGB32);
        Graphics.Blit(sourceTexture, _processedTexture);
    }

    [ContextMenu("Resize Image GPU")]
    public void ResizeImage()
    {
        if (computeShader == null || _processedTexture == null)
        {
            Debug.LogError("Compute Shader or Processed Texture is not assigned.");
            return;
        }

        // Poprawka: U¿yj for zamiast foreach na int[], by unikn¹æ b³êdu GetEnumerator
        int[] kernels = new int[] {
            computeShader.FindKernel("EnergyKernel"),
            computeShader.FindKernel("CumulativeEnergyKernel"),
            computeShader.FindKernel("ResizeSeamKernel")
        };
        for (int i = 0; i < kernels.Length; i++)
        {
            if (kernels[i] < 0)
            {
                Debug.LogError("Kernel not found: " + (i == 0 ? "EnergyKernel" : i == 1 ? "CumulativeEnergyKernel" : "ResizeSeamKernel"));
                return;
            }
        }

        int widthDifference = targetWidth - currentWidth;
        bool isEnlargement = widthDifference > 0;
        int absDiff = Mathf.Abs(widthDifference);
        for (int i = 0; i < absDiff; i++)
        {
            ResizeSingleSeamGPU(isEnlargement);
            Debug.Log($"{(isEnlargement ? "Added" : "Removed")} vertical seam {i + 1}/{absDiff}");
        }

        if (outputRenderer != null)
        {
            outputRenderer.material.mainTexture = _processedTexture;
        }

        Debug.Log("GPU Resize Complete!");
    }

    void ResizeSingleSeamGPU(bool isEnlargement)
    {
        int width = currentWidth;
        int height = currentHeight;

        // Prepare textures
        ReleaseRenderTexture(_energyMap);
        ReleaseRenderTexture(_cumulativeEnergyMap);
        _energyMap = CreateRenderTexture(width, height, RenderTextureFormat.RFloat);
        _cumulativeEnergyMap = CreateRenderTexture(width, height, RenderTextureFormat.RGFloat);

        int energyKernel = computeShader.FindKernel("EnergyKernel");
        computeShader.SetTexture(energyKernel, "SourceTexture", _processedTexture);
        computeShader.SetTexture(energyKernel, "EnergyMap", _energyMap);
        computeShader.SetInt("Width", width);
        computeShader.SetInt("Height", height);
        computeShader.Dispatch(energyKernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);

        // Row-by-row Cumulative DP
        int cumulativeKernel = computeShader.FindKernel("CumulativeEnergyKernel");
        computeShader.SetTexture(cumulativeKernel, "EnergyMap", _energyMap);
        computeShader.SetTexture(cumulativeKernel, "CumulativeEnergyMap", _cumulativeEnergyMap);
        computeShader.SetInt("Width", width);
        computeShader.SetInt("Height", height);
        for (int y = 0; y < height; y++)
        {
            computeShader.SetInt("CurrentRow", y);
            computeShader.Dispatch(cumulativeKernel, Mathf.CeilToInt(width / 8f), 1, 1);
        }

        // Find seam on CPU
        int[] seamPath = FindSeamPathOnCPU(width, height);

        // Resize kernel
        int newWidth = isEnlargement ? width + 1 : width - 1;
        RenderTexture destTexture = CreateRenderTexture(newWidth, height, RenderTextureFormat.ARGB32);
        ComputeBuffer seamBuffer = new ComputeBuffer(height, sizeof(int));
        seamBuffer.SetData(seamPath);

        int resizeKernel = computeShader.FindKernel("ResizeSeamKernel");
        computeShader.SetTexture(resizeKernel, "SourceTexture", _processedTexture);
        computeShader.SetTexture(resizeKernel, "DestTexture", destTexture);
        computeShader.SetBuffer(resizeKernel, "SeamPath", seamBuffer);
        computeShader.SetInt("Width", width);
        computeShader.SetInt("Height", height);
        computeShader.SetBool("IsInsertion", isEnlargement);
        computeShader.Dispatch(resizeKernel, Mathf.CeilToInt(newWidth / 8f), Mathf.CeilToInt(height / 8f), 1);

        // Cleanup
        seamBuffer.Release();
        ReleaseRenderTexture(_processedTexture);
        _processedTexture = destTexture;
        currentWidth = newWidth;

        // Debug: Save cumulative map (uncomment to check)
        // SaveRenderTextureToFile(_cumulativeEnergyMap, "CumulativeDebug.png");
    }

    private int[] FindSeamPathOnCPU(int width, int height)
    {
        Texture2D cumCPU = new Texture2D(width, height, TextureFormat.RGFloat, false);
        RenderTexture.active = _cumulativeEnergyMap;
        cumCPU.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        cumCPU.Apply();
        RenderTexture.active = null;
        Color[] cumPixels = cumCPU.GetPixels();
        Destroy(cumCPU);

        // Poprawka: Find min in last row – u¿yj float, nie int[] konwersji
        float minEnergy = float.MaxValue;
        int endX = 0;
        for (int x = 0; x < width; x++)
        {
            float energy = cumPixels[(height - 1) * width + x].r;  // .r = energy
            if (energy < minEnergy)
            {
                minEnergy = energy;
                endX = x;
            }
        }

        int[] seam = new int[height];  // Poprawka: Inicjalizacja tablicy int[]
        seam[height - 1] = endX;
        int currentX = endX;  // Poprawka: U¿yj int, nie int[]

        // Backtrack up using .g for delta (-1,0,1 encoded as 0,1,2)
        for (int y = height - 2; y >= 0; y--)
        {
            float deltaEncoded = cumPixels[y * width + currentX].g;  // 0=left(-1), 1=center(0), 2=right(1)
            int delta = Mathf.RoundToInt(deltaEncoded - 1.0f);  // Decode to -1,0,1
            currentX += delta;
            currentX = Mathf.Clamp(currentX, 0, width - 1);
            seam[y] = currentX;
        }

        return seam;
    }

    // Helper: Save RT to PNG for debug
    void SaveRenderTextureToFile(RenderTexture rt, string filename)
    {
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        byte[] bytes = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/" + filename, bytes);
        Destroy(tex);
        RenderTexture.active = null;
    }

    RenderTexture CreateRenderTexture(int w, int h, RenderTextureFormat f)
    {
        RenderTexture rt = new RenderTexture(w, h, 0, f);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    void ReleaseRenderTexture(RenderTexture rt)
    {
        if (rt != null) rt.Release();
    }

    void OnDestroy()
    {
        ReleaseRenderTexture(_processedTexture);
        ReleaseRenderTexture(_energyMap);
        ReleaseRenderTexture(_cumulativeEnergyMap);
    }
}