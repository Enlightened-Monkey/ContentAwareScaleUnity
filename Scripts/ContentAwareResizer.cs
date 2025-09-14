using UnityEngine;
using System.Collections;

/// <summary>
/// Content-Aware Image Resizer using Seam Carving.
/// Resizes the texture based on the GameObject's scale.
/// </summary>
public class ContentAwareResizer : MonoBehaviour
{
    public Texture2D sourceTexture;
    public Renderer outputRenderer;
    [Tooltip("Delay in seconds after scaling stops before the resize is applied.")]
    public float resizeDelay = 0.5f;

    public Texture2D currentProcessedTexture { get; private set; }
    private Texture2D originalTexture; // Store the pristine original texture
    private int originalWidth;
    private int originalHeight;
    private Vector3 lastScale;
    private Coroutine resizeCoroutine;
    private Transform targetTransform; // The transform to monitor for scale changes

    void Start()
    {
        if (sourceTexture != null)
        {
            // Store a clean, readable copy of the original texture and its dimensions
            originalTexture = CreateReadableTextureCopy(sourceTexture);
            originalWidth = sourceTexture.width;
            originalHeight = sourceTexture.height;
            currentProcessedTexture = CreateReadableTextureCopy(originalTexture); // Start with a copy

            if (outputRenderer != null)
            {
                UpdateTexture(currentProcessedTexture);
                targetTransform = outputRenderer.transform; // Use the renderer's transform
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
        // Do nothing if there is no target to monitor
        if (targetTransform == null) return;

        // Check if the object's scale has changed
        if (targetTransform.localScale != lastScale)
        {
            lastScale = targetTransform.localScale;

            // Use a coroutine with a delay (debouncing) to avoid resizing on every frame
            if (resizeCoroutine != null)
            {
                StopCoroutine(resizeCoroutine);
            }
            resizeCoroutine = StartCoroutine(DelayedResize());
        }
    }

    /// <summary>
    /// Waits for a short period after scaling stops, then triggers the resize.
    /// </summary>
    private IEnumerator DelayedResize()
    {
        yield return new WaitForSeconds(resizeDelay);

        // Calculate new target dimensions based on the object's scale relative to its original size
        int newWidth = Mathf.RoundToInt(originalWidth * targetTransform.localScale.x);
        int newHeight = Mathf.RoundToInt(originalHeight * targetTransform.localScale.y);

        // Clamp values to prevent invalid texture dimensions
        newWidth = Mathf.Max(2, newWidth);
        newHeight = Mathf.Max(2, newHeight);

        Debug.Log($"Scale changed. Resizing to {newWidth}x{newHeight}");
        ResizeImage(newWidth, newHeight);
    }

    /// <summary>
    /// Resizes the image to the specified target dimensions.
    /// </summary>
    public void ResizeImage(int targetWidth, int targetHeight)
    {
        if (originalTexture == null)
        {
            Debug.LogError("Source Texture is not assigned.");
            return;
        }

        Debug.Log($"Starting resize from {currentProcessedTexture.width}x{currentProcessedTexture.height} to {targetWidth}x{targetHeight}...");

        // Always start from a fresh copy of the original texture for best quality
        currentProcessedTexture = CreateReadableTextureCopy(originalTexture);

        int widthDifference = currentProcessedTexture.width - targetWidth;
        int heightDifference = currentProcessedTexture.height - targetHeight;

        // --- Width Resizing ---
        if (widthDifference > 0) // Reduce width
        {
            for (int i = 0; i < widthDifference; i++)
            {
                currentProcessedTexture = RemoveVerticalSeam(currentProcessedTexture);
                if ((i + 1) % 10 == 0 || i == widthDifference - 1)
                    Debug.Log($"Removed vertical seam {i + 1}/{widthDifference}");
            }
        }
        else if (widthDifference < 0) // Increase width
        {
            int total = -widthDifference;
            for (int i = 0; i < total; i++)
            {
                currentProcessedTexture = InsertVerticalSeam(currentProcessedTexture);
                if ((i + 1) % 10 == 0 || i == total - 1)
                    Debug.Log($"Inserted vertical seam {i + 1}/{total}");
            }
        }

        // --- Height Resizing ---
        if (heightDifference > 0) // Reduce height
        {
            for (int i = 0; i < heightDifference; i++)
            {
                currentProcessedTexture = RemoveHorizontalSeam(currentProcessedTexture);
                if ((i + 1) % 10 == 0 || i == heightDifference - 1)
                    Debug.Log($"Removed horizontal seam {i + 1}/{heightDifference}");
            }
        }
        else if (heightDifference < 0) // Increase height
        {
            int total = -heightDifference;
            for (int i = 0; i < total; i++)
            {
                currentProcessedTexture = InsertHorizontalSeam(currentProcessedTexture);
                if ((i + 1) % 10 == 0 || i == total - 1)
                    Debug.Log($"Inserted horizontal seam {i + 1}/{total}");
            }
        }

        if (outputRenderer != null)
        {
            UpdateTexture(currentProcessedTexture);
        }
        Debug.Log($"Finished resize. Final dimensions: {currentProcessedTexture.width}x{currentProcessedTexture.height}");
    }

    public void ResetToOriginal()
    {
        if (originalTexture != null && targetTransform != null)
        {
            currentProcessedTexture = CreateReadableTextureCopy(originalTexture);
            targetTransform.localScale = Vector3.one; // Reset scale as well
            lastScale = Vector3.one;
            UpdateTexture(currentProcessedTexture);
        }
    }

    private void UpdateTexture(Texture2D texture)
    {
        if (outputRenderer != null && texture != null)
        {
            outputRenderer.material.mainTexture = texture;
        }
    }

    private Texture2D CreateReadableTextureCopy(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear
        );
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

    private Texture2D RemoveVerticalSeam(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);
        int[] seam = FindVerticalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width - 1, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[(width - 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = seam[y];
            int newX = 0;
            for (int x = 0; x < width; x++)
            {
                if (x == seamX) continue;
                newPixels[y * (width - 1) + newX] = pixels[y * width + x];
                newX++;
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D RemoveHorizontalSeam(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);
        int[] seam = FindHorizontalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width, height - 1, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * (height - 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = seam[x];
            int newY = 0;
            for (int y = 0; y < height; y++)
            {
                if (y == seamY) continue;
                newPixels[newY * width + x] = pixels[y * width + x];
                newY++;
            }
        }

        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertVerticalSeam(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);
        int[] seam = FindVerticalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width + 1, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[(width + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = seam[y];
            int newX = 0;
            for (int x = 0; x < width; x++)
            {
                newPixels[y * (width + 1) + newX] = pixels[y * width + x];
                newX++;
                if (x == seamX)
                {
                    Color left = pixels[y * width + x];
                    Color right = (x + 1 < width) ? pixels[y * width + x + 1] : left;
                    newPixels[y * (width + 1) + newX] = Color.Lerp(left, right, 0.5f);
                    newX++;
                }
            }
        }
        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertHorizontalSeam(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        float[,] energyMap = CalculateEnergyMap(pixels, width, height);
        int[] seam = FindHorizontalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width, height + 1, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * (height + 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = seam[x];
            int newY = 0;
            for (int y = 0; y < height; y++)
            {
                newPixels[newY * width + x] = pixels[y * width + x];
                newY++;
                if (y == seamY)
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

    private float[,] CalculateEnergyMap(Color[] pixels, int width, int height)
    {
        float[,] energyMap = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    energyMap[x, y] = 1000f;
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