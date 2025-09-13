# ContentAwareResizer Setup Instructions

## Quick Setup Guide

### 1. Scene Setup
1. Create a new Scene or open an existing one
2. Ensure you have a Camera in the scene tagged as "MainCamera"

### 2. GameObject Setup
1. Create a new GameObject (e.g., a Quad or Plane)
2. Add a **Collider** component (required for mouse interaction)
   - For Quad: Add a **Box Collider**
   - For Plane: Add a **Mesh Collider** 
3. Ensure the GameObject has a **Renderer** component (should be automatic)

### 3. ContentAwareResizer Configuration
1. Add the **ContentAwareResizer** script to your GameObject
2. Configure the following fields in the Inspector:
   - **Source Texture**: Drag your image texture here
   - **Output Renderer**: Drag the GameObject's Renderer component here
   - **Enable Interactive Mode**: Check this box (enabled by default)
   - **Target Width/Height**: Set desired dimensions for batch resize
   - **Seam Color**: Choose color for seam visualization (default: red)

### 4. Texture Import Settings
**IMPORTANT**: Your source texture must have these import settings:
1. Select your texture in the Project window
2. In the Inspector, check **"Read/Write Enabled"**
3. Click **"Apply"**

### 5. Testing the Setup

#### Interactive Mode Test:
1. Enter Play Mode
2. Click on your GameObject to select it
3. Try scrolling while selected to see seam carving in action
4. Hold X or Y while scrolling for axis-specific scaling
5. Press S or click GUI buttons to toggle seam display

#### Batch Mode Test:
1. Set Target Width/Height in the Inspector
2. Right-click the ContentAwareResizer component
3. Select "Resize Image" from the context menu

### 6. Optional Demo Setup
1. Add the **ContentAwareResizerDemo** script to any GameObject
2. Assign your ContentAwareResizer to the demo script's resizer field
3. Press Space (or configured key) to run demo steps

## Troubleshooting

### Common Issues:
- **No interaction**: Check if GameObject has a Collider
- **Texture errors**: Ensure texture is set to "Read/Write Enabled"
- **No camera**: Make sure Main Camera is tagged correctly
- **Performance**: Large textures (>2048px) may be slow for real-time interaction

### Performance Tips:
- Use smaller textures for interactive mode (512x512 recommended)
- Batch mode can handle larger textures efficiently
- Consider GPU version (GPUContentAwareResizer) for better performance

## Example Scene Hierarchy:
```
Main Camera (tagged "MainCamera")
├── Directional Light  
└── Content Aware Object
    ├── MeshRenderer
    ├── BoxCollider (or MeshCollider)
    └── ContentAwareResizer (script)
        └── ContentAwareResizerDemo (optional)
```