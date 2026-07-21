using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum XRTrainingPanelAction
{
    Start,
    Reset,
    ToggleLight,
    GoFinish
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class XRTrainingPanelButton : MonoBehaviour
{
    public XRTrainingManager manager;
    public XRTrainingPanelAction action;
    public Button visualButton;
    public float cooldownSeconds = 0.2f;

    XRSimpleInteractable m_Interactable;
    float m_LastInvokeTime = -999f;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();

        if (m_Interactable != null)
            m_Interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        if (m_Interactable != null)
            m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnMouseDown()
    {
        InvokeAction();
    }

    public void InvokeAction()
    {
        if (manager == null)
            ResolveReferences();

        if (manager == null)
            return;

        if (Time.unscaledTime - m_LastInvokeTime < cooldownSeconds)
            return;

        m_LastInvokeTime = Time.unscaledTime;

        switch (action)
        {
            case XRTrainingPanelAction.Start:
                manager.StartTask();
                break;
            case XRTrainingPanelAction.Reset:
                manager.ResetTask();
                break;
            case XRTrainingPanelAction.ToggleLight:
                manager.ToggleLight();
                break;
            case XRTrainingPanelAction.GoFinish:
                manager.TryTeleportToFinish();
                break;
        }
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        InvokeAction();
    }

    void ResolveReferences()
    {
        if (manager == null)
            manager = FindObjectOfType<XRTrainingManager>();

        var hitbox = GetComponent<Collider>();
        if (hitbox != null)
            hitbox.isTrigger = true;

        if (m_Interactable == null)
            m_Interactable = GetComponent<XRSimpleInteractable>();

        if (m_Interactable == null)
            m_Interactable = gameObject.AddComponent<XRSimpleInteractable>();

        m_Interactable.interactionLayers = InteractionLayerMask.GetMask("Default");
    }
}
