using UnityEngine;

public class Unity_VSI : MonoBehaviour
{
    [Header("UI Layers")]
    public RectTransform itemHand; // 垂直速度指针

    [Header("Flight Data")]
    public float climbRate = 0f; // 爬升角 [deg]

    // 垂直速度表刻度范围（根据实际需求调整）
    private const float minClimbRate = -30f; // 最大下降率
    private const float maxClimbRate = 30f;  // 最大爬升率

    void Update()
    {
        // 从UDPDataReceiver获取数据
        if (UDPDataReceiver.Instance != null)
        {
            // 获取爬升角数据 [载机爬升角][_角度_毫弧度]
            string climbRateKey = "[载机爬升角][_角度_毫弧度]";
            if (UDPDataReceiver.Instance.planeData.ContainsKey(climbRateKey))
            {
                float climbRateValue;
                if (float.TryParse(UDPDataReceiver.Instance.planeData[climbRateKey], out climbRateValue))
                {
                    // 转换毫弧度为度
                    climbRate = climbRateValue * 180f / Mathf.PI / 1000f;
                }
            }
        }
        
        // 计算指针旋转角度
        // 线性映射：-30 到 30 度对应 -180 到 180 度
        float normalizedClimbRate = Mathf.Clamp((climbRate - minClimbRate) / (maxClimbRate - minClimbRate), 0f, 1f);
        float angle = (normalizedClimbRate - 0.5f) * 360f;

        // 赋值
        if (itemHand != null)
            itemHand.localEulerAngles = new Vector3(0, 0, -angle);
    }
}
