using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class XRTrainingTargetZone : MonoBehaviour
{
    public XRTrainingColorId colorId;
    public XRTrainingManager manager;
    public GameObject labelObject;

    Renderer[] m_Renderers;
    Color[] m_BaseColors;
    Coroutine m_FlashRoutine;

    void Awake()
    {
        CacheRenderers();
        CaptureBaseColors();
        var zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null)
            zoneCollider.isTrigger = true;
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

    public bool ContainsPoint(Vector3 point)
    {
        var zoneCollider = GetComponent<Collider>();
        if (zoneCollider != null && zoneCollider.bounds.Contains(point))
            return true;

        Vector2 pointXZ = new Vector2(point.x, point.z);
        Vector2 zoneXZ = new Vector2(transform.position.x, transform.position.z);
        return Vector2.Distance(pointXZ, zoneXZ) <= 0.55f;
    }

    public void ShowCorrectFeedback()
    {
        Flash(Color.white);
    }

    public void ShowWrongFeedback()
    {
        Flash(new Color(1f, 0.1f, 0.1f, 1f));
    }

    public void ResetFeedback()
    {
        if (m_FlashRoutine != null)
        {
            StopCoroutine(m_FlashRoutine);
            m_FlashRoutine = null;
        }

        if (m_BaseColors == null || m_BaseColors.Length == 0)
            CaptureBaseColors();

        RestoreBaseColors();
    }

    public void SetLayoutActive(bool active)
    {
        gameObject.SetActive(active);
        if (labelObject != null)
            labelObject.SetActive(active);
    }

    public void UpdateLabelPosition(Vector3 worldPosition)
    {
        if (labelObject != null)
            labelObject.transform.position = worldPosition;
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
        yield return new WaitForSeconds(0.25f);
        RestoreBaseColors();
        m_FlashRoutine = null;
    }

    void CacheRenderers()
    {
        if (m_Renderers == null || m_Renderers.Length == 0)
            m_Renderers = GetComponentsInChildren<Renderer>();
    }

    void CaptureBaseColors()
    {
        CacheRenderers();
        m_BaseColors = new Color[m_Renderers.Length];
        for (int i = 0; i < m_Renderers.Length; i++)
            m_BaseColors[i] = ReadColor(m_Renderers[i]);
    }

    void RestoreBaseColors()
    {
        CacheRenderers();
        for (int i = 0; i < m_Renderers.Length && i < m_BaseColors.Length; i++)
            WriteColor(m_Renderers[i], m_BaseColors[i]);
    }

    void SetColor(Color color)
    {
        CacheRenderers();
        for (int i = 0; i < m_Renderers.Length; i++)
            WriteColor(m_Renderers[i], color);
    }

    static Color ReadColor(Renderer objectRenderer)
    {
        var material = Application.isPlaying ? objectRenderer.material : objectRenderer.sharedMaterial;
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
        var material = Application.isPlaying ? objectRenderer.material : objectRenderer.sharedMaterial;
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
