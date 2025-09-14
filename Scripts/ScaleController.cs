using UnityEngine;

/// <summary>
/// Controls the scale of a target object using keyboard input.
/// </summary>
public class ScaleController : MonoBehaviour
{
    [Tooltip("The object whose scale will be changed. Assign your ContentAwareResizer object here.")]
    public Transform targetObject;

    [Tooltip("How quickly the object scales per second.")]
    public float scaleSpeed = 0.5f;

    void Update()
    {
        if (targetObject == null)
        {
            Debug.LogWarning("Target object is not assigned in ScaleController.");
            return;
        }

        // Use arrow keys to adjust scale
        // Use Left/Right for X-axis and Up/Down for Y-axis
        float scaleX = Input.GetKey(KeyCode.RightArrow) ? scaleSpeed : (Input.GetKey(KeyCode.LeftArrow) ? -scaleSpeed : 0);
        float scaleY = Input.GetKey(KeyCode.UpArrow) ? scaleSpeed : (Input.GetKey(KeyCode.DownArrow) ? -scaleSpeed : 0);

        Vector3 scaleChange = new Vector3(scaleX, scaleY, 0) * Time.deltaTime;

        // Apply the change and ensure scale doesn't become negative
        targetObject.localScale += scaleChange;
        targetObject.localScale = new Vector3(
            Mathf.Max(0.1f, targetObject.localScale.x),
            Mathf.Max(0.1f, targetObject.localScale.y),
            targetObject.localScale.z
        );
    }
}