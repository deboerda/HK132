using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlaneDisplayController : MonoBehaviour
{
    [Serializable]
    public class PlaneView
    {
        public string trackId;
        public RectTransform icon;
        public LineRenderer trail;
        public readonly List<Vector3> trailPoints = new List<Vector3>();
        public float lastHeading;
        public bool hasLastHeading;
    }

    [Header("UI")]
    [SerializeField] private RectTransform mapRoot;
    [SerializeField] private RectTransform planeIconPrefab;
    [SerializeField] private LineRenderer trailTemplate;
    [SerializeField] private Text debugText;

    [Header("Geo Range")]
    [SerializeField] private Vector2 longitudeRange = new Vector2(100f, 110f);
    [SerializeField] private Vector2 latitudeRange = new Vector2(20f, 30f);

    [Header("Game Range")]
    [SerializeField] private Vector2 xRange = new Vector2(-400f, 400f);
    [SerializeField] private Vector2 yRange = new Vector2(-250f, 250f);

    [Header("Plane")]
    [SerializeField] private bool autoFollowDataReceiver = true;
    [SerializeField] private float planeIconZeroHeadingOffset = 90f;
    [SerializeField] private float trailPointDistanceThreshold = 4f;
    [SerializeField] private float trailMaxPoints = 300f;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLog = true;

    private readonly Dictionary<string, PlaneView> planes = new Dictionary<string, PlaneView>();
    private bool isTrailVisible = true;

    public bool IsTrailVisible => isTrailVisible;

    private void Start()
    {
        if (mapRoot == null)
        {
            mapRoot = GetComponent<RectTransform>();
        }

        if (mapRoot == null)
        {
            LogError("Map root is missing.");
        }
    }

    private void Update()
    {
        if (!autoFollowDataReceiver)
        {
            return;
        }

        var receiver = FlightDataStreamReceiver.Instance;
        if (receiver == null)
        {
            return;
        }

        var positions = receiver.SnapshotPositions;
        if (positions != null && positions.Count > 0)
        {
            foreach (var position in positions)
            {
                if (position == null)
                {
                    continue;
                }

                UpdatePlanePosition(
                    position.trackId ?? "primary",
                    position.lng,
                    position.lat,
                    position.alt,
                    position.heading,
                    position.pitch,
                    position.roll);
            }
        }

        if (debugText != null && receiver.PrimaryPosition != null)
        {
            debugText.text = $"Track: {receiver.PrimaryTrackId}\nLon: {receiver.PrimaryPosition.lng:F5}\nLat: {receiver.PrimaryPosition.lat:F5}\nAlt: {receiver.PrimaryPosition.alt:F1}\nHeading: {receiver.PrimaryPosition.heading:F1}";
        }
    }

    public void SetLongitudeRange(float min, float max)
    {
        longitudeRange = new Vector2(min, max);
    }

    public void SetLatitudeRange(float min, float max)
    {
        latitudeRange = new Vector2(min, max);
    }

    public void SetGameRange(float minX, float maxX, float minY, float maxY)
    {
        xRange = new Vector2(minX, maxX);
        yRange = new Vector2(minY, maxY);
    }

    public void ToggleTrails()
    {
        isTrailVisible = !isTrailVisible;
        foreach (var kv in planes)
        {
            if (kv.Value.trail != null)
            {
                kv.Value.trail.gameObject.SetActive(isTrailVisible);
            }
        }
    }

    public void UpdatePlanePosition(int planeIndex, float longitude, float latitude, float altitude, string planeName = "", float heading = 0f, float pitch = 0f, float roll = 0f)
    {
        UpdatePlanePosition(planeIndex.ToString(), longitude, latitude, altitude, heading, pitch, roll, planeName);
    }

    public void UpdatePlanePosition(string trackId, float longitude, float latitude, float altitude, float heading, float pitch = 0f, float roll = 0f, string planeName = "")
    {
        if (string.IsNullOrWhiteSpace(trackId))
        {
            trackId = "unknown";
        }

        PlaneView view = GetOrCreatePlaneView(trackId, planeName);
        Vector2 anchored = ConvertToAnchoredPosition(longitude, latitude);
        Vector3 localPos = new Vector3(anchored.x, anchored.y, 0f);

        if (view.icon != null)
        {
            view.icon.anchoredPosition = anchored;
            view.icon.localRotation = Quaternion.Euler(0f, 0f, ConvertHeadingToUiRotation(heading));
            view.icon.gameObject.SetActive(true);
        }

        UpdateTrail(view, localPos);
        view.lastHeading = heading;
        view.hasLastHeading = true;
    }

    public Vector2 ConvertToAnchoredPosition(float longitude, float latitude)
    {
        if (mapRoot == null)
        {
            return Vector2.zero;
        }

        float lon01 = Mathf.InverseLerp(longitudeRange.x, longitudeRange.y, longitude);
        float lat01 = Mathf.InverseLerp(latitudeRange.x, latitudeRange.y, latitude);

        float x = Mathf.Lerp(xRange.x, xRange.y, lon01);
        float y = Mathf.Lerp(yRange.x, yRange.y, lat01);
        return new Vector2(x, y);
    }

    private float ConvertHeadingToUiRotation(float heading)
    {
        return planeIconZeroHeadingOffset - heading;
    }

    public void ClearAllTrails()
    {
        foreach (var view in planes.Values)
        {
            view.trailPoints.Clear();
            if (view.trail != null)
            {
                view.trail.positionCount = 0;
            }
        }
    }

    private PlaneView GetOrCreatePlaneView(string trackId, string planeName)
    {
        if (planes.TryGetValue(trackId, out var view))
        {
            return view;
        }

        view = new PlaneView { trackId = trackId };

        if (planeIconPrefab != null && mapRoot != null)
        {
            RectTransform icon = Instantiate(planeIconPrefab, mapRoot);
            icon.name = string.IsNullOrWhiteSpace(planeName) ? $"Plane_{trackId}" : planeName;
            view.icon = icon;
        }

        view.trail = CreateTrailRenderer(trackId);
        planes[trackId] = view;
        return view;
    }

    private LineRenderer CreateTrailRenderer(string trackId)
    {
        GameObject trailObject = new GameObject($"Trail_{trackId}");
        trailObject.transform.SetParent(mapRoot != null ? mapRoot : transform, false);

        var lr = trailObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 0;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 3;
        lr.numCornerVertices = 3;

        if (trailTemplate != null)
        {
            lr.startWidth = trailTemplate.startWidth;
            lr.endWidth = trailTemplate.endWidth;
            lr.startColor = trailTemplate.startColor;
            lr.endColor = trailTemplate.endColor;
            lr.material = trailTemplate.material;
            lr.sortingLayerName = trailTemplate.sortingLayerName;
            lr.sortingOrder = trailTemplate.sortingOrder;
            lr.alignment = trailTemplate.alignment;
            lr.widthCurve = trailTemplate.widthCurve;
            lr.colorGradient = trailTemplate.colorGradient;
        }
        else
        {
            lr.startWidth = 3f;
            lr.endWidth = 3f;
            lr.startColor = new Color(0.1f, 0.65f, 1f, 1f);
            lr.endColor = new Color(0.1f, 0.65f, 1f, 1f);
            lr.material = new Material(Shader.Find("Sprites/Default"));
        }

        lr.gameObject.SetActive(isTrailVisible);
        return lr;
    }

    private void UpdateTrail(PlaneView view, Vector3 position)
    {
        if (view.trail == null)
        {
            return;
        }

        if (view.trailPoints.Count == 0)
        {
            view.trailPoints.Add(position);
        }
        else
        {
            Vector3 last = view.trailPoints[view.trailPoints.Count - 1];
            if (Vector3.Distance(last, position) >= trailPointDistanceThreshold)
            {
                view.trailPoints.Add(position);
            }
        }

        while (view.trailPoints.Count > Mathf.Max(16, (int)trailMaxPoints))
        {
            view.trailPoints.RemoveAt(0);
        }

        view.trail.gameObject.SetActive(isTrailVisible);
        view.trail.positionCount = view.trailPoints.Count;
        view.trail.SetPositions(view.trailPoints.ToArray());
    }

    private void Log(string message)
    {
        if (enableDebugLog)
        {
            Debug.Log($"[PlaneDisplayController] {message}");
        }
    }

    private void LogError(string message)
    {
        if (enableDebugLog)
        {
            Debug.LogError($"[PlaneDisplayController] {message}");
        }
    }
}
