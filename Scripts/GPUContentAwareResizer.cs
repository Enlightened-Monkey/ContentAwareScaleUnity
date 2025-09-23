using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GPU-Accelerated Content-Aware Image Resizer (Pixel Array Version).
/// This version uses pixel arrays for GPU processing to ensure perfect color preservation.
/// All texture operations are handled in C#, while GPU processes raw pixel data.
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

            // Validate that all kernels were found successfully
            if (energyKernel < 0 || removeSeamsKernel < 0 || insertSeamsKernel < 0)
            {
                Debug.LogError("Failed to find one or more compute kernels in the shader!");
                return;
            }

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

        RenderTexture temp = CreateRenderTexture(originalWidth, originalHeight, RenderTextureFormat.ARGB32);
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
            
            // Additional safety: prevent processing more seams than the texture width/height allows
            if (isVertical)
            {
                seamsInThisBatch = Mathf.Min(seamsInThisBatch, currentProcessedTexture.width - 1);
            }
            else
            {
                seamsInThisBatch = Mathf.Min(seamsInThisBatch, currentProcessedTexture.height - 1);
            }
            
            if (seamsInThisBatch <= 0) break;

            // Get pixels from current texture
            Color[] sourcePixels = GetPixelsFromTexture(currentProcessedTexture);
            int width = currentProcessedTexture.width;
            int height = currentProcessedTexture.height;

            // Handle transpose for horizontal seams
            if (!isVertical)
            {
                sourcePixels = TransposePixelArray(sourcePixels, width, height);
                int temp = width;
                width = height;
                height = temp;
            }

            // 1. Calculate energy map using pixel array
            float[,] energyMap = CalculateEnergyMapGPU(sourcePixels, width, height);

            // 2. Find a batch of seams on the CPU
            List<int> allSeamData = new List<int>();
            for (int i = 0; i < seamsInThisBatch; i++)
            {
                int[] seam = FindVerticalSeamCPU(energyMap, width, height);
                
                // Validate seam coordinates
                bool seamValid = true;
                for (int y = 0; y < height; y++)
                {
                    if (seam[y] < 0 || seam[y] >= width)
                    {
                        Debug.LogError($"Invalid seam coordinate: seam[{y}] = {seam[y]}, width = {width}");
                        seamValid = false;
                        break;
                    }
                }
                
                if (seamValid)
                {
                    allSeamData.AddRange(seam);
                    // Erase seam from energy map to find the next best one
                    for (int y = 0; y < height; y++) energyMap[seam[y], y] = float.MaxValue;
                }
                else
                {
                    Debug.LogError("Skipping invalid seam");
                    break;
                }
            }

            // Validate seam data
            if (allSeamData.Count == 0)
            {
                Debug.LogError("No seam data to process!");
                return;
            }

            // Debug information for large changes
            if (seamsInThisBatch > 50)
            {
                Debug.Log($"Processing large batch: {seamsInThisBatch} seams, width: {width}, height: {height}");
            }

            // 3. Process seams using GPU with pixel arrays
            Color[] resultPixels = ProcessSeamsGPU(sourcePixels, width, height, allSeamData, toInsert);
            
            // Calculate new dimensions
            int newWidth = toInsert ? width + seamsInThisBatch : width - seamsInThisBatch;
            newWidth = Mathf.Max(1, newWidth);

            // Handle transpose back for horizontal seams
            if (!isVertical)
            {
                resultPixels = TransposePixelArray(resultPixels, newWidth, height);
                int temp = newWidth;
                newWidth = height;
                height = temp;
            }

            // Convert result back to texture
            ReleaseRenderTexture(currentProcessedTexture);
            currentProcessedTexture = CreateTextureFromPixels(resultPixels, newWidth, height);

            seamsProcessed += seamsInThisBatch;
        }
    }

    // --- GPU INTERACTION WITH PIXEL ARRAYS ---
    private float[,] CalculateEnergyMapGPU(Color[] pixels, int width, int height)
    {
        // Convert Color[] to float4 array for GPU
        Vector4[] pixelData = new Vector4[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixelData[i] = new Vector4(pixels[i].r, pixels[i].g, pixels[i].b, pixels[i].a);
        }

        // Create buffers
        ComputeBuffer inputBuffer = new ComputeBuffer(pixels.Length, sizeof(float) * 4);
        ComputeBuffer energyBuffer = new ComputeBuffer(pixels.Length, sizeof(float));
        
        inputBuffer.SetData(pixelData);

        // Set up compute shader
        seamCarvingShader.SetBuffer(energyKernel, "InputPixels", inputBuffer);
        seamCarvingShader.SetBuffer(energyKernel, "EnergyMap", energyBuffer);
        seamCarvingShader.SetInt("Width", width);
        seamCarvingShader.SetInt("Height", height);
        
        // Dispatch compute shader
        int totalPixels = width * height;
        int threadGroups = Mathf.CeilToInt(totalPixels / 64.0f);
        seamCarvingShader.Dispatch(energyKernel, threadGroups, 1, 1);

        // Get results
        float[] energyData = new float[pixels.Length];
        energyBuffer.GetData(energyData);

        // Convert to 2D array
        float[,] energyMap = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                energyMap[x, y] = energyData[y * width + x];
            }
        }

        // Cleanup
        inputBuffer.Release();
        energyBuffer.Release();

        return energyMap;
    }

    private Color[] ProcessSeamsGPU(Color[] inputPixels, int width, int height, List<int> seamData, bool toInsert)
    {
        int seamCount = seamData.Count / height;
        int newWidth = toInsert ? width + seamCount : width - seamCount;
        int outputSize = newWidth * height;

        // Convert input to Vector4 array
        Vector4[] inputData = new Vector4[inputPixels.Length];
        for (int i = 0; i < inputPixels.Length; i++)
        {
            inputData[i] = new Vector4(inputPixels[i].r, inputPixels[i].g, inputPixels[i].b, inputPixels[i].a);
        }

        // Create buffers
        ComputeBuffer inputBuffer = new ComputeBuffer(inputPixels.Length, sizeof(float) * 4);
        ComputeBuffer outputBuffer = new ComputeBuffer(outputSize, sizeof(float) * 4);
        ComputeBuffer seamBuffer = new ComputeBuffer(seamData.Count, sizeof(int));

        inputBuffer.SetData(inputData);
        seamBuffer.SetData(seamData.ToArray());

        // Select kernel
        int kernel = toInsert ? insertSeamsKernel : removeSeamsKernel;

        // Set up compute shader
        seamCarvingShader.SetBuffer(kernel, "InputPixels", inputBuffer);
        seamCarvingShader.SetBuffer(kernel, "OutputPixels", outputBuffer);
        seamCarvingShader.SetBuffer(kernel, "Seams", seamBuffer);
        seamCarvingShader.SetInt("Width", width);
        seamCarvingShader.SetInt("Height", height);
        seamCarvingShader.SetInt("NumSeams", seamCount);

        // Dispatch
        int threadGroups = Mathf.CeilToInt(outputSize / 64.0f);
        seamCarvingShader.Dispatch(kernel, threadGroups, 1, 1);

        // Get results
        Vector4[] outputData = new Vector4[outputSize];
        outputBuffer.GetData(outputData);

        // Convert back to Color array
        Color[] outputPixels = new Color[outputSize];
        for (int i = 0; i < outputSize; i++)
        {
            outputPixels[i] = new Color(outputData[i].x, outputData[i].y, outputData[i].z, outputData[i].w);
        }

        // Cleanup
        inputBuffer.Release();
        outputBuffer.Release();
        seamBuffer.Release();

        return outputPixels;
    }

    private Color[] GetPixelsFromTexture(RenderTexture texture)
    {
        Texture2D tempTex = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        RenderTexture.active = texture;
        tempTex.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;

        Color[] pixels = tempTex.GetPixels();
        Destroy(tempTex);
        return pixels;
    }

    private RenderTexture CreateTextureFromPixels(Color[] pixels, int width, int height)
    {
        Texture2D tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tempTex.SetPixels(pixels);
        tempTex.Apply();

        RenderTexture result = CreateRenderTexture(width, height, RenderTextureFormat.ARGB32);
        Graphics.Blit(tempTex, result);
        
        Destroy(tempTex);
        return result;
    }

    private Color[] TransposePixelArray(Color[] pixels, int width, int height)
    {
        Color[] transposed = new Color[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                transposed[x * height + y] = pixels[y * width + x];
            }
        }
        return transposed;
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
            RenderTextureFormat.ARGB32, // Use ARGB32 for better color preservation
            RenderTextureReadWrite.sRGB // Use sRGB for proper color space handling
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
        // Use ARGB32 for better color preservation when format is Default
        RenderTextureFormat actualFormat = (format == RenderTextureFormat.Default) ? RenderTextureFormat.ARGB32 : format;
        RenderTexture rt = new RenderTexture(width, height, 0, actualFormat); // Reduced depth buffer from 24 to 0 for color textures
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