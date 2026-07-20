using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Rigidbody))]
public sealed class XRTrainingGrabbable : MonoBehaviour, IXRSelectFilter
{
    public XRTrainingColorId colorId;
    public string displayName = "Object";
    public XRTrainingManager manager;
    public bool isDistractor;

    public bool Scored { get; private set; }
    public bool IsHeld { get; private set; }
    public XRTrainingTargetZone CurrentZone { get; private set; }

    Rigidbody m_Rigidbody;
    XRGrabInteractable m_GrabInteractable;
    Renderer[] m_Renderers;
    Color[] m_BaseColors;
    Vector3 m_InitialPosition;
    Quaternion m_InitialRotation;
    Transform m_InitialParent;
    Vector3 m_InitialScale;
    bool m_InteractionAllowed;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        CacheComponents();
        CaptureInitialState();
    }

    void OnEnable()
    {
        CacheComponents();
        if (m_GrabInteractable == null)
            return;

        m_GrabInteractable.enabled = true;
        m_GrabInteractable.selectFilters.Remove(this);
        m_GrabInteractable.selectFilters.Add(this);
        m_GrabInteractable.selectEntered.AddListener(OnSelectEntered);
        m_GrabInteractable.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        if (m_GrabInteractable == null)
            return;

        m_GrabInteractable.selectFilters.Remove(this);
        m_GrabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        m_GrabInteractable.selectExited.RemoveListener(OnSelectExited);
    }

    public void CaptureInitialState()
    {
        CacheComponents();
        m_InitialPosition = transform.position;
        m_InitialRotation = transform.rotation;
        m_InitialParent = transform.parent;
        m_InitialScale = transform.localScale;
        Scored = false;
        IsHeld = false;
        CurrentZone = null;
        CaptureBaseColors();
    }

    public void MarkScored()
    {
        Scored = true;
        IsHeld = false;
        SetInteractionEnabled(false);
    }

    public void ClearScore()
    {
        Scored = false;
    }

    public void SetRoundStartPose(Vector3 position, Quaternion rotation, Vector3 scale, bool active)
    {
        gameObject.SetActive(active);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = scale;
        CaptureInitialState();
        ResetObject();
    }

    public void ResetObject()
    {
        CacheComponents();
        transform.SetParent(m_InitialParent, true);
        transform.SetPositionAndRotation(m_InitialPosition, m_InitialRotation);
        transform.localScale = m_InitialScale;
        Scored = false;
        IsHeld = false;
        CurrentZone = null;
        RestoreBaseColors();

        if (m_Rigidbody == null)
            return;

        m_Rigidbody.velocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.Sleep();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        CacheComponents();
        m_InteractionAllowed = enabled;

        if (m_GrabInteractable != null)
            m_GrabInteractable.enabled = true;

        if (m_Rigidbody != null)
            m_Rigidbody.isKinematic = !enabled || Scored;
    }

    public bool TryBeginManualGrab()
    {
        CacheComponents();

        if (!CanBeSelected())
        {
            ReportLockedOperation();
            return false;
        }

        IsHeld = true;
        SetHeldVisual(true);

        if (m_Rigidbody != null)
        {
            m_Rigidbody.velocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.isKinematic = true;
        }

        manager?.ReportGrab(this);
        return true;
    }

    public void UpdateManualGrab(Vector3 worldPosition)
    {
        if (!IsHeld)
            return;

        if (m_Rigidbody != null && m_Rigidbody.isKinematic)
            m_Rigidbody.position = worldPosition;

        transform.position = worldPosition;
    }

    public void EndManualGrab()
    {
        if (!IsHeld)
            return;

        IsHeld = false;
        SetHeldVisual(false);
        manager?.ReportRelease(this);

        if (m_Rigidbody != null)
        {
            m_Rigidbody.velocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
            m_Rigidbody.isKinematic = !m_InteractionAllowed || Scored;
        }
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        return CanBeSelected();
    }

    public void SetCurrentZone(XRTrainingTargetZone zone)
    {
        CurrentZone = zone;
    }

    public void SnapToTarget(XRTrainingTargetZone zone)
    {
        if (zone == null)
            return;

        float halfHeight = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z) * 0.5f;
        transform.SetPositionAndRotation(zone.transform.position + Vector3.up * (0.08f + halfHeight), Quaternion.identity);

        if (m_Rigidbody != null)
        {
            m_Rigidbody.velocity = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!CanBeSelected())
        {
            ReportLockedOperation();
            return;
        }

        IsHeld = true;
        SetHeldVisual(true);
        manager?.ReportGrab(this);
    }

    bool CanBeSelected()
    {
        if (Scored || !m_InteractionAllowed)
            return false;

        return manager == null || manager.CanInteractWithObjects;
    }

    void ReportLockedOperation()
    {
        if (manager == null)
            return;

        string reason = Scored ? "Already placed." : "Press Start Task first.";
        manager.ReportInvalidObjectOperation(this, reason);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!IsHeld && manager != null && !manager.CanInteractWithObjects)
            return;

        IsHeld = false;
        SetHeldVisual(false);
        manager?.ReportRelease(this);
    }

    void CacheComponents()
    {
        if (m_Rigidbody == null)
            m_Rigidbody = GetComponent<Rigidbody>();

        if (m_GrabInteractable == null)
            m_GrabInteractable = GetComponent<XRGrabInteractable>();

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

    void RestoreBaseColors()
    {
        if (m_Renderers == null || m_BaseColors == null)
            return;

        for (int i = 0; i < m_Renderers.Length && i < m_BaseColors.Length; i++)
            WriteColor(m_Renderers[i], m_BaseColors[i]);
    }

    void SetHeldVisual(bool held)
    {
        if (m_Renderers == null || m_BaseColors == null)
            return;

        for (int i = 0; i < m_Renderers.Length && i < m_BaseColors.Length; i++)
        {
            var color = held ? Color.Lerp(m_BaseColors[i], Color.white, 0.45f) : m_BaseColors[i];
            WriteColor(m_Renderers[i], color);
        }
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
