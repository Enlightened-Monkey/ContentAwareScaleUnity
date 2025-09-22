using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GPU-Accelerated Content-Aware Image Resizer (Optimized Batch Version).
/// This version correctly batches seam data to minimize CPU-GPU stalls and fixes visual artifacts.
/// </summary>
public class GPUContentAwareResizer : MonoBehaviour
{
    public ComputeShader seamCarvingShader;
    public Texture2D sourceTexture;
    public Renderer outputRenderer;
    [Tooltip("Delay in seconds after scaling stops before the resize is applied.")]
    public float resizeDelay = 0.5f;
    [Tooltip("Number of seams to process in a single GPU batch. Higher is faster.")]
    public int seamProcessBatchSize = 20;

    private RenderTexture currentProcessedTexture;
    private Texture2D originalTexture;
    private int originalWidth, originalHeight;
    private Vector3 lastScale;
    private Coroutine resizeCoroutine;
    private Transform targetTransform;

    private int energyKernel, removeSeamsKernel, insertSeamsKernel;

    void Start()
    {
        if (sourceTexture != null && seamCarvingShader != null)
        {
            originalTexture = CreateReadableTextureCopy(sourceTexture);
            originalWidth = originalTexture.width;
            originalHeight = originalTexture.height;

            currentProcessedTexture = CreateRenderTexture(originalWidth, originalHeight);
            Graphics.Blit(originalTexture, currentProcessedTexture);

            energyKernel = seamCarvingShader.FindKernel("CalculateEnergy");
            removeSeamsKernel = seamCarvingShader.FindKernel("RemoveVerticalSeams");
            insertSeamsKernel = seamCarvingShader.FindKernel("InsertVerticalSeams");

            if (outputRenderer != null)
            {
                outputRenderer.material.mainTexture = currentProcessedTexture;
                targetTransform = outputRenderer.transform;
            }

            if (targetTransform != null) lastScale = targetTransform.localScale;
        }
        else Debug.LogError("Source Texture or Seam Carving Shader is not assigned!");
    }

    void Update()
    {
        if (targetTransform == null) return;
        if (targetTransform.localScale != lastScale)
        {
            lastScale = targetTransform.localScale;
            if (resizeCoroutine != null) StopCoroutine(resizeCoroutine);
            resizeCoroutine = StartCoroutine(DelayedResize());
        }
    }

    private IEnumerator DelayedResize()
    {
        yield return new WaitForSeconds(resizeDelay);

        int newWidth = Mathf.RoundToInt(originalWidth * targetTransform.localScale.x);
        int newHeight = Mathf.RoundToInt(originalHeight * targetTransform.localScale.y);

        newWidth = Mathf.Max(2, newWidth);
        newHeight = Mathf.Max(2, newHeight);

        ResizeImage(newWidth, newHeight);
    }

    public void ResizeImage(int targetWidth, int targetHeight)
    {
        if (originalTexture == null) return;

        Debug.Log($"Starting resize from {originalWidth}x{originalHeight} to {targetWidth}x{targetHeight}...");

        RenderTexture temp = CreateRenderTexture(originalWidth, originalHeight);
        Graphics.Blit(originalTexture, temp);
        ReleaseRenderTexture(currentProcessedTexture);
        currentProcessedTexture = temp;

        int widthDifference = currentProcessedTexture.width - targetWidth;
        if (widthDifference != 0)
            ProcessSeams(widthDifference > 0 ? widthDifference : -widthDifference, true, widthDifference < 0);

        int heightDifference = currentProcessedTexture.height - targetHeight;
        if (heightDifference != 0)
            ProcessSeams(heightDifference > 0 ? heightDifference : -heightDifference, false, heightDifference < 0);

        outputRenderer.material.mainTexture = currentProcessedTexture;
        Debug.Log($"Finished resize. Final dimensions: {currentProcessedTexture.width}x{currentProcessedTexture.height}");
    }

