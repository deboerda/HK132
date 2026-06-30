using UnityEngine;
using UnityEngine.UI;

public class FlightProgressController : MonoBehaviour
{
    [Header("UI组件")]
    public Slider progressSlider; // 进度条组件
    public Text progressText; // 可选的进度文本显示
    
    [Header("进度配置")]
    public float minFlightTime = 0f; // 最小飞行时间（秒）
    public float maxFlightTime = 100f; // 最大飞行时间（秒）
    
    private float currentFlightTime = 0f;
    
    void Start()
    {
        // 初始化进度条
        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.value = 0f;
        }
        
        UpdateProgressUI();
    }
    
    void UpdateProgress()
    {
        // 计算进度百分比
        float progress = Mathf.Clamp01((currentFlightTime - minFlightTime) / (maxFlightTime - minFlightTime));
        
        // 更新进度条
        if (progressSlider != null)
        {
            progressSlider.value = progress;
        }
        
        UpdateProgressUI();
    }
    
    void UpdateProgressUI()
    {
        // 计算百分比
        float progressPercentage = 0f;
        if (maxFlightTime > minFlightTime)
        {
            progressPercentage = Mathf.Clamp01((currentFlightTime - minFlightTime) / (maxFlightTime - minFlightTime)) * 100f;
        }

        // 更新进度文本（优先查找子对象中的Text组件）
        if (progressText != null)
        {
            progressText.text = string.Format("飞行进度: {0:F1}%", progressPercentage);
        }
        else
        {
            // 如果面板上有Text组件直接更新
            Text t = GetComponentInChildren<Text>();
            if (t != null)
            {
                t.text = string.Format("{0:F1}%", progressPercentage);
            }
        }
    }
    
    // 手动设置飞行时间（用于测试）
    public void SetFlightTime(float time)
    {
        currentFlightTime = time;
        UpdateProgress();
    }

    // 直接设置归一化进度，供新的时间轴或数据源驱动
    public void SetNormalizedProgress(float progress)
    {
        currentFlightTime = Mathf.Lerp(minFlightTime, maxFlightTime, Mathf.Clamp01(progress));
        UpdateProgress();
    }
    
    // 重置进度
    public void ResetProgress()
    {
        currentFlightTime = 0f;
        UpdateProgress();
    }
}
