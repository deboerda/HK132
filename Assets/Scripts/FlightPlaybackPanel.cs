using System;
using UnityEngine;
using UnityEngine.UI;

public class FlightPlaybackPanel : MonoBehaviour
{
    public enum PlaybackState
    {
        Idle,
        Ready,
        Playing,
        Paused,
        Stopped
    }

    [Header("Transport")]
    [SerializeField] private FlightDataStreamClient dataClient;
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool requestTaskTimeOnStart = true;
    [SerializeField] private string defaultTaskId = "";

    [Header("Timeline")]
    [SerializeField] private float minFlightTime = 0f;
    [SerializeField] private float maxFlightTime = 600f;
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private InputField seekTimeInput;
    [SerializeField] private Text currentTimeText;
    [SerializeField] private Text progressText;
    [SerializeField] private Text realtimeClockText;
    [SerializeField] private Text connectionStatusText;
    [SerializeField] private Text serverStatusText;

    [Header("Playback Controls")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button stopButton;
    [SerializeField] private Button seekButton;
    [SerializeField] private Button[] speedButtons;
    [SerializeField] private Text stateText;
    [SerializeField] private Text speedText;

    [Header("Speed Options")]
    [SerializeField] private float[] speedOptions = new float[] { 0.5f, 1f, 2f, 4f, 8f };

    [Header("Debug")]
    [SerializeField] private bool enableDebugLog = true;

    private PlaybackState currentState = PlaybackState.Idle;
    private float currentFlightTime;
    private float currentSpeed = 1f;
    private string realtimeClockFormat = "HH:mm:ss.fff";
    private readonly object taskTimeLock = new object();
    private FlightDataStreamReceiver.TaskTimeInfo pendingTaskTimeInfo;
    private readonly object flightTimeLock = new object();
    private float pendingFlightTime = -1f;
    private string taskStartTime;

    private void OnEnable()
    {
        FlightDataStreamReceiver.TaskTimeInfoReceived += OnTaskTimeInfoReceived;
        FlightDataStreamReceiver.FlightTimeReceived += OnFlightTimeReceived;
    }

    private void OnDisable()
    {
        FlightDataStreamReceiver.TaskTimeInfoReceived -= OnTaskTimeInfoReceived;
        FlightDataStreamReceiver.FlightTimeReceived -= OnFlightTimeReceived;
    }

    private void Start()
    {
        if (dataClient == null)
        {
            dataClient = GetComponent<FlightDataStreamClient>();
        }

        if (connectOnStart && dataClient != null)
        {
            dataClient.Connect();
            SetState(PlaybackState.Ready);
        }

        if (requestTaskTimeOnStart)
        {
            RequestTaskTimeInfo();
        }

        BindUI();
        UpdateSpeedUI();
        UpdateTimelineUI();
        UpdateConnectionUI();
        UpdateRealtimeClock();
    }

    private void Update()
    {
        ApplyPendingTaskTimeInfo();
        ApplyPendingFlightTime();
        UpdateRealtimeClock();
        UpdateConnectionUI();
    }

    private void BindUI()
    {
        if (timelineSlider != null)
        {
            timelineSlider.minValue = 0f;
            timelineSlider.maxValue = 1f;
            timelineSlider.onValueChanged.RemoveListener(OnTimelineSliderChanged);
            timelineSlider.onValueChanged.AddListener(OnTimelineSliderChanged);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(OnStartClicked);
            startButton.onClick.AddListener(OnStartClicked);
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.RemoveListener(OnPauseClicked);
            pauseButton.onClick.AddListener(OnPauseClicked);
        }

        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(OnStopClicked);
            stopButton.onClick.AddListener(OnStopClicked);
        }

        if (seekButton != null)
        {
            seekButton.onClick.RemoveListener(OnSeekClicked);
            seekButton.onClick.AddListener(OnSeekClicked);
        }

        if (speedButtons != null)
        {
            for (int i = 0; i < speedButtons.Length; i++)
            {
                int index = i;
                if (speedButtons[index] == null) continue;
                speedButtons[index].onClick.RemoveAllListeners();
                speedButtons[index].onClick.AddListener(() => OnSpeedButtonClicked(index));
            }
        }
    }