    private void ProcessSeams(int totalSeams, bool isVertical, bool toInsert)
    {
        int seamsProcessed = 0;
        while (seamsProcessed < totalSeams)
        {
            int seamsInThisBatch = Mathf.Min(seamProcessBatchSize, totalSeams - seamsProcessed);

            RenderTexture sourceTex = currentProcessedTexture;
            if (!isVertical) sourceTex = TransposeTexture(currentProcessedTexture);

            int width = sourceTex.width;
            int height = sourceTex.height;

            // 1. Calculate energy map once per batch
            float[,] energyMap = CalculateEnergyMapGPU(sourceTex);

            // 2. Find a batch of seams on the CPU
            List<int> allSeamData = new List<int>();
            for (int i = 0; i < seamsInThisBatch; i++)
            {
                int[] seam = FindVerticalSeamCPU(energyMap, width, height);
                allSeamData.AddRange(seam);
                // Erase seam from energy map to find the next best one
                for (int y = 0; y < height; y++) energyMap[seam[y], y] = float.MaxValue;
            }

            // 3. Dispatch GPU kernel once with all seam data
            int newWidth = toInsert ? width + seamsInThisBatch : width - seamsInThisBatch;
            RenderTexture resultTex = CreateRenderTexture(newWidth, height);

            ComputeBuffer seamBuffer = new ComputeBuffer(allSeamData.Count, sizeof(int));
            seamBuffer.SetData(allSeamData);

            int kernel = toInsert ? insertSeamsKernel : removeSeamsKernel;
            seamCarvingShader.SetTexture(kernel, "InputTexture", sourceTex);
            seamCarvingShader.SetTexture(kernel, "OutputTexture", resultTex);
            seamCarvingShader.SetBuffer(kernel, "Seams", seamBuffer);
            seamCarvingShader.SetInt("Width", width);
            seamCarvingShader.SetInt("Height", height);
            seamCarvingShader.SetInt("NumSeams", seamsInThisBatch);

            seamCarvingShader.Dispatch(kernel, Mathf.CeilToInt(newWidth / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

            seamBuffer.Release();

            if (!isVertical)
            {
                ReleaseRenderTexture(sourceTex); // release transposed source
                currentProcessedTexture = TransposeTexture(resultTex);
                ReleaseRenderTexture(resultTex); // release intermediate result
            }
            else
            {
                currentProcessedTexture = resultTex;
            }

            seamsProcessed += seamsInThisBatch;
        }
    }

    // --- GPU INTERACTION & CPU ALGORITHM (MOSTLY UNCHANGED) ---
    // NOTE: TransposeTexture is still a slow CPU implementation. For max performance, it should be a compute shader.
    private float[,] CalculateEnergyMapGPU(RenderTexture source)
    {
        int width = source.width;
        int height = source.height;
        RenderTexture energyMapRT = CreateRenderTexture(width, height, RenderTextureFormat.RFloat);

        seamCarvingShader.SetTexture(energyKernel, "SourceTexture", source);
        seamCarvingShader.SetTexture(energyKernel, "EnergyMap", energyMapRT);
        seamCarvingShader.SetInt("Width", width);
        seamCarvingShader.SetInt("Height", height);
        seamCarvingShader.Dispatch(energyKernel, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

        Texture2D tempTex = new Texture2D(width, height, TextureFormat.RFloat, false);
        RenderTexture.active = energyMapRT;
        tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;

        Color[] pixels = tempTex.GetPixels();
        float[,] energyMap = new float[width, height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) energyMap[x, y] = pixels[y * width + x].r;

        Destroy(tempTex);
        ReleaseRenderTexture(energyMapRT);
        return energyMap;
    }

    private int[] FindVerticalSeamCPU(float[,] energyMap, int width, int height)
    {
        float[,] M = new float[width, height];
        int[,] backtrack = new int[width, height];
        for (int x = 0; x < width; x++) M[x, 0] = energyMap[x, 0];
        for (int y = 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float minPrevEnergy = M[x, y - 1];
                int path = 0;
                if (x > 0 && M[x - 1, y - 1] < minPrevEnergy) { minPrevEnergy = M[x - 1, y - 1]; path = -1; }
                if (x < width - 1 && M[x + 1, y - 1] < minPrevEnergy) { minPrevEnergy = M[x + 1, y - 1]; path = 1; }
                M[x, y] = energyMap[x, y] + minPrevEnergy;
                backtrack[x, y] = path;
            }
        }
        float minEnergy = float.MaxValue;
        int endX = 0;
        for (int x = 0; x < width; x++) if (M[x, height - 1] < minEnergy) { minEnergy = M[x, height - 1]; endX = x; }
        int[] seam = new int[height];
        seam[height - 1] = endX;
        for (int y = height - 1; y > 0; y--) { endX += backtrack[endX, y]; seam[y - 1] = endX; }
        return seam;
    }

    private RenderTexture TransposeTexture(RenderTexture source)
    {
        int width = source.width;
        int height = source.height;
        RenderTexture transposed = CreateRenderTexture(height, width);
        Texture2D tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        RenderTexture.active = source;
        tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        RenderTexture.active = null;
        Color32[] pixels = tempTex.GetPixels32();
        Color32[] newPixels = new Color32[pixels.Length];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) newPixels[x * height + y] = pixels[y * width + x];
        Texture2D newTempTex = new Texture2D(height, width);
        newTempTex.SetPixels32(newPixels);
        newTempTex.Apply();
        Graphics.Blit(newTempTex, transposed);
        Destroy(tempTex);
        Destroy(newTempTex);
        return transposed;
    }

    // --- UTILITY FUNCTIONS ---

    /// <summary>
    /// Creates a CPU-readable copy of a texture. This is necessary because the source texture
    /// might be non-readable on the CPU, and we need a clean copy to reset to.
    /// </summary>
    private Texture2D CreateReadableTextureCopy(Texture2D source)
    {
        // Create a temporary RenderTexture with the same dimensions as the source.
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear
        );

        // Copy the source texture data to the RenderTexture.
        Graphics.Blit(source, rt);

        // Backup the currently active RenderTexture.
        RenderTexture previous = RenderTexture.active;
        // Set our temporary RenderTexture as the active one.
        RenderTexture.active = rt;

        // Create a new Texture2D to hold the pixel data from the GPU.
        Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);

