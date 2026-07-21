using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class XRTrainingMouseGrabber : MonoBehaviour
{
    public XRTrainingManager manager;
    public Camera eventCamera;
    public float maxRayDistance = 30f;
    public LayerMask raycastMask = ~0;
    public bool autoStartOnMouseGrab;

    XRTrainingGrabbable m_Held;
    float m_HoldPlaneY;
    Vector3 m_GrabOffset;

    void Awake()
    {
        ResolveReferences();
    }

    void Update()
    {
        ResolveReferences();

        if (manager == null)
            return;

        var mouse = Mouse.current;
        if (mouse == null || eventCamera == null)
        {
            if (m_Held != null)
                ForceRelease();
            return;
        }

        if (m_Held != null && manager.CurrentState != XRTrainingTaskState.Running)
        {
            ForceRelease();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
            TryHandlePrimaryPress(mouse.position.ReadValue());

        if (m_Held != null)
        {
            UpdateGrab(mouse.position.ReadValue());

            if (mouse.leftButton.wasReleasedThisFrame)
                ForceRelease();
        }
    }

    void TryHandlePrimaryPress(Vector2 screenPosition)
    {
        if (eventCamera == null)
            return;

        Ray ray = eventCamera.ScreenPointToRay(screenPosition);
        if (TryClickPanelButton(ray))
            return;

        TryBeginGrab(ray, screenPosition);
    }

    void TryBeginGrab(Ray ray, Vector2 screenPosition)
    {
        if (eventCamera == null)
            return;

        if (!TryFindGrabbable(ray, out var grabbable, out var hitPoint))
        {
            if (manager != null && manager.CurrentState == XRTrainingTaskState.Running)
                manager.ReportInvalidObjectOperation(null, "Point at a cube, then hold LEFT MOUSE.");

            return;
        }

        if (autoStartOnMouseGrab && manager != null && manager.CurrentState == XRTrainingTaskState.WaitingToStart)
            manager.StartTask();

        if (grabbable == null || !grabbable.TryBeginManualGrab())
            return;

        m_Held = grabbable;
        m_HoldPlaneY = grabbable.transform.position.y;

        if (ray.direction.y != 0f)
        {
            float planeDistance = (m_HoldPlaneY - ray.origin.y) / ray.direction.y;
            Vector3 rayPoint = ray.GetPoint(Mathf.Max(0f, planeDistance));
            m_GrabOffset = grabbable.transform.position - rayPoint;
        }
        else
        {
            m_GrabOffset = grabbable.transform.position - hitPoint;
        }

        UpdateGrab(screenPosition);
    }

    bool TryClickPanelButton(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (var hit in hits)
        {
            var panelButton = hit.collider != null ? hit.collider.GetComponentInParent<XRTrainingPanelButton>() : null;
            if (panelButton == null || !panelButton.gameObject.activeInHierarchy)
                continue;

            panelButton.InvokeAction();
            return true;
        }

        return false;
    }

    void UpdateGrab(Vector2 screenPosition)
    {
        if (m_Held == null || eventCamera == null)
            return;

        Ray ray = eventCamera.ScreenPointToRay(screenPosition);
        float planeDistance = RayPlaneDistance(ray, m_HoldPlaneY);
        if (float.IsNaN(planeDistance))
            return;

        Vector3 target = ray.GetPoint(planeDistance) + m_GrabOffset;
        target.y = m_HoldPlaneY;
        m_Held.UpdateManualGrab(target);
    }

    void ForceRelease()
    {
        if (m_Held == null)
            return;

        m_Held.EndManualGrab();
        m_Held = null;
        m_GrabOffset = Vector3.zero;
    }

    bool TryFindGrabbable(Ray ray, out XRTrainingGrabbable grabbable, out Vector3 hitPoint)
    {
        grabbable = null;
        hitPoint = Vector3.zero;

        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (var hit in hits)
        {
            var candidate = hit.collider != null ? hit.collider.GetComponentInParent<XRTrainingGrabbable>() : null;
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
                continue;

            grabbable = candidate;
            hitPoint = hit.point;
            return true;
        }

        return false;
    }

    static float RayPlaneDistance(Ray ray, float planeY)
    {
        if (Mathf.Approximately(ray.direction.y, 0f))
            return float.NaN;

        float distance = (planeY - ray.origin.y) / ray.direction.y;
        return distance >= 0f ? distance : float.NaN;
    }

    void ResolveReferences()
    {
        if (manager == null)
            manager = FindObjectOfType<XRTrainingManager>();

        if (eventCamera == null && Camera.main != null)
            eventCamera = Camera.main;
    }
}
