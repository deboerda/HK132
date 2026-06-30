using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class FlightDataStreamReceiver : MonoBehaviour
{
    [Serializable]
    public class TrackPosition
    {
        public string trackId;
        public string planId;
        public string flightTime;
        public float lng;
        public float lat;
        public float alt;
        public float heading;
        public float pitch;
        public float roll;
    }

    [Serializable]
    public class TrackTimetableState
    {
        public string trackId;
        public string state;
        public float battery;
        public float speed;
    }

    [Serializable]
    public class TaskTimeInfo
    {
        public string taskId;
        public string startTime;
        public string endTime;
        public float durationSeconds;
    }

    public static FlightDataStreamReceiver Instance { get; private set; }
    public static event Action<TaskTimeInfo> TaskTimeInfoReceived;
    public static event Action<string, float> FlightTimeReceived;

    [Header("Data Server")]
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int port = 8888;
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool listenOnAllInterfaces = true;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLog = true;

    private TcpClient client;
    private TcpListener listener;
    private NetworkStream stream;
    private Thread readThread;
    private Thread listenThread;
    private volatile bool isRunning;
    private readonly object dataLock = new object();

    private readonly Dictionary<string, TrackPosition> latestPositionsByTrack = new Dictionary<string, TrackPosition>();
    private readonly Dictionary<string, TrackTimetableState> latestStatesByTrack = new Dictionary<string, TrackTimetableState>();
    private readonly Dictionary<string, float> previousHeadingByTrack = new Dictionary<string, float>();
    private readonly Dictionary<string, DateTime> previousTimeByTrack = new Dictionary<string, DateTime>();
    private string primaryTrackId;
    private TaskTimeInfo latestTaskTimeInfo;
    private DateTime? taskStartTime;

    public string Host => host;
    public int Port => port;
    public bool IsConnected => client != null && client.Connected && stream != null;

    public TrackPosition PrimaryPosition { get { lock (dataLock) { return GetPrimaryPositionUnsafe(); } } }
    public TrackTimetableState PrimaryTimetableState { get { lock (dataLock) { return GetPrimaryStateUnsafe(); } } }
    public float PrimaryAltitudeMeters { get { lock (dataLock) { return GetPrimaryPositionUnsafe()?.alt ?? 0f; } } }
    public float PrimaryAirspeedKnots { get { lock (dataLock) { return GetPrimaryStateUnsafe()?.speed ?? 0f; } } }
    public float PrimaryPitchDeg { get { lock (dataLock) { return GetPrimaryPositionUnsafe()?.pitch ?? 0f; } } }
    public float PrimaryRollDeg { get { lock (dataLock) { return GetPrimaryPositionUnsafe()?.roll ?? 0f; } } }
    public float PrimaryTurnRateDegPerSec { get { lock (dataLock) { return GetPrimaryTurnRateUnsafe(); } } }
    public float PrimarySlipSkidDeg { get { lock (dataLock) { return GetPrimaryPositionUnsafe()?.roll ?? 0f; } } }
    public string PrimaryTrackId { get { lock (dataLock) { return primaryTrackId; } } }
    public TaskTimeInfo LatestTaskTimeInfo { get { lock (dataLock) { return latestTaskTimeInfo; } } }
    public List<TrackPosition> SnapshotPositions
    {
        get
        {
            lock (dataLock)
            {
                return new List<TrackPosition>(latestPositionsByTrack.Values);
            }
        }
    }
    public List<TrackTimetableState> SnapshotStates
    {
        get
        {
            lock (dataLock)
            {
                return new List<TrackTimetableState>(latestStatesByTrack.Values);
            }
        }
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    public bool Connect()
    {
        if (IsConnected || listener != null)
        {
            return true;
        }

        try
        {
            isRunning = true;

            var bindAddress = listenOnAllInterfaces ? System.Net.IPAddress.Any : System.Net.IPAddress.Parse(host);
            listener = new TcpListener(bindAddress, port);
            listener.Start();

            listenThread = new Thread(ListenLoop);
            listenThread.IsBackground = true;
            listenThread.Start();

            Log($"Listening on {bindAddress}:{port}");
            return true;
        }
        catch (Exception ex)
        {
            LogError($"Listen failed: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        isRunning = false;

        try
        {
            client?.Close();
        }
        catch { }
        client = null;

        try
        {
            listener?.Stop();
        }
        catch { }
        listener = null;

        try
        {
            stream?.Close();
        }
        catch { }
        stream = null;
    }

    private void ListenLoop()
    {
        try
        {
            while (isRunning && listener != null)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(50);
                    continue;
                }

                client = listener.AcceptTcpClient();
                client.NoDelay = true;
                stream = client.GetStream();
                Log($"Data client connected from {client.Client.RemoteEndPoint}");
                ReadLoop();
            }
        }
        catch (Exception ex)
        {
            if (isRunning)
            {
                LogError($"Listen loop failed: {ex.Message}");
            }
        }
        finally
        {
            Disconnect();
        }
    }

    private void ReadLoop()
    {
        try
        {
            using (var reader = new StreamReader(stream))
            {
                while (isRunning && stream != null && client != null && client.Connected)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    HandleMessage(line);
                }
            }
        }
        catch (Exception ex)
        {
            if (isRunning)
            {
                LogError($"Read loop failed: {ex.Message}");
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            var envelope = JObject.Parse(json);
            string type = envelope.Value<string>("type");
            var data = envelope["data"];

            switch (type)
            {
                case "TRACK_POSITIONS":
                    ParseTrackPositions(data);
                    break;
                case "TIMETABLE_STATE":
                    ParseTimetableState(data);
                    break;
                case "CYCLE_TABLE_DATA":
                    ParseCycleTableData(data);
                    break;
                case "TASK_TIME_INFO":
                    ParseTaskTimeInfo(data);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            LogError($"Parse message failed: {ex.Message}");
        }
    }

    private void ParseTrackPositions(JToken data)
    {
        if (data == null)
        {
            return;
        }

        PublishFlightTime(data.Value<string>("flightTime"));

        lock (dataLock)
        {
            var tracks = data["tracks"] as JArray;
            if (tracks == null)
            {
                return;
            }

            foreach (var trackToken in tracks)
            {
                string trackId = trackToken.Value<string>("trackId");
                if (string.IsNullOrWhiteSpace(trackId))
                {
                    continue;
                }

                var plans = trackToken["plans"] as JArray;
                if (plans == null)
                {
                    continue;
                }

                foreach (var planToken in plans)
                {
                    string planId = planToken.Value<string>("planId");
                    var positions = planToken["positions"] as JArray;
                    if (positions == null)
                    {
                        continue;
                    }

                    foreach (var posToken in positions)
                    {
                        var position = new TrackPosition
                        {
                            trackId = trackId,
                            planId = planId,
                            flightTime = posToken.Value<string>("flightTime"),
                            lng = posToken.Value<float?>("lng") ?? 0f,
                            lat = posToken.Value<float?>("lat") ?? 0f,
                            alt = posToken.Value<float?>("alt") ?? 0f,
                            heading = posToken.Value<float?>("heading") ?? 0f,
                            pitch = posToken.Value<float?>("pitch") ?? 0f,
                            roll = posToken.Value<float?>("roll") ?? 0f,
                        };

                        latestPositionsByTrack[trackId] = position;
                        if (string.IsNullOrWhiteSpace(primaryTrackId))
                        {
                            primaryTrackId = trackId;
                        }

                        UpdateTurnRate(trackId, position);
                    }
                }
            }
        }
    }

    private void ParseTaskTimeInfo(JToken data)
    {
        if (data == null)
        {
            return;
        }

        string startTime = data.Value<string>("startTime");
        string endTime = data.Value<string>("endTime");
        float durationSeconds = 0f;

        if (TryParseLocalTime(startTime, out DateTime start) && TryParseLocalTime(endTime, out DateTime end))
        {
            durationSeconds = Mathf.Max(0f, (float)(end - start).TotalSeconds);
            taskStartTime = start;
        }

        var info = new TaskTimeInfo
        {
            taskId = data.Value<string>("taskId"),
            startTime = startTime,
            endTime = endTime,
            durationSeconds = durationSeconds,
        };

        lock (dataLock)
        {
            latestTaskTimeInfo = info;
        }

        TaskTimeInfoReceived?.Invoke(info);
        Log($"Task time info: {info.startTime} ~ {info.endTime} ({info.durationSeconds:F3}s)");
    }

    private void PublishFlightTime(string flightTime)
    {
        if (string.IsNullOrWhiteSpace(flightTime) || !TryParseLocalTime(flightTime, out DateTime current))
        {
            return;
        }

        DateTime? start;
        lock (dataLock)
        {
            if (!taskStartTime.HasValue)
            {
                taskStartTime = current;
            }

            start = taskStartTime;
        }

        if (!start.HasValue)
        {
            return;
        }

        float elapsedSeconds = Mathf.Max(0f, (float)(current - start.Value).TotalSeconds);
        FlightTimeReceived?.Invoke(flightTime, elapsedSeconds);
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

    private void ParseTimetableState(JToken data)
    {
        if (data == null)
        {
            return;
        }

        PublishFlightTime(data.Value<string>("flightTime"));

        lock (dataLock)
        {
            var states = data["flightStates"] as JArray;
            if (states == null)
            {
                return;
            }

            foreach (var stateToken in states)
            {
                string trackId = stateToken.Value<string>("trackId");
                if (string.IsNullOrWhiteSpace(trackId))
                {
                    continue;
                }

                latestStatesByTrack[trackId] = new TrackTimetableState
                {
                    trackId = trackId,
                    state = stateToken.Value<string>("state"),
                    battery = stateToken.Value<float?>("battery") ?? 0f,
                    speed = stateToken.Value<float?>("speed") ?? 0f,
                };
                if (string.IsNullOrWhiteSpace(primaryTrackId))
                {
                    primaryTrackId = trackId;
                }
            }
        }
    }

    private void ParseCycleTableData(JToken data)
    {
        if (data == null)
        {
            return;
        }

        PublishFlightTime(data.Value<string>("flightTime"));

        // 目前周期表主要用于调试展示，先不额外落库。
    }

    private void UpdateTurnRate(string trackId, TrackPosition position)
    {
        if (string.IsNullOrWhiteSpace(position.flightTime))
        {
            previousHeadingByTrack[trackId] = position.heading;
            return;
        }

        if (!DateTime.TryParse(position.flightTime, out var currentTime))
        {
            previousHeadingByTrack[trackId] = position.heading;
            return;
        }

        if (previousHeadingByTrack.TryGetValue(trackId, out var previousHeading) &&
            previousTimeByTrack.TryGetValue(trackId, out var previousTime))
        {
            double deltaSeconds = (currentTime - previousTime).TotalSeconds;
            if (deltaSeconds > 0.001)
            {
                float deltaHeading = Mathf.DeltaAngle(previousHeading, position.heading);
                _primaryTurnRateCacheByTrack[trackId] = deltaHeading / (float)deltaSeconds;
            }
        }

        previousHeadingByTrack[trackId] = position.heading;
        previousTimeByTrack[trackId] = currentTime;
    }

    private readonly Dictionary<string, float> _primaryTurnRateCacheByTrack = new Dictionary<string, float>();

    private TrackPosition GetPrimaryPositionUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(primaryTrackId) && latestPositionsByTrack.TryGetValue(primaryTrackId, out var position))
        {
            return position;
        }

        foreach (var kv in latestPositionsByTrack)
        {
            primaryTrackId = kv.Key;
            return kv.Value;
        }

        return null;
    }

    private TrackTimetableState GetPrimaryStateUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(primaryTrackId) && latestStatesByTrack.TryGetValue(primaryTrackId, out var state))
        {
            return state;
        }

        foreach (var kv in latestStatesByTrack)
        {
            primaryTrackId = kv.Key;
            return kv.Value;
        }

        return null;
    }

    private float GetPrimaryTurnRateUnsafe()
    {
        if (!string.IsNullOrWhiteSpace(primaryTrackId) && _primaryTurnRateCacheByTrack.TryGetValue(primaryTrackId, out var rate))
        {
            return rate;
        }

        return 0f;
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void Log(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[FlightDataStreamReceiver] {message}");
        }
    }

    private void LogError(string message)
    {
        if (enableDebugLog)
        {
            Debug.LogError($"[FlightDataStreamReceiver] {message}");
        }
    }
}
