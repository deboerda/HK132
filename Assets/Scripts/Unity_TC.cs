using UnityEngine;

public class Unity_TC : MonoBehaviour
{
    [Header("UI Layers")]
    public RectTransform itemMark; // 飞机图案标志
    public RectTransform itemBall; // 底部侧滑黑球

    [Header("Flight Data")]
    public float turnRate = 0.0f; // 转弯率
    public float slipSkid = 0.0f; // 侧滑量

    void Update()
    {
        // 从UDPDataReceiver获取数据
        if (UDPDataReceiver.Instance != null)
        {
            // 获取横滚角速率数据 [载机横滚角速率][_角速率]
            string turnRateKey = "[载机横滚角速率][_角速率]";
            if (UDPDataReceiver.Instance.planeData.ContainsKey(turnRateKey))
            {
                float turnRateValue;
                if (float.TryParse(UDPDataReceiver.Instance.planeData[turnRateKey], out turnRateValue))
                {
                    turnRate = turnRateValue;
                }
            }
            
            // 获取侧滑角数据 [载机侧滑角][_角度_毫弧度]
            string slipSkidKey = "[载机侧滑角][_角度_毫弧度]";
            if (UDPDataReceiver.Instance.planeData.ContainsKey(slipSkidKey))
            {
                float slipSkidValue;
                if (float.TryParse(UDPDataReceiver.Instance.planeData[slipSkidKey], out slipSkidValue))
                {
                    // 转换毫弧度为度
                    slipSkid = slipSkidValue * 180f / Mathf.PI / 1000f;
                }
            }
        }
        
        // 1. 黑球的旋转映射
        if (itemBall != null)
            itemBall.localEulerAngles = new Vector3(0, 0, slipSkid);

        // 2. 飞机标记的倾斜映射 (原理：3度转弯率 = 20度UI旋转)
        float angle = (turnRate / 3.0f) * 20.0f;
        if (itemMark != null)
            itemMark.localEulerAngles = new Vector3(0, 0, -angle);
    }
}