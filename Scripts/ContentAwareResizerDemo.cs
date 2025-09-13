using UnityEngine;

/// <summary>
/// Demo script showing how to use the ContentAwareResizer programmatically
/// </summary>
public class ContentAwareResizerDemo : MonoBehaviour
{
    [Header("Demo Configuration")]
    public ContentAwareResizer resizer;
    public KeyCode demoKey = KeyCode.Space;
    
    [Header("Automated Demo")]
    public bool runAutomatedDemo = false;
    public float demoInterval = 2.0f;
    
    private float lastDemoTime = 0f;
    private int demoStep = 0;
    
    void Update()
    {
        // Manual demo trigger
        if (Input.GetKeyDown(demoKey))
        {
            RunDemo();
        }
        
        // Automated demo
        if (runAutomatedDemo && Time.time - lastDemoTime > demoInterval)
        {
            RunAutomatedDemo();
            lastDemoTime = Time.time;
        }
    }
    
    void RunDemo()
    {
        if (resizer == null)
        {
            Debug.LogWarning("ContentAwareResizer not assigned!");
            return;
        }
        
        Debug.Log("Running ContentAwareResizer demo...");
        
        // Toggle seam display
        resizer.ToggleSeamDisplay();
        
        // Show current texture info
        Debug.Log($"Current texture dimensions: {resizer.currentProcessedTexture?.width}x{resizer.currentProcessedTexture?.height}");
    }
    
    void RunAutomatedDemo()
    {
        if (resizer == null) return;
        
        switch (demoStep % 4)
        {
            case 0:
                Debug.Log("Demo: Showing seams");
                if (!resizer.showSeams) resizer.ToggleSeamDisplay();
                break;
            case 1:
                Debug.Log("Demo: Hiding seams");  
                if (resizer.showSeams) resizer.ToggleSeamDisplay();
                break;
            case 2:
                Debug.Log("Demo: Batch resize to target dimensions");
                resizer.ResizeImage();
                break;
            case 3:
                Debug.Log("Demo: Reset to original");
                resizer.ResetToOriginal();
                break;
        }
        
        demoStep++;
    }
    
    void OnGUI()
    {
        if (resizer != null)
        {
            // Demo controls
            GUI.Label(new Rect(Screen.width - 200, 10, 190, 20), "Demo Controls:");
            GUI.Label(new Rect(Screen.width - 200, 30, 190, 20), $"Press {demoKey} for demo");
            
            if (GUI.Button(new Rect(Screen.width - 200, 50, 100, 25), "Run Demo"))
            {
                RunDemo();
            }
            
            runAutomatedDemo = GUI.Toggle(new Rect(Screen.width - 200, 80, 150, 20), 
                runAutomatedDemo, "Auto Demo");
        }
    }
}