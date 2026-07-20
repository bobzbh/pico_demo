using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class XRTrainingSelectable : MonoBehaviour
{
    public XRTrainingManager manager;
    public string displayName = "Object";
    public Color hoverColor = new Color(1f, 0.95f, 0.25f, 1f);

    readonly List<Material> m_Materials = new List<Material>();
    readonly List<Color> m_OriginalColors = new List<Color>();
    XRBaseInteractable m_Interactable;

    void Awake()
    {
        CacheMaterials();
        m_Interactable = GetComponent<XRBaseInteractable>();

        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.AddListener(OnHoverEntered);
        m_Interactable.hoverExited.AddListener(OnHoverExited);
        m_Interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDestroy()
    {
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void CacheMaterials()
    {
        m_Materials.Clear();
        m_OriginalColors.Clear();

        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var objectRenderer in renderers)
        {
            Material[] materials = Application.isPlaying ? objectRenderer.materials : objectRenderer.sharedMaterials;
            foreach (var material in materials)
            {
                if (material == null)
                    continue;

                m_Materials.Add(material);
                m_OriginalColors.Add(ReadColor(material));
            }
        }
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        SetHighlight(true);
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        SetHighlight(false);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (manager != null)
            manager.ShowObjectName(displayName);
    }

    void SetHighlight(bool active)
    {
        for (var i = 0; i < m_Materials.Count; i++)
        {
            var material = m_Materials[i];
            if (material == null)
                continue;

            WriteColor(material, active ? hoverColor : m_OriginalColors[i]);
        }
    }

    static Color ReadColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    static void WriteColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
