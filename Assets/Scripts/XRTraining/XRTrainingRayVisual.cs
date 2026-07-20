using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class XRTrainingRayVisual : MonoBehaviour
{
    public float maxDistance = 6f;
    public Color defaultColor = new Color(0.1f, 0.85f, 1f, 1f);
    public Color hitColor = new Color(1f, 0.9f, 0.25f, 1f);
    public LayerMask layerMask = ~0;

    LineRenderer m_LineRenderer;
    Material m_RuntimeMaterial;

    void Awake()
    {
        m_LineRenderer = GetComponent<LineRenderer>();
        m_RuntimeMaterial = new Material(Shader.Find("Sprites/Default"));
        m_LineRenderer.sharedMaterial = m_RuntimeMaterial;
        m_LineRenderer.positionCount = 2;
        m_LineRenderer.useWorldSpace = true;
        m_LineRenderer.startWidth = 0.006f;
        m_LineRenderer.endWidth = 0.002f;
    }

    void LateUpdate()
    {
        var origin = transform.position;
        var direction = transform.forward;
        var end = origin + direction * maxDistance;
        var color = defaultColor;

        if (Physics.Raycast(origin, direction, out var hitInfo, maxDistance, layerMask, QueryTriggerInteraction.Ignore))
        {
            end = hitInfo.point;
            color = hitColor;
        }

        m_LineRenderer.SetPosition(0, origin);
        m_LineRenderer.SetPosition(1, end);
        m_LineRenderer.startColor = color;
        m_LineRenderer.endColor = new Color(color.r, color.g, color.b, 0.35f);
    }
}