    public void OnStartClicked()
    {
        if (currentState == PlaybackState.Playing)
        {
            if (SendPause())
            {
                SetState(PlaybackState.Paused);
            }

            return;
        }

        if (SendStart())
        {
            SetState(PlaybackState.Playing);
        }
    }

    public void OnPauseClicked()
    {
        if (SendPause())
        {
            SetState(PlaybackState.Paused);
        }
    }

    public void OnStopClicked()
    {
        if (SendStop())
        {
            SetFlightTime(minFlightTime);
            SeekToCurrentFlightTime();
            SetState(PlaybackState.Stopped);
        }
    }

    public void OnSeekClicked()
    {
        string targetTime = seekTimeInput != null ? seekTimeInput.text : string.Empty;
        if (SeekToTime(targetTime))
        {
            Log($"Seek to {targetTime}");
        }
    }

    public void OnSpeedButtonClicked(int index)
    {
        if (speedOptions == null || speedOptions.Length == 0)
        {
            Log("Speed options are empty.");
            return;
        }

        if (index < 0 || index >= speedOptions.Length)
        {
            return;
        }

        SetSpeed(speedOptions[index]);
    }

    public void OnTimelineSliderChanged(float sliderValue)
    {
        currentFlightTime = Mathf.Lerp(minFlightTime, maxFlightTime, sliderValue);
        UpdateTimelineUI();
        SeekToCurrentFlightTime();
    }

    public bool SendStart()
    {
        if (!EnsureClient())
        {
            return false;
        }

        dataClient.SendTaskTimeInfoRequest(defaultTaskId);
        return dataClient.SendStart(defaultTaskId);


    }

    public bool RequestTaskTimeInfo()
    {
        return EnsureClient() && dataClient.SendTaskTimeInfoRequest(defaultTaskId);
    }

    public bool SendPause()
    {
        return EnsureClient() && dataClient.SendPause();
    }

    public bool SendStop()
    {
        return EnsureClient() && dataClient.SendStop();
    }

    public bool SetSpeed(float multiplier)
    {
        currentSpeed = multiplier;
        UpdateSpeedUI();
        return EnsureClient() && dataClient.SendSpeed(multiplier);
    }

    public bool SeekToTime(string targetTime)
    {
        if (string.IsNullOrWhiteSpace(targetTime))
        {
            Log("SeekToTime ignored: targetTime is empty.");
            return false;
        }

        return EnsureClient() && dataClient.SendSeek(targetTime.Trim());
    }

    public void SetFlightTime(float time)
    {
        currentFlightTime = time;
        UpdateTimelineUI();
    }

    public void SetFlightTimeRange(float minTime, float maxTime)
    {
        minFlightTime = minTime;
        maxFlightTime = Mathf.Max(minTime, maxTime);
        currentFlightTime = Mathf.Clamp(currentFlightTime, minFlightTime, maxFlightTime);
        UpdateTimelineUI();
    }

    public void SetNormalizedProgress(float progress)
    {
        currentFlightTime = Mathf.Lerp(minFlightTime, maxFlightTime, Mathf.Clamp01(progress));
        UpdateTimelineUI();
    }

    public void SetState(PlaybackState state)
    {
        currentState = state;
        UpdateStateUI();
    }

    public PlaybackState GetState()
    {
        return currentState;
    }

    private bool EnsureClient()
    {
        if (dataClient == null)
        {
            Log("FlightDataStreamClient is missing.");
            return false;
        }

        return dataClient.Connect();
    }

    private void OnTaskTimeInfoReceived(FlightDataStreamReceiver.TaskTimeInfo info)
    {
        if (info == null)
        {
            return;
        }

        lock (taskTimeLock)
        {
            pendingTaskTimeInfo = info;
        }
    }

