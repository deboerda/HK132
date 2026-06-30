using UnityEngine;

public class Unity_ADI : MonoBehaviour
{
    [Header("UI Layers")]
    public RectTransform itemBack;
    public RectTransform itemFace;
    public RectTransform itemRing;

    [Header("Flight Data")]
    [Range(-180f, 180f)] public float roll = 0.0f;
    [Range(-25f, 25f)] public float pitch = 0.0f;

    private const float originalPixPerDeg = 1.7f;

    public void SetAttitude(float pitchDegrees, float rollDegrees)
    {
        pitch = pitchDegrees;
        roll = rollDegrees;
    }

    private void Update()
    {
        float uiRollAngle = -roll;
        Vector3 rot = new Vector3(0, 0, uiRollAngle);

        if (itemBack != null)
            itemBack.localEulerAngles = rot;
        if (itemFace != null)
            itemFace.localEulerAngles = rot;
        if (itemRing != null)
            itemRing.localEulerAngles = rot;

        float rollRad = roll * Mathf.Deg2Rad;
        float delta = originalPixPerDeg * pitch;
        float deltaX = delta * Mathf.Sin(rollRad);
        float deltaY = -1.0f * (delta * Mathf.Cos(rollRad));

        if (itemFace != null)
            itemFace.anchoredPosition = new Vector2(deltaX, deltaY);
    }
}
