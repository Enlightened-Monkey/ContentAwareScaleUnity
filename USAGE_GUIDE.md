# ContentAwareResizer - Usage Guide

## Interactive Features

The ContentAwareResizer now supports interactive scaling with the following features:

### Setup
1. Add the ContentAwareResizer script to a GameObject with a Renderer component
2. Assign a source texture to the `sourceTexture` field
3. Assign the GameObject's Renderer to the `outputRenderer` field
4. Enable `enableInteractiveMode` (enabled by default)

### Interactive Controls

#### Mouse Interaction
- **Left Click & Drag**: Select and move the object around the scene
- **Mouse Scroll**: Scale the texture using content-aware seam carving
  - Scroll up: Enlarge the texture (insert seams)  
  - Scroll down: Shrink the texture (remove seams)

#### Axis-Specific Scaling
- **Hold X + Scroll**: Scale only on the X-axis (vertical seams only)
- **Hold Y + Scroll**: Scale only on the Y-axis (horizontal seams only)
- **No keys held + Scroll**: Uniform scaling (both X and Y axes)

#### Seam Visualization
- **GUI Button**: Click "Show Seams" / "Hide Seams" button in the top-left corner
- **Keyboard**: Press `S` key to toggle seam display
- **Context Menu**: Right-click the component and select "Toggle Seam Display"

### Configuration Options

- `enableInteractiveMode`: Toggle interactive controls on/off
- `showSeams`: Initial state of seam visualization
- `seamColor`: Color used to highlight seams (default: red)
- `targetWidth/targetHeight`: Used for batch resizing via context menu
- `sourceTexture`: Input texture to process
- `outputRenderer`: Renderer that will display the result

### Batch Processing (Legacy Mode)

The original batch processing is still available:
- Right-click the component in Inspector
- Select "Resize Image" from context menu
- Resizes from current dimensions to `targetWidth` x `targetHeight`

### Technical Notes

- Textures must be marked as "Read/Write Enabled" in import settings
- The component creates readable copies of textures automatically
- Seam carving preserves important image features while scaling
- Interactive mode requires a Camera tagged as "MainCamera"
- The GameObject needs a Collider for mouse interaction detection