using UnityEngine;

public class Unity_HSI : MonoBehaviour
{
    [Header("UI Layers")]
    public RectTransform itemFace;
    public RectTransform itemHand;

    [Header("Flight Data")]
    public float heading = 0f;

    public void SetHeading(float headingDegrees)
    {
        heading = headingDegrees;
    }

    private void Update()
    {
        float faceAngle = -heading;

        if (itemFace != null)
            itemFace.localEulerAngles = new Vector3(0, 0, faceAngle);
        if (itemHand != null)
            itemHand.localEulerAngles = Vector3.zero;
    }
}
