using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Content-Aware Image Resizer using Seam Carving.
/// This optimized version processes seams in batches for significantly improved performance.
/// </summary>
public class ContentAwareResizer : MonoBehaviour
{
    public Texture2D sourceTexture;
    public Renderer outputRenderer;
    [Tooltip("Delay in seconds after scaling stops before the resize is applied.")]
    public float resizeDelay = 0.5f;
    [Tooltip("Number of seams to process in a single batch. Higher values are faster but may reduce quality.")]
    public int seamProcessBatchSize = 10;

    public Texture2D currentProcessedTexture { get; private set; }
    private Texture2D originalTexture;
    private int originalWidth;
    private int originalHeight;
    private Vector3 lastScale;
    private Coroutine resizeCoroutine;
    private Transform targetTransform;

    void Start()
    {
        if (sourceTexture != null)
        {
            originalTexture = CreateReadableTextureCopy(sourceTexture);
            originalWidth = sourceTexture.width;
            originalHeight = sourceTexture.height;
            currentProcessedTexture = CreateReadableTextureCopy(originalTexture);

            if (outputRenderer != null)
            {
                UpdateTexture(currentProcessedTexture);
                targetTransform = outputRenderer.transform;
            }
            else
            {
                Debug.LogWarning("Output Renderer is not assigned. Scale-based resizing will be disabled.");
                targetTransform = null;
            }

            if (targetTransform != null)
            {
                lastScale = targetTransform.localScale;
            }
        }
    }

    void Update()
    {
        if (targetTransform == null) return;

        if (targetTransform.localScale != lastScale)
        {
            lastScale = targetTransform.localScale;

            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
            }
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

        Debug.Log($"Scale changed. Resizing to {newWidth}x{newHeight}");
        ResizeImage(newWidth, newHeight);
    }

    public void ResizeImage(int targetWidth, int targetHeight)
    {
        if (originalTexture == null)
        {
            Debug.LogError("Source Texture is not assigned.");
            return;
        }

        Debug.Log($"Starting resize from {currentProcessedTexture.width}x{currentProcessedTexture.height} to {targetWidth}x{targetHeight}...");

        // Always start from a fresh copy for the best quality.
        currentProcessedTexture = CreateReadableTextureCopy(originalTexture);

        // --- Width Resizing ---
        int widthDifference = currentProcessedTexture.width - targetWidth;
        if (widthDifference > 0)
        {
            currentProcessedTexture = ProcessSeams(currentProcessedTexture, widthDifference, true, false);
        }
        else if (widthDifference < 0)
        {
            currentProcessedTexture = ProcessSeams(currentProcessedTexture, -widthDifference, true, true);
        }

        // --- Height Resizing ---
        int heightDifference = currentProcessedTexture.height - targetHeight;
        if (heightDifference > 0)
        {
            currentProcessedTexture = ProcessSeams(currentProcessedTexture, heightDifference, false, false);
        }
        else if (heightDifference < 0)
        {
            currentProcessedTexture = ProcessSeams(currentProcessedTexture, -heightDifference, false, true);
        }

        if (outputRenderer != null)
        {
            UpdateTexture(currentProcessedTexture);
        }
        Debug.Log($"Finished resize. Final dimensions: {currentProcessedTexture.width}x{currentProcessedTexture.height}");
    }

    /// <summary>
    /// Master function to handle seam processing in batches.
    /// </summary>
    private Texture2D ProcessSeams(Texture2D texture, int totalSeams, bool isVertical, bool toInsert)
    {
        int seamsProcessed = 0;
        while (seamsProcessed < totalSeams)
        {
            int seamsInThisBatch = Mathf.Min(seamProcessBatchSize, totalSeams - seamsProcessed);

            Debug.Log($"Processing batch of {seamsInThisBatch} {(isVertical ? "vertical" : "horizontal")} seams. ({(toInsert ? "Insert" : "Remove")})");

            if (isVertical)
            {
                texture = toInsert ?
                    InsertVerticalSeams(texture, seamsInThisBatch) :
                    RemoveVerticalSeams(texture, seamsInThisBatch);
            }
            else
            {
                texture = toInsert ?
                    InsertHorizontalSeams(texture, seamsInThisBatch) :
                    RemoveHorizontalSeams(texture, seamsInThisBatch);
            }

            seamsProcessed += seamsInThisBatch;
        }
        return texture;
    }

