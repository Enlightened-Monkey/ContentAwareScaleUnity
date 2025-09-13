using UnityEngine;
using System.Collections.Generic;

public class ContentAwareResizer : MonoBehaviour
{
    public Texture2D sourceTexture;
    public int targetWidth;
    public int targetHeight;
    public Renderer outputRenderer;
    
    [Header("Interactive Controls")]
    public bool enableInteractiveMode = true;
    public bool showSeams = false;
    public Color seamColor = Color.red;
    
    private Camera mainCamera;
    private bool isSelected = false;
    private Vector3 offset;
    private Texture2D currentProcessedTexture;
    private int[] lastVerticalSeam;
    private int[] lastHorizontalSeam;

    void Start()
    {
        mainCamera = Camera.main;
        if (sourceTexture != null)
        {
            currentProcessedTexture = CreateReadableTextureCopy(sourceTexture);
        }
    }
    
    void Update()
    {
        if (enableInteractiveMode)
        {
            HandleInteractiveMode();
        }
    }
    
    private void HandleInteractiveMode()
    {
        // Handle mouse selection
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit) && hit.collider.gameObject == gameObject)
            {
                isSelected = true;
                offset = transform.position - mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCamera.nearClipPlane));
            }
        }

        // Handle dragging
        if (Input.GetMouseButton(0) && isSelected)
        {
            Vector3 mousePosition = mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCamera.nearClipPlane));
            transform.position = mousePosition + offset;
        }

        // Handle scaling with scroll wheel
        if (isSelected && currentProcessedTexture != null)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                ProcessInteractiveScaling(scroll);
            }
        }

        // Handle mouse release
        if (Input.GetMouseButtonUp(0))
        {
            isSelected = false;
        }
        
        // Toggle seam display
        if (Input.GetKeyDown(KeyCode.S))
        {
            showSeams = !showSeams;
            if (showSeams && currentProcessedTexture != null)
            {
                ShowSeamVisualization();
            }
            else if (currentProcessedTexture != null)
            {
                UpdateTexture(currentProcessedTexture);
            }
        }
    }
    
    private void ProcessInteractiveScaling(float scroll)
    {
        if (currentProcessedTexture == null) return;
        
        int width = currentProcessedTexture.width;
        int height = currentProcessedTexture.height;
        Color[] pixels = currentProcessedTexture.GetPixels();

        float[,] energyMap = CalculateEnergyMap(pixels, width, height);

        if (!Input.GetKey(KeyCode.X) && !Input.GetKey(KeyCode.Y))
        {
            // Uniform scaling: both X and Y
            ProcessUniformScaling(scroll, energyMap, width, height, pixels);
        }
        else if (Input.GetKey(KeyCode.X))
        {
            // X-axis only (vertical seams)
            ProcessVerticalScaling(scroll, energyMap, width, height, pixels);
        }
        else if (Input.GetKey(KeyCode.Y))
        {
            // Y-axis only (horizontal seams)
            ProcessHorizontalScaling(scroll, energyMap, width, height, pixels);
        }
        
        if (showSeams)
        {
            ShowSeamVisualization();
        }
        else
        {
            UpdateTexture(currentProcessedTexture);
        }
    }
    
    private void ProcessUniformScaling(float scroll, float[,] energyMap, int width, int height, Color[] pixels)
    {
        if (scroll > 0)
        {
            // Enlarge: add seams
            lastVerticalSeam = FindVerticalSeam(energyMap, width, height);
            currentProcessedTexture = InsertVerticalSeam(currentProcessedTexture);
            
            // Recalculate for horizontal seam
            width = currentProcessedTexture.width;
            height = currentProcessedTexture.height;
            pixels = currentProcessedTexture.GetPixels();
            energyMap = CalculateEnergyMap(pixels, width, height);
            
            lastHorizontalSeam = FindHorizontalSeam(energyMap, width, height);
            currentProcessedTexture = InsertHorizontalSeam(currentProcessedTexture);
        }
        else
        {
            // Shrink: remove seams
            lastVerticalSeam = FindVerticalSeam(energyMap, width, height);
            currentProcessedTexture = RemoveVerticalSeam(currentProcessedTexture);
            
            // Recalculate for horizontal seam
            width = currentProcessedTexture.width;
            height = currentProcessedTexture.height;
            pixels = currentProcessedTexture.GetPixels();
            energyMap = CalculateEnergyMap(pixels, width, height);
            
            lastHorizontalSeam = FindHorizontalSeam(energyMap, width, height);
            currentProcessedTexture = RemoveHorizontalSeam(currentProcessedTexture);
        }
    }
    
    private void ProcessVerticalScaling(float scroll, float[,] energyMap, int width, int height, Color[] pixels)
    {
        lastVerticalSeam = FindVerticalSeam(energyMap, width, height);
        
        if (scroll > 0)
        {
            currentProcessedTexture = InsertVerticalSeam(currentProcessedTexture);
        }
        else
        {
            currentProcessedTexture = RemoveVerticalSeam(currentProcessedTexture);
        }
    }
    
    private void ProcessHorizontalScaling(float scroll, float[,] energyMap, int width, int height, Color[] pixels)
    {
        lastHorizontalSeam = FindHorizontalSeam(energyMap, width, height);
        
        if (scroll > 0)
        {
            currentProcessedTexture = InsertHorizontalSeam(currentProcessedTexture);
        }
        else
        {
            currentProcessedTexture = RemoveHorizontalSeam(currentProcessedTexture);
        }
    }
    
    private void ShowSeamVisualization()
    {
        if (currentProcessedTexture == null) return;
        
        Texture2D seamTexture = new Texture2D(currentProcessedTexture.width, currentProcessedTexture.height, TextureFormat.RGBA32, false);
        Color[] pixels = currentProcessedTexture.GetPixels();
        Color[] seamPixels = new Color[pixels.Length];
        
        // Copy original pixels
        System.Array.Copy(pixels, seamPixels, pixels.Length);
        
        // Highlight seams
        if (lastVerticalSeam != null)
        {
            for (int y = 0; y < lastVerticalSeam.Length; y++)
            {
                int x = lastVerticalSeam[y];
                if (x >= 0 && x < currentProcessedTexture.width && y >= 0 && y < currentProcessedTexture.height)
                {
                    seamPixels[y * currentProcessedTexture.width + x] = seamColor;
                }
            }
        }
        
        if (lastHorizontalSeam != null)
        {
            for (int x = 0; x < lastHorizontalSeam.Length; x++)
            {
                int y = lastHorizontalSeam[x];
                if (x >= 0 && x < currentProcessedTexture.width && y >= 0 && y < currentProcessedTexture.height)
                {
                    seamPixels[y * currentProcessedTexture.width + x] = seamColor;
                }
            }
        }
        
        seamTexture.SetPixels(seamPixels);
        seamTexture.Apply();
        
        UpdateTexture(seamTexture);
    }
    
    private void UpdateTexture(Texture2D texture)
    {
        if (outputRenderer != null && texture != null)
        {
            outputRenderer.material.mainTexture = texture;
        }
    }
    
    [ContextMenu("Toggle Seam Display")]
    public void ToggleSeamDisplay()
    {
        showSeams = !showSeams;
        if (currentProcessedTexture != null)
        {
            if (showSeams)
            {
                ShowSeamVisualization();
            }
            else
            {
                UpdateTexture(currentProcessedTexture);
            }
        }
    }
    
    void OnGUI()
    {
        if (enableInteractiveMode)
        {
            // Create a simple button in the top-left corner
            if (GUI.Button(new Rect(10, 10, 120, 25), showSeams ? "Hide Seams" : "Show Seams"))
            {
                ToggleSeamDisplay();
            }
            
            // Display instructions
            GUI.Label(new Rect(10, 40, 300, 20), "Click and drag to move, scroll to scale");
            GUI.Label(new Rect(10, 60, 300, 20), "Hold X for X-axis only, Y for Y-axis only");
            GUI.Label(new Rect(10, 80, 300, 20), "Press S to toggle seam display");
        }
    }

    /// <summary>
    /// Creates a readable copy of a texture, which is necessary for GetPixels() to work
    /// if the original texture is not marked as "Read/Write Enabled".
    /// </summary>
    private Texture2D CreateReadableTextureCopy(Texture2D source)
    {
        // 1. Create a temporary RenderTexture with the same dimensions and format.
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.Default,
            RenderTextureReadWrite.Linear
        );

        // 2. Blit the source texture to the RenderTexture.
        Graphics.Blit(source, rt);

        // 3. Backup the currently active RenderTexture.
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        // 4. Create a new readable Texture2D to store the pixels.
        // Use an uncompressed format like RGBA32 that supports SetPixels/GetPixels.
        Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);

        // 5. Read the pixels from the active RenderTexture into the new Texture2D.
        readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTexture.Apply();

        // 6. Restore the previous active RenderTexture and release the temporary one.
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return readableTexture;
    }

    [ContextMenu("Resize Image")]
    public void ResizeImage()
    {
        if (sourceTexture == null)
        {
            Debug.LogError("Source Texture is not assigned.");
            return;
        }

        // Create a temporary, writable copy of the texture that is guaranteed to be readable.
        Texture2D processedTexture = CreateReadableTextureCopy(sourceTexture);
        currentProcessedTexture = processedTexture;

        int widthDifference = processedTexture.width - targetWidth;
        int heightDifference = processedTexture.height - targetHeight;

        Debug.Log($"Starting resize. Current: {processedTexture.width}x{processedTexture.height}, Target: {targetWidth}x{targetHeight}");

        // --- Width Resizing ---
        if (widthDifference > 0) // Reduce width
        {
            for (int i = 0; i < widthDifference; i++)
            {
                currentProcessedTexture = RemoveVerticalSeam(currentProcessedTexture);
                if (i % 10 == 0) Debug.Log($"Removed vertical seam {i + 1}/{widthDifference}");
            }
        }
        else if (widthDifference < 0) // Increase width
        {
            for (int i = 0; i < -widthDifference; i++)
            {
                currentProcessedTexture = InsertVerticalSeam(currentProcessedTexture);
                if (i % 10 == 0) Debug.Log($"Inserted vertical seam {i + 1}/{-widthDifference}");
            }
        }

        // --- Height Resizing ---
        if (heightDifference > 0) // Reduce height
        {
            for (int i = 0; i < heightDifference; i++)
            {
                currentProcessedTexture = RemoveHorizontalSeam(currentProcessedTexture);
                if (i % 10 == 0) Debug.Log($"Removed horizontal seam {i + 1}/{heightDifference}");
            }
        }
        else if (heightDifference < 0) // Increase height
        {
            for (int i = 0; i < -heightDifference; i++)
            {
                currentProcessedTexture = InsertHorizontalSeam(currentProcessedTexture);
                if (i % 10 == 0) Debug.Log($"Inserted horizontal seam {i + 1}/{-heightDifference}");
            }
        }

        // Display the result
        if (outputRenderer != null)
        {
            Debug.Log($"Finished resize. Final dimensions: {currentProcessedTexture.width}x{currentProcessedTexture.height}");
            UpdateTexture(currentProcessedTexture);
        }
        else
        {
            Debug.LogWarning("Output Renderer is not assigned. Cannot display the result.");
        }
    }

    private Texture2D RemoveVerticalSeam(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();

        float[,] energyMap = CalculateEnergyMap(pixels, width, height);
        lastVerticalSeam = FindVerticalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width - 1, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[(width - 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = lastVerticalSeam[y];
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
        lastHorizontalSeam = FindHorizontalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width, height - 1, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * (height - 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = lastHorizontalSeam[x];
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
        lastVerticalSeam = FindVerticalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width + 1, height, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[(width + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = lastVerticalSeam[y];
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
        lastHorizontalSeam = FindHorizontalSeam(energyMap, width, height);

        Texture2D newTexture = new Texture2D(width, height + 1, TextureFormat.RGBA32, false);
        Color[] newPixels = new Color[width * (height + 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = lastHorizontalSeam[x];
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