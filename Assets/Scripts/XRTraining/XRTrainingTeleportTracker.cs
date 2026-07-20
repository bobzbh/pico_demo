using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public sealed class XRTrainingTeleportTracker : MonoBehaviour
{
    public XRTrainingManager manager;
    public Transform xrOrigin;
    public float teleportDistanceThreshold = 1.1f;
    public float cooldownSeconds = 0.25f;

    TeleportationProvider m_Provider;
    Vector3 m_LastPosition;
    float m_LastTeleportTime = -100f;
    bool m_HasLastPosition;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (m_Provider != null)
            m_Provider.locomotionEnded += OnLocomotionEnded;
    }

    void OnDisable()
    {
        if (m_Provider != null)
            m_Provider.locomotionEnded -= OnLocomotionEnded;
    }

    void Update()
    {
        if (manager == null || manager.CurrentState != XRTrainingTaskState.Running || xrOrigin == null)
        {
            m_HasLastPosition = false;
            return;
        }

        if (!m_HasLastPosition)
        {
            m_LastPosition = xrOrigin.position;
            m_HasLastPosition = true;
            return;
        }

        Vector3 current = xrOrigin.position;
        float distance = Vector2.Distance(new Vector2(m_LastPosition.x, m_LastPosition.z), new Vector2(current.x, current.z));
        if (distance >= teleportDistanceThreshold && Time.unscaledTime - m_LastTeleportTime > cooldownSeconds)
            RecordTeleport(current);

        m_LastPosition = current;
    }

    public void Configure(XRTrainingManager owner, Transform origin)
    {
        manager = owner;
        xrOrigin = origin;
    }

    public void RecordTeleport(Vector3 position)
    {
        m_LastTeleportTime = Time.unscaledTime;
        m_LastPosition = position;
        m_HasLastPosition = true;
        manager?.ReportTeleport(position);
    }

    public void RecordInvalidTeleport(Vector3 position, string reason)
    {
        manager?.ReportInvalidTeleport(position, reason);
    }

    void OnLocomotionEnded(LocomotionProvider provider)
    {
        if (Time.unscaledTime - m_LastTeleportTime <= cooldownSeconds)
            return;

        Vector3 position = xrOrigin != null ? xrOrigin.position : transform.position;
        RecordTeleport(position);
    }

    void ResolveReferences()
    {
        if (manager == null)
            manager = FindObjectOfType<XRTrainingManager>();

        if (xrOrigin == null && manager != null)
            xrOrigin = manager.xrOrigin;

        if (m_Provider == null)
            m_Provider = FindObjectOfType<TeleportationProvider>();
    }
}