    private Texture2D RemoveVerticalSeams(Texture2D texture, int numSeams)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);

        List<int[]> seams = new List<int[]>();
        for (int i = 0; i < numSeams; i++)
        {
            int[] seam = FindVerticalSeam(energyMap, width, height);
            seams.Add(seam);
            // "Erase" the seam from the energy map to find the next-best one
            for (int y = 0; y < height; y++)
            {
                energyMap[seam[y], y] = float.MaxValue;
            }
        }

        int newWidth = width - numSeams;
        Texture2D newTexture = new Texture2D(newWidth, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[newWidth * height];

        bool[,] seamPixelMap = new bool[width, height];
        foreach (var seam in seams)
        {
            for (int y = 0; y < height; y++)
            {
                seamPixelMap[seam[y], y] = true;
            }
        }

        for (int y = 0; y < height; y++)
        {
            int newX = 0;
            for (int x = 0; x < width; x++)
            {
                if (!seamPixelMap[x, y])
                {
                    newPixels[y * newWidth + newX] = pixels[y * width + x];
                    newX++;
                }
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertVerticalSeams(Texture2D texture, int numSeams)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);

        List<int[]> seams = new List<int[]>();
        for (int i = 0; i < numSeams; i++)
        {
            int[] seam = FindVerticalSeam(energyMap, width, height);
            seams.Add(seam);
            for (int y = 0; y < height; y++)
            {
                energyMap[seam[y], y] = float.MaxValue;
            }
        }

        int newWidth = width + numSeams;
        Texture2D newTexture = new Texture2D(newWidth, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[newWidth * height];

        // Use a list of seam X coordinates for each row
        List<int>[] seamPixelsInRow = new List<int>[height];
        for (int y = 0; y < height; y++) seamPixelsInRow[y] = new List<int>();
        foreach (var seam in seams)
        {
            for (int y = 0; y < height; y++)
            {
                seamPixelsInRow[y].Add(seam[y]);
            }
        }

        for (int y = 0; y < height; y++)
        {
            int newX = 0;
            for (int x = 0; x < width; x++)
            {
                newPixels[y * newWidth + newX] = pixels[y * width + x];
                newX++;

                if (seamPixelsInRow[y].Contains(x))
                {
                    Color left = pixels[y * width + x];
                    Color right = (x + 1 < width) ? pixels[y * width + x + 1] : left;
                    newPixels[y * newWidth + newX] = Color.Lerp(left, right, 0.5f);
                    newX++;
                }
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    // NOTE: Horizontal versions are separate for clarity but could be combined with a transpose operation.
    private Texture2D RemoveHorizontalSeams(Texture2D texture, int numSeams)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);

        List<int[]> seams = new List<int[]>();
        for (int i = 0; i < numSeams; i++)
        {
            int[] seam = FindHorizontalSeam(energyMap, width, height);
            seams.Add(seam);
            for (int x = 0; x < width; x++)
            {
                energyMap[x, seam[x]] = float.MaxValue;
            }
        }

        int newHeight = height - numSeams;
        Texture2D newTexture = new Texture2D(width, newHeight, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * newHeight];

        bool[,] seamPixelMap = new bool[width, height];
        foreach (var seam in seams)
        {
            for (int x = 0; x < width; x++)
            {
                seamPixelMap[x, seam[x]] = true;
            }
        }

        for (int x = 0; x < width; x++)
        {
            int newY = 0;
            for (int y = 0; y < height; y++)
            {
                if (!seamPixelMap[x, y])
                {
                    newPixels[newY * width + x] = pixels[y * width + x];
                    newY++;
                }
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertHorizontalSeams(Texture2D texture, int numSeams)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);

        List<int[]> seams = new List<int[]>();
        for (int i = 0; i < numSeams; i++)
        {
            int[] seam = FindHorizontalSeam(energyMap, width, height);
            seams.Add(seam);
            for (int x = 0; x < width; x++)
            {
                energyMap[x, seam[x]] = float.MaxValue;
            }
        }

        int newHeight = height + numSeams;
        Texture2D newTexture = new Texture2D(width, newHeight, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * newHeight];

        List<int>[] seamPixelsInCol = new List<int>[width];
        for (int x = 0; x < width; x++) seamPixelsInCol[x] = new List<int>();
        foreach (var seam in seams)
        {
            for (int x = 0; x < width; x++)
            {
                seamPixelsInCol[x].Add(seam[x]);
            }
        }

        for (int x = 0; x < width; x++)
        {
            int newY = 0;
            for (int y = 0; y < height; y++)
            {
                newPixels[newY * width + x] = pixels[y * width + x];
                newY++;

                if (seamPixelsInCol[x].Contains(y))
                {
                    Color up = pixels[y * width + x];
                    Color down = (y + 1 < height) ? pixels[(y + 1) * width + x] : up;
                    newPixels[newY * width + x] = Color.Lerp(up, down, 0.5f);
                    newY++;
                }
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    public void ResetToOriginal()
    {
        if (originalTexture != null && targetTransform != null)
        {
            currentProcessedTexture = CreateReadableTextureCopy(originalTexture);
            targetTransform.localScale = Vector3.one;
            lastScale = Vector3.one;
            UpdateTexture(currentProcessedTexture);
        }
    }

    // --- Helper and Core Algorithm Functions (Unchanged from original) ---

    private void UpdateTexture(Texture2D texture)
    {
        if (outputRenderer != null && texture != null)
        {
            // Destroy the old texture before assigning a new one to prevent memory leaks
            if (outputRenderer.material.mainTexture != null && outputRenderer.material.mainTexture != sourceTexture)
            {
                Destroy(outputRenderer.material.mainTexture);
            }
            outputRenderer.material.mainTexture = texture;
        }
    }

    private Texture2D CreateReadableTextureCopy(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTexture.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return readableTexture;
    }

    private float[,] CalculateEnergyMap(Color[] pixels, int width, int height)
    {
        float[,] energyMap = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    energyMap[x, y] = 1000f; // Using a high, but not max, value for the border
                    continue;
                }
                Color p_x_minus_1 = pixels[y * width + (x - 1)];
                Color p_x_plus_1 = pixels[y * width + (x + 1)];
                Color p_y_minus_1 = pixels[(y - 1) * width + x];
                Color p_y_plus_1 = pixels[(y + 1) * width + x];

                float deltaX_sq = Mathf.Pow(p_x_plus_1.r - p_x_minus_1.r, 2) + Mathf.Pow(p_x_plus_1.g - p_x_minus_1.g, 2) + Mathf.Pow(p_x_plus_1.b - p_x_minus_1.b, 2);
                float deltaY_sq = Mathf.Pow(p_y_plus_1.r - p_y_minus_1.r, 2) + Mathf.Pow(p_y_plus_1.g - p_y_minus_1.g, 2) + Mathf.Pow(p_y_plus_1.b - p_y_minus_1.b, 2);

                energyMap[x, y] = Mathf.Sqrt(deltaX_sq + deltaY_sq);
            }
        }
        return energyMap;
    }

    private int[] FindVerticalSeam(float[,] energyMap, int width, int height)
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
                if (x > 0 && M[x - 1, y - 1] < minPrevEnergy)
                {
                    minPrevEnergy = M[x - 1, y - 1];
                    path = -1;
                }
                if (x < width - 1 && M[x + 1, y - 1] < minPrevEnergy)
                {
                    minPrevEnergy = M[x + 1, y - 1];
                    path = 1;
                }
                M[x, y] = energyMap[x, y] + minPrevEnergy;
                backtrack[x, y] = path;
            }
        }

        float minEnergy = float.MaxValue;
        int endX = 0;
        for (int x = 0; x < width; x++)
        {
            if (M[x, height - 1] < minEnergy)
            {
                minEnergy = M[x, height - 1];
                endX = x;
            }
        }

        int[] seam = new int[height];
        seam[height - 1] = endX;
        for (int y = height - 1; y > 0; y--)
        {
            endX += backtrack[endX, y];
            seam[y - 1] = endX;
        }
        return seam;
    }

    private int[] FindHorizontalSeam(float[,] energyMap, int width, int height)
    {
        float[,] M = new float[width, height];
        int[,] backtrack = new int[width, height];

        for (int y = 0; y < height; y++) M[0, y] = energyMap[0, y];

        for (int x = 1; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float minPrevEnergy = M[x - 1, y];
                int path = 0;
                if (y > 0 && M[x - 1, y - 1] < minPrevEnergy)
                {
                    minPrevEnergy = M[x - 1, y - 1];
                    path = -1;
                }
                if (y < height - 1 && M[x - 1, y + 1] < minPrevEnergy)
                {
                    minPrevEnergy = M[x - 1, y + 1];
                    path = 1;
                }
                M[x, y] = energyMap[x, y] + minPrevEnergy;
                backtrack[x, y] = path;
            }
        }

        float minEnergy = float.MaxValue;
        int endY = 0;
        for (int y = 0; y < height; y++)
        {
            if (M[width - 1, y] < minEnergy)
            {
                minEnergy = M[width - 1, y];
                endY = y;
            }
        }

        int[] seam = new int[width];
        seam[width - 1] = endY;
        for (int x = width - 1; x > 0; x--)
        {
            endY += backtrack[x, endY];
            seam[x - 1] = endY;
        }
        return seam;
    }
}