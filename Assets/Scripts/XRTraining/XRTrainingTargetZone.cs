using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
public sealed class XRTrainingTargetZone : MonoBehaviour
{
    public XRTrainingColorId colorId;
    public XRTrainingManager manager;
    public float targetRadius = 0.6f;

    Renderer[] m_Renderers;
    Color[] m_BaseColors;
    Collider m_Collider;
    Coroutine m_FlashRoutine;

    void Reset()
    {
        var zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null)
            zoneCollider.isTrigger = true;
    }

    void Awake()
    {
        CacheComponents();
        CaptureBaseColors();
    }

    void OnTriggerEnter(Collider other)
    {
        var grabbable = other.GetComponentInParent<XRTrainingGrabbable>();
        if (grabbable != null)
            grabbable.SetCurrentZone(this);
    }

    void OnTriggerExit(Collider other)
    {
        var grabbable = other.GetComponentInParent<XRTrainingGrabbable>();
        if (grabbable != null && grabbable.CurrentZone == this)
            grabbable.SetCurrentZone(null);
    }

    public void ApplyDifficultyVisual(float radius, bool active)
    {
        targetRadius = Mathf.Max(0.1f, radius);
        gameObject.SetActive(active);
        transform.localScale = new Vector3(targetRadius * 2f, 0.08f, targetRadius * 2f);

        CacheComponents();
        if (m_Collider is BoxCollider box)
        {
            box.isTrigger = true;
            box.size = new Vector3(1f, 3.2f, 1f);
            box.center = new Vector3(0f, 1.2f, 0f);
        }
    }

    public bool ContainsPoint(Vector3 point)
    {
        if (m_Collider == null)
            CacheComponents();

        if (m_Collider != null && m_Collider.bounds.Contains(point))
            return true;

        return Vector2.Distance(new Vector2(point.x, point.z), new Vector2(transform.position.x, transform.position.z)) <= targetRadius;
    }

    public void ShowCorrectFeedback()
    {
        Flash(Color.green);
    }

    public void ShowWrongFeedback()
    {
        Flash(Color.red);
    }

    public void ResetFeedback()
    {
        if (m_FlashRoutine != null)
        {
            StopCoroutine(m_FlashRoutine);
            m_FlashRoutine = null;
        }

        RestoreBaseColors();
    }

    void CacheComponents()
    {
        if (m_Collider == null)
            m_Collider = GetComponent<Collider>();

        if (m_Renderers == null || m_Renderers.Length == 0)
            m_Renderers = GetComponentsInChildren<Renderer>();
    }

    void CaptureBaseColors()
    {
        if (m_Renderers == null)
            return;

        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = ReadColor(m_Renderers[i]);
    }

    void Flash(Color color)
    {
        if (!isActiveAndEnabled)
            return;

        if (m_FlashRoutine != null)
            StopCoroutine(m_FlashRoutine);

        m_FlashRoutine = StartCoroutine(FlashRoutine(color));
    }

    IEnumerator FlashRoutine(Color color)
    {
        SetColor(color);
        yield return new WaitForSeconds(0.45f);
        RestoreBaseColors();
        m_FlashRoutine = null;
    }

    void RestoreBaseColors()
    {
        if (m_Renderers == null || m_BaseColors == null)
            return;

        for (int i = 0; i < m_Renderers.Length && i < m_BaseColors.Length; i++)
            WriteColor(m_Renderers[i], m_BaseColors[i]);
    }

    void SetColor(Color color)
    {
        if (m_Renderers == null)
            return;

        for (int i = 0; i < m_Renderers.Length; i++)
            WriteColor(m_Renderers[i], color);
    }

    static Color ReadColor(Renderer objectRenderer)
    {
        Material material = ColorMaterial(objectRenderer);
        if (material == null)
            return Color.white;

        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    static void WriteColor(Renderer objectRenderer, Color color)
    {
        Material material = ColorMaterial(objectRenderer);
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    static Material ColorMaterial(Renderer objectRenderer)
    {
        if (objectRenderer == null)
            return null;

        return Application.isPlaying ? objectRenderer.material : objectRenderer.sharedMaterial;
    }
}
