using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(XRGrabInteractable))]
public sealed class XRTrainingGrabbable : MonoBehaviour, IXRSelectFilter
{
    public XRTrainingColorId colorId;
    public string displayName = "Cube";
    public XRTrainingManager manager;

    public bool Scored { get; private set; }
    public bool IsHeld { get; private set; }
    public XRTrainingTargetZone CurrentZone { get; private set; }

    Rigidbody m_Rigidbody;
    XRGrabInteractable m_GrabInteractable;
    Transform m_InitialParent;
    Vector3 m_InitialPosition;
    Quaternion m_InitialRotation;
    Vector3 m_InitialScale;
    bool m_InteractionEnabled;

    public bool canProcess => isActiveAndEnabled;

    void Awake()
    {
        CacheComponents();
        CaptureInitialState();
    }

    void OnEnable()
    {
        CacheComponents();
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
        m_InitialParent = transform.parent;
        m_InitialPosition = transform.position;
        m_InitialRotation = transform.rotation;
        m_InitialScale = transform.localScale;
        Scored = false;
        IsHeld = false;
        CurrentZone = null;
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

        m_Rigidbody.velocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.useGravity = true;
        m_Rigidbody.isKinematic = !m_InteractionEnabled;
        m_Rigidbody.Sleep();
    }

    public void SetInteractionEnabled(bool enabled)
    {
        CacheComponents();
        m_InteractionEnabled = enabled;
        m_GrabInteractable.enabled = true;
        m_Rigidbody.useGravity = true;
        m_Rigidbody.isKinematic = !enabled || Scored;
    }

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        return CanBeGrabbed();
    }

    public void SetCurrentZone(XRTrainingTargetZone zone)
    {
        CurrentZone = zone;
    }

    public void MarkScored(XRTrainingTargetZone zone)
    {
        Scored = true;
        IsHeld = false;
        SnapToTarget(zone);
        SetInteractionEnabled(false);
    }

    public void SnapToTarget(XRTrainingTargetZone zone)
    {
        if (zone == null)
            return;

        float halfHeight = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z) * 0.5f;
        Vector3 targetPosition = zone.transform.position + Vector3.up * (0.08f + halfHeight);
        transform.SetPositionAndRotation(targetPosition, Quaternion.identity);

        if (m_Rigidbody == null)
            return;

        m_Rigidbody.velocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.isKinematic = true;
    }

    public bool TryBeginManualGrab()
    {
        if (!CanBeGrabbed())
        {
            manager?.ReportInvalidObjectOperation(this, "Press Start first.");
            return false;
        }

        IsHeld = true;
        m_Rigidbody.velocity = Vector3.zero;
        m_Rigidbody.angularVelocity = Vector3.zero;
        m_Rigidbody.isKinematic = true;
        manager?.ReportGrab(this);
        return true;
    }

    public void UpdateManualGrab(Vector3 worldPosition)
    {
        if (!IsHeld)
            return;

        transform.position = worldPosition;
        if (m_Rigidbody != null)
            m_Rigidbody.position = worldPosition;
    }

    public void EndManualGrab()
    {
        if (!IsHeld)
            return;

        IsHeld = false;
        if (m_Rigidbody != null)
            m_Rigidbody.isKinematic = !m_InteractionEnabled || Scored;

        manager?.ReportRelease(this);
    }

    bool CanBeGrabbed()
    {
        if (Scored || !m_InteractionEnabled)
            return false;

        return manager == null || manager.CanInteractWithObjects;
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!CanBeGrabbed())
        {
            manager?.ReportInvalidObjectOperation(this, Scored ? "Already placed." : "Press Start first.");
            return;
        }

        IsHeld = true;
        manager?.ShowObjectName(displayName);
        manager?.ReportGrab(this);
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (!IsHeld)
            return;

        IsHeld = false;
        manager?.ReportRelease(this);
    }

    void CacheComponents()
    {
        if (m_Rigidbody == null)
            m_Rigidbody = GetComponent<Rigidbody>();

        if (m_GrabInteractable == null)
            m_GrabInteractable = GetComponent<XRGrabInteractable>();
    }
}