        // Read the pixels from the active RenderTexture into our new CPU texture.
        readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTexture.Apply(); // Apply the pixel changes.

        // Restore the previously active RenderTexture.
        RenderTexture.active = previous;
        // Release the temporary RenderTexture from memory.
        RenderTexture.ReleaseTemporary(rt);

        return readableTexture;
    }

    /// <summary>
    /// Creates a new RenderTexture, which is a texture that lives on the GPU.
    /// It's configured to be writable by compute shaders.
    /// </summary>
    private RenderTexture CreateRenderTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.Default)
    {
        RenderTexture rt = new RenderTexture(width, height, 24, format);
        // This flag is crucial! It allows a compute shader to write data to this texture.
        rt.enableRandomWrite = true;
        rt.Create(); // Actually create the GPU resource.
        return rt;
    }

    /// <summary>
    /// Properly releases the memory used by a RenderTexture.
    /// </summary>
    private void ReleaseRenderTexture(RenderTexture rt)
    {
        if (rt != null)
        {
            rt.Release(); // Release the GPU resource.
            Destroy(rt);  // Destroy the Unity engine object wrapper.
        }
    }

    /// <summary>
    /// Unity message called when the GameObject is destroyed.
    /// Used for cleanup to prevent memory leaks.
    /// </summary>
    void OnDestroy()
    {
        // Clean up the main RenderTexture and the original texture copy when the object is destroyed.
        ReleaseRenderTexture(currentProcessedTexture);
        if (originalTexture != null)
        {
            Destroy(originalTexture);
        }
    }
}