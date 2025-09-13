using UnityEngine;
using System.Collections.Generic;

public class spriteManager : MonoBehaviour
{
    private Camera mainCamera;
    private Transform selectedSprite;
    private Vector3 offset;
    private float initialDistance;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        HandleSpriteInteraction();
    }

    private void HandleSpriteInteraction()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction);

            if (hit.collider != null)
            {
                selectedSprite = hit.collider.transform;
                offset = selectedSprite.position - mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCamera.nearClipPlane));
            }
        }

        if (Input.GetMouseButton(0) && selectedSprite != null)
        {
            Vector3 mousePosition = mainCamera.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, mainCamera.nearClipPlane));
            selectedSprite.position = mousePosition + offset;
        }

        if (selectedSprite != null)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                SpriteRenderer renderer = selectedSprite.GetComponent<SpriteRenderer>();
                if (renderer != null)
                {
                    Sprite sprite = renderer.sprite;
                    Texture2D texture = sprite.texture; // Zak³adamy, ¿e readable

                    Vector3 oldScale = selectedSprite.localScale;
                    int oldWidth = texture.width;
                    int oldHeight = texture.height;

                    float[,] energy = CalculateEnergyMap(texture);

                    if (!Input.GetKey(KeyCode.X) && !Input.GetKey(KeyCode.Y))
                    {
                        // Uniform: Na przemian pionowy i poziomy seam dla balansu
                        if (scroll > 0)
                        {
                            // Powiêksz: wstaw
                            float[,] cumulativeV = CalculateCumulativeEnergy(energy, oldWidth, oldHeight);
                            int[] seamV = FindVerticalSeam(cumulativeV, oldWidth, oldHeight);
                            texture = InsertVerticalSeam(texture, seamV);

                            float[,] cumulativeH = CalculateCumulativeEnergyHorizontal(energy, oldWidth, oldHeight); // U¿yj starej energy dla prostoty
                            int[] seamH = FindHorizontalSeam(cumulativeH, oldWidth, oldHeight);
                            texture = InsertHorizontalSeam(texture, seamH);
                        }
                        else
                        {
                            // Zmniejsz: usuñ
                            float[,] cumulativeV = CalculateCumulativeEnergy(energy, oldWidth, oldHeight);
                            int[] seamV = FindVerticalSeam(cumulativeV, oldWidth, oldHeight);
                            texture = RemoveVerticalSeam(texture, seamV);

                            float[,] cumulativeH = CalculateCumulativeEnergyHorizontal(energy, oldWidth, oldHeight);
                            int[] seamH = FindHorizontalSeam(cumulativeH, oldWidth, oldHeight);
                            texture = RemoveHorizontalSeam(texture, seamH);
                        }

                        // Utwórz nowy sprite z poprawnym rect
                        Rect newRect = new Rect(0, 0, texture.width, texture.height);
                        Vector2 newPivot = new Vector2(0.5f, 0.5f);
                        Sprite newSprite = Sprite.Create(texture, newRect, newPivot, sprite.pixelsPerUnit);
                        renderer.sprite = newSprite;

                        // Dostosuj scale by zachowaæ wizualny rozmiar (opcjonalne, jeœli chcesz zachowaæ fizyczny rozmiar)
                        // selectedSprite.localScale = new Vector3(oldScale.x * (texture.width / (float)oldWidth), oldScale.y * (texture.height / (float)oldHeight), oldScale.z);
                    }
                    else if (Input.GetKey(KeyCode.X))
                    {
                        // Tylko X (pionowe seamy)
                        float[,] cumulative = CalculateCumulativeEnergy(energy, oldWidth, oldHeight);
                        int[] seam = FindVerticalSeam(cumulative, oldWidth, oldHeight);
                        if (scroll > 0)
                        {
                            texture = InsertVerticalSeam(texture, seam);
                        }
                        else
                        {
                            texture = RemoveVerticalSeam(texture, seam);
                        }
                        Rect newRect = new Rect(0, 0, texture.width, texture.height);
                        Vector2 newPivot = new Vector2(0.5f, 0.5f);
                        Sprite newSprite = Sprite.Create(texture, newRect, newPivot, sprite.pixelsPerUnit);
                        renderer.sprite = newSprite;
                        // selectedSprite.localScale = new Vector3(oldScale.x * (texture.width / (float)oldWidth), oldScale.y, oldScale.z);
                    }
                    else if (Input.GetKey(KeyCode.Y))
                    {
                        // Tylko Y (poziome seamy)
                        float[,] cumulative = CalculateCumulativeEnergyHorizontal(energy, oldWidth, oldHeight);
                        int[] seam = FindHorizontalSeam(cumulative, oldWidth, oldHeight);
                        if (scroll > 0)
                        {
                            texture = InsertHorizontalSeam(texture, seam);
                        }
                        else
                        {
                            texture = RemoveHorizontalSeam(texture, seam);
                        }
                        Rect newRect = new Rect(0, 0, texture.width, texture.height);
                        Vector2 newPivot = new Vector2(0.5f, 0.5f);
                        Sprite newSprite = Sprite.Create(texture, newRect, newPivot, sprite.pixelsPerUnit);
                        renderer.sprite = newSprite;
                        // selectedSprite.localScale = new Vector3(oldScale.x, oldScale.y * (texture.height / (float)oldHeight), oldScale.z);
                    }
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            selectedSprite = null;
        }
    }

    private float[,] CalculateEnergyMap(Texture2D texture)
    {
        int width = texture.width;
        int height = texture.height;
        float[,] energy = new float[width, height];
        Color[] pixels = texture.GetPixels();

        // Konwersja na grayscale dla obliczeñ gradientu
        float[,] gray = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = pixels[y * width + x];
                gray[x, y] = color.grayscale; // Unity's grayscale: 0.3R + 0.59G + 0.11B
            }
        }

        // Oblicz gradienty Sobel
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float gx = gray[x + 1, y - 1] + 2 * gray[x + 1, y] + gray[x + 1, y + 1]
                         - gray[x - 1, y - 1] - 2 * gray[x - 1, y] - gray[x - 1, y + 1];
                float gy = gray[x - 1, y + 1] + 2 * gray[x, y + 1] + gray[x + 1, y + 1]
                         - gray[x - 1, y - 1] - 2 * gray[x, y - 1] - gray[x + 1, y - 1];
                energy[x, y] = Mathf.Sqrt(gx * gx + gy * gy);
            }
        }

        // Obs³uga krawêdzi (proste kopiowanie energii z s¹siadów)
        for (int x = 0; x < width; x++)
        {
            energy[x, 0] = energy[x, 1];
            energy[x, height - 1] = energy[x, height - 2];
        }
        for (int y = 0; y < height; y++)
        {
            energy[0, y] = energy[1, y];
            energy[width - 1, y] = energy[width - 2, y];
        }

        return energy;
    }

    private float[,] CalculateCumulativeEnergy(float[,] energy, int width, int height)
    {
        float[,] cumulative = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            cumulative[x, 0] = energy[x, 0];
        }

        for (int y = 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float minAbove = cumulative[x, y - 1];
                if (x > 0) minAbove = Mathf.Min(minAbove, cumulative[x - 1, y - 1]);
                if (x < width - 1) minAbove = Mathf.Min(minAbove, cumulative[x + 1, y - 1]);
                cumulative[x, y] = energy[x, y] + minAbove;
            }
        }
        return cumulative;
    }

    private int[] FindVerticalSeam(float[,] cumulative, int width, int height)
    {
        int[] seam = new int[height];

        // ZnajdŸ min na dole
        float minEnergy = float.MaxValue;
        int minX = 0;
        for (int x = 0; x < width; x++)
        {
            if (cumulative[x, height - 1] < minEnergy)
            {
                minEnergy = cumulative[x, height - 1];
                minX = x;
            }
        }
        seam[height - 1] = minX;

        // Backtrack w górê
        for (int y = height - 2; y >= 0; y--)
        {
            int currentX = seam[y + 1];
            float minAbove = cumulative[currentX, y];
            int bestX = currentX;

            if (currentX > 0 && cumulative[currentX - 1, y] < minAbove)
            {
                minAbove = cumulative[currentX - 1, y];
                bestX = currentX - 1;
            }
            if (currentX < width - 1 && cumulative[currentX + 1, y] < minAbove)
            {
                bestX = currentX + 1;
            }
            seam[y] = bestX;
        }
        return seam;
    }

    private Texture2D RemoveVerticalSeam(Texture2D texture, int[] seam)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        Color[] newPixels = new Color[(width - 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = seam[y];
            int offset = y * width;
            int newOffset = y * (width - 1);

            // Kopiuj lew¹ stronê
            System.Array.Copy(pixels, offset, newPixels, newOffset, seamX);
            // Kopiuj praw¹ stronê
            System.Array.Copy(pixels, offset + seamX + 1, newPixels, newOffset + seamX, width - seamX - 1);
        }

        Texture2D newTexture = new Texture2D(width - 1, height);
        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertVerticalSeam(Texture2D texture, int[] seam)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        Color[] newPixels = new Color[(width + 1) * height];

        for (int y = 0; y < height; y++)
        {
            int seamX = seam[y];
            int offset = y * width;
            int newOffset = y * (width + 1);

            // Kopiuj lew¹ stronê
            System.Array.Copy(pixels, offset, newPixels, newOffset, seamX + 1); // +1 by wstawiæ duplikat
            // Duplikuj seam piksel
            newPixels[newOffset + seamX + 1] = pixels[offset + seamX];
            // Kopiuj praw¹ stronê
            System.Array.Copy(pixels, offset + seamX + 1, newPixels, newOffset + seamX + 2, width - seamX - 1);
        }

        Texture2D newTexture = new Texture2D(width + 1, height);
        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private float[,] CalculateCumulativeEnergyHorizontal(float[,] energy, int width, int height)
    {
        float[,] cumulative = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            cumulative[0, y] = energy[0, y];
        }

        for (int x = 1; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float minLeft = cumulative[x - 1, y];
                if (y > 0) minLeft = Mathf.Min(minLeft, cumulative[x - 1, y - 1]);
                if (y < height - 1) minLeft = Mathf.Min(minLeft, cumulative[x - 1, y + 1]);
                cumulative[x, y] = energy[x, y] + minLeft;
            }
        }
        return cumulative;
    }

    private int[] FindHorizontalSeam(float[,] cumulative, int width, int height)
    {
        int[] seam = new int[width];

        // Min na prawo
        float minEnergy = float.MaxValue;
        int minY = 0;
        for (int y = 0; y < height; y++)
        {
            if (cumulative[width - 1, y] < minEnergy)
            {
                minEnergy = cumulative[width - 1, y];
                minY = y;
            }
        }
        seam[width - 1] = minY;

        // Backtrack w lewo
        for (int x = width - 2; x >= 0; x--)
        {
            int currentY = seam[x + 1];
            float minLeft = cumulative[x, currentY];
            int bestY = currentY;

            if (currentY > 0 && cumulative[x, currentY - 1] < minLeft)
            {
                minLeft = cumulative[x, currentY - 1];
                bestY = currentY - 1;
            }
            if (currentY < height - 1 && cumulative[x, currentY + 1] < minLeft)
            {
                bestY = currentY + 1;
            }
            seam[x] = bestY;
        }
        return seam;
    }

    private Texture2D RemoveHorizontalSeam(Texture2D texture, int[] seam)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        Color[] newPixels = new Color[width * (height - 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = seam[x];
            int newOffset = x * (height - 1);
            for (int y = 0; y < seamY; y++)
            {
                newPixels[newOffset + y] = pixels[y * width + x];
            }
            for (int y = seamY; y < height - 1; y++)
            {
                newPixels[newOffset + y] = pixels[(y + 1) * width + x];
            }
        }

        Texture2D newTexture = new Texture2D(width, height - 1);
        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }

    private Texture2D InsertHorizontalSeam(Texture2D texture, int[] seam)
    {
        int width = texture.width;
        int height = texture.height;
        Color[] pixels = texture.GetPixels();
        Color[] newPixels = new Color[width * (height + 1)];

        for (int x = 0; x < width; x++)
        {
            int seamY = seam[x];
            int newOffset = x * (height + 1);
            for (int y = 0; y <= seamY; y++)
            {
                newPixels[newOffset + y] = pixels[y * width + x];
            }
            newPixels[newOffset + seamY + 1] = pixels[seamY * width + x]; // Duplikat
            for (int y = seamY + 1; y < height; y++)
            {
                newPixels[newOffset + y + 1] = pixels[y * width + x];
            }
        }

        Texture2D newTexture = new Texture2D(width, height + 1);
        newTexture.SetPixels(newPixels);
        newTexture.Apply();
        return newTexture;
    }
}
