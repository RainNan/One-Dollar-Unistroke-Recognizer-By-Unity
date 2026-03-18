using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UnistrokePanel : MaskableGraphic, IPointerDownHandler, IPointerUpHandler
{
    [Serializable]
    public class GestureTemplate
    {
        public string name;
        public List<Vector2> points;

        public GestureTemplate(string name, List<Vector2> points)
        {
            this.name = name;
            this.points = points;
        }
    }

    [Serializable]
    public class RecognizeResult
    {
        public string gestureName;
        public float score;
        public float distance;

        public RecognizeResult(string gestureName, float score, float distance)
        {
            this.gestureName = gestureName;
            this.score = score;
            this.distance = distance;
        }
    }

    [Header("Templates")]
    [SerializeField] private List<GestureTemplate> templates = new();
    [SerializeField]
    private string currentTemplateName;
    
    [Header("Draw")]
    [SerializeField] private Camera uiCamera;
    [SerializeField] private float lineThickness = 8f;
    [SerializeField] private float minPointDistance = 6f;

    [Header("$1 Config")]
    [SerializeField] private int sampleCount = 64;
    [SerializeField] private float squareSize = 250f;
    
    private readonly List<Vector2> _pathPoints = new();
    private bool _isDrawing;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = true;
    }

    private void Update()
    {
        if (!_isDrawing) return;

        Vector2 screenPoint = Input.mousePosition;

        if (!RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPoint, uiCamera))
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                screenPoint,
                uiCamera,
                out Vector2 localPoint))
            return;

        if (_pathPoints.Count > 0 &&
            Vector2.Distance(_pathPoints[^1], localPoint) < minPointDistance)
            return;

        _pathPoints.Add(localPoint);
        SetVerticesDirty();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right)
            return;

        _isDrawing = true;
        _pathPoints.Clear();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            _pathPoints.Add(localPoint);
            SetVerticesDirty();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right)
            return;

        _isDrawing = false;
        SetVerticesDirty();

        if (_pathPoints.Count < 2)
        {
            Debug.Log("The path contains too few points to recognize.");
            return;
        }

        RecognizeResult result = RecognizeCurrentGesture();
        if (result == null)
        {
            Debug.Log("No gesture templates are available.");
            return;
        }

        Debug.Log($"Recognition result: {result.gestureName}, score={result.score:F4}, distance={result.distance:F4}");
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_pathPoints.Count < 2)
            return;

        for (int i = 0; i < _pathPoints.Count - 1; i++)
        {
            DrawLineSegment(vh, _pathPoints[i], _pathPoints[i + 1], lineThickness, color);
        }
    }

    public void ClearPath()
    {
        _pathPoints.Clear();
        SetVerticesDirty();
    }

    public IReadOnlyList<Vector2> GetPath()
    {
        return _pathPoints;
    }

    public IReadOnlyList<Vector2> GetCurrentPath()
    {
        return _pathPoints;
    }

    public void SaveCurrentAsTemplate(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            Debug.LogWarning("Template name cannot be empty.");
            return;
        }

        if (_pathPoints.Count < 2)
        {
            Debug.LogWarning("The current path contains too few points to save as a template.");
            return;
        }

        List<Vector2> normalized = Normalize(_pathPoints);
        templates.Add(new GestureTemplate(templateName, normalized));
        Debug.Log($"Template saved: {templateName}, point count = {normalized.Count}");
    }

    public void ClearTemplates()
    {
        templates.Clear();
        Debug.Log("All gesture templates have been cleared.");
    }

    public List<GestureTemplate> GetTemplates()
    {
        return templates;
    }

    private RecognizeResult RecognizeCurrentGesture()
    {
        if (templates == null || templates.Count == 0)
            return null;

        List<Vector2> candidate = Normalize(_pathPoints);

        float bestDistance = float.MaxValue;
        string bestName = "Unknown";

        for (int i = 0; i < templates.Count; i++)
        {
            GestureTemplate template = templates[i];
            float distance = PathDistance(candidate, template.points);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestName = template.name;
            }
        }

        float halfDiagonal = 0.5f * Mathf.Sqrt(squareSize * squareSize + squareSize * squareSize);
        float score = 1f - (bestDistance / halfDiagonal);
        score = Mathf.Clamp01(score);

        return new RecognizeResult(bestName, score, bestDistance);
    }

    private List<Vector2> Normalize(List<Vector2> points)
    {
        List<Vector2> resampled = Resample(points, sampleCount);
        List<Vector2> rotated = RotateToZero(resampled);
        List<Vector2> scaled = ScaleToSquare(rotated, squareSize);
        List<Vector2> translated = TranslateToOrigin(scaled);
        return translated;
    }

    private List<Vector2> Resample(List<Vector2> points, int targetCount)
    {
        var result = new List<Vector2>();

        if (points == null || points.Count == 0 || targetCount <= 0)
            return result;

        if (points.Count == 1)
        {
            for (int i = 0; i < targetCount; i++)
                result.Add(points[0]);
            return result;
        }

        float totalLength = PathLength(points);
        if (totalLength <= Mathf.Epsilon)
        {
            for (int i = 0; i < targetCount; i++)
                result.Add(points[0]);
            return result;
        }

        float interval = totalLength / (targetCount - 1);
        result.Add(points[0]);

        float accumulated = 0f;
        Vector2 prev = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 curr = points[i];
            float dist = Vector2.Distance(prev, curr);
            if (dist <= Mathf.Epsilon)
                continue;

            while (accumulated + dist >= interval)
            {
                float t = (interval - accumulated) / dist;
                Vector2 sampledPoint = Vector2.Lerp(prev, curr, t);
                result.Add(sampledPoint);
                prev = sampledPoint;
                dist = Vector2.Distance(prev, curr);
                accumulated = 0f;
            }

            accumulated += dist;
            prev = curr;
        }

        if (result.Count == targetCount - 1)
            result.Add(points[^1]);

        while (result.Count < targetCount)
            result.Add(points[^1]);

        if (result.Count > targetCount)
            result.RemoveRange(targetCount, result.Count - targetCount);

        return result;
    }

    private List<Vector2> RotateToZero(List<Vector2> points)
    {
        float angle = IndicativeAngle(points);
        return RotateBy(points, -angle);
    }

    private float IndicativeAngle(List<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return 0f;

        Vector2 center = Centroid(points);
        Vector2 first = points[0];
        return Mathf.Atan2(first.y - center.y, first.x - center.x);
    }

    private List<Vector2> RotateBy(List<Vector2> points, float radians)
    {
        var rotated = new List<Vector2>(points.Count);

        if (points == null || points.Count == 0)
            return rotated;

        Vector2 center = Centroid(points);
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);

        for (int i = 0; i < points.Count; i++)
        {
            float dx = points[i].x - center.x;
            float dy = points[i].y - center.y;
            float x = dx * cos - dy * sin + center.x;
            float y = dx * sin + dy * cos + center.y;
            rotated.Add(new Vector2(x, y));
        }

        return rotated;
    }

    private List<Vector2> ScaleToSquare(List<Vector2> points, float size)
    {
        var scaled = new List<Vector2>(points.Count);

        if (points == null || points.Count == 0)
            return scaled;

        Rect box = BoundingBox(points);
        float width = box.width <= Mathf.Epsilon ? 1f : box.width;
        float height = box.height <= Mathf.Epsilon ? 1f : box.height;

        for (int i = 0; i < points.Count; i++)
        {
            float x = points[i].x * (size / width);
            float y = points[i].y * (size / height);
            scaled.Add(new Vector2(x, y));
        }

        return scaled;
    }

    private Rect BoundingBox(List<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return new Rect(0f, 0f, 0f, 0f);

        float minX = points[0].x;
        float minY = points[0].y;
        float maxX = points[0].x;
        float maxY = points[0].y;

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 point = points[i];
            minX = Mathf.Min(minX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxX = Mathf.Max(maxX, point.x);
            maxY = Mathf.Max(maxY, point.y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private List<Vector2> TranslateToOrigin(List<Vector2> points)
    {
        var translated = new List<Vector2>(points.Count);

        if (points == null || points.Count == 0)
            return translated;

        Vector2 center = Centroid(points);
        for (int i = 0; i < points.Count; i++)
        {
            translated.Add(points[i] - center);
        }

        return translated;
    }

    private Vector2 Centroid(List<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;

        float sumX = 0f;
        float sumY = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            sumX += points[i].x;
            sumY += points[i].y;
        }

        return new Vector2(sumX / points.Count, sumY / points.Count);
    }

    private float PathDistance(List<Vector2> a, List<Vector2> b)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return float.MaxValue;

        int count = Mathf.Min(a.Count, b.Count);
        float sum = 0f;

        for (int i = 0; i < count; i++)
        {
            sum += Vector2.Distance(a[i], b[i]);
        }

        return sum / count;
    }

    private float PathLength(List<Vector2> points)
    {
        if (points == null || points.Count < 2)
            return 0f;

        float length = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            length += Vector2.Distance(points[i - 1], points[i]);
        }

        return length;
    }

    private void DrawLineSegment(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color32 color32)
    {
        Vector2 dir = end - start;
        float length = dir.magnitude;
        if (length <= 0.001f)
            return;

        dir /= length;
        Vector2 normal = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);

        Vector2 v0 = start - normal;
        Vector2 v1 = start + normal;
        Vector2 v2 = end + normal;
        Vector2 v3 = end - normal;

        int index = vh.currentVertCount;
        vh.AddVert(v0, color32, Vector2.zero);
        vh.AddVert(v1, color32, Vector2.up);
        vh.AddVert(v2, color32, Vector2.one);
        vh.AddVert(v3, color32, Vector2.right);
        vh.AddTriangle(index + 0, index + 1, index + 2);
        vh.AddTriangle(index + 0, index + 2, index + 3);
    }

    [ContextMenu("Save Current Template")]
    private void SaveCurrentTemplate()
    {
        SaveCurrentAsTemplate(currentTemplateName);
    }
}