    private void ApplyPendingTaskTimeInfo()
    {
        FlightDataStreamReceiver.TaskTimeInfo info = null;
        lock (taskTimeLock)
        {
            if (pendingTaskTimeInfo == null)
            {
                return;
            }

            info = pendingTaskTimeInfo;
            pendingTaskTimeInfo = null;
        }

        SetFlightTimeRange(0f, info.durationSeconds);
        taskStartTime = info.startTime;

        if (seekTimeInput != null && !string.IsNullOrWhiteSpace(info.startTime))
        {
            seekTimeInput.SetTextWithoutNotify(info.startTime);
        }

        Log($"Task time range applied: {info.startTime} ~ {info.endTime}, max={info.durationSeconds:F3}s");
    }

    private void OnFlightTimeReceived(string flightTime, float elapsedSeconds)
    {
        lock (flightTimeLock)
        {
            pendingFlightTime = elapsedSeconds;
        }
    }

    private void ApplyPendingFlightTime()
    {
        float time;
        lock (flightTimeLock)
        {
            if (pendingFlightTime < 0f)
            {
                return;
            }

            time = pendingFlightTime;
            pendingFlightTime = -1f;
        }

        SetFlightTime(time);
    }

    private bool SeekToCurrentFlightTime()
    {
        if (string.IsNullOrWhiteSpace(taskStartTime) || !TryParseLocalTime(taskStartTime, out DateTime start))
        {
            return false;
        }

        DateTime target = start.AddSeconds(currentFlightTime);
        string targetTime = target.ToString("yyyy-MM-dd HH:mm:ss.fff");
        if (seekTimeInput != null)
        {
            seekTimeInput.SetTextWithoutNotify(targetTime);
        }

        return SeekToTime(targetTime);
    }

    private static bool TryParseLocalTime(string value, out DateTime time)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out time);
    }

    private void UpdateTimelineUI()
    {
        float normalized = Mathf.Approximately(maxFlightTime, minFlightTime)
            ? 0f
            : Mathf.Clamp01((currentFlightTime - minFlightTime) / (maxFlightTime - minFlightTime));

        if (timelineSlider != null && !Mathf.Approximately(timelineSlider.value, normalized))
        {
            timelineSlider.SetValueWithoutNotify(normalized);
        }

        string flightTimeText = $"{currentFlightTime:F3}s";

        if (currentTimeText != null)
        {
            currentTimeText.text = flightTimeText;
        }

        if (progressText != null)
        {
            progressText.text = $"进度: {normalized * 100f:F1}%";
        }
    }

    private void UpdateStateUI()
    {
        if (stateText != null)
        {
            stateText.text = $"状态: {GetStateDisplayName(currentState)}";
        }

        if (startButton != null)
        {
            var label = startButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = currentState == PlaybackState.Playing ? "暂停" : "开始";
            }
        }
    }

    private void UpdateConnectionUI()
    {
        if (dataClient == null)
        {
            if (connectionStatusText != null)
            {
                connectionStatusText.text = "连接状态: 未绑定客户端";
            }

            if (serverStatusText != null)
            {
                serverStatusText.text = "服务器状态: 未知";
            }

            return;
        }

        string connectionStatus = dataClient.IsConnected ? "已连接" : "未连接";
        if (connectionStatusText != null)
        {
            connectionStatusText.text = $"连接状态: {connectionStatus}";
        }

        if (serverStatusText != null)
        {
            serverStatusText.text = $"服务器状态: {dataClient.Host}:{dataClient.Port}";
        }
    }

    private void UpdateRealtimeClock()
    {
        if (realtimeClockText != null)
        {
            realtimeClockText.text = $"当前时钟: {DateTime.Now.ToString(realtimeClockFormat)}";
        }
    }

    private void UpdateSpeedUI()
    {
        if (speedText != null)
        {
            speedText.text = $"倍速: {currentSpeed:F1}x";
        }
    }

    private static string GetStateDisplayName(PlaybackState state)
    {
        switch (state)
        {
            case PlaybackState.Idle:
                return "待机";
            case PlaybackState.Ready:
                return "就绪";
            case PlaybackState.Playing:
                return "播放中";
            case PlaybackState.Paused:
                return "已暂停";
            case PlaybackState.Stopped:
                return "已停止";
            default:
                return state.ToString();
        }
    }

    private void Log(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[FlightPlaybackPanel] {message}");
        }
    }
}
