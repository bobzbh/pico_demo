using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(InputField))]
public sealed class XRTrainingUserIdInput : MonoBehaviour
{
    public XRTrainingManager manager;
    InputField m_Input;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (m_Input != null)
            m_Input.onEndEdit.AddListener(OnEndEdit);
    }

    void OnDisable()
    {
        if (m_Input != null)
            m_Input.onEndEdit.RemoveListener(OnEndEdit);
    }

    void ResolveReferences()
    {
        if (m_Input == null)
            m_Input = GetComponent<InputField>();

        if (manager == null)
            manager = FindObjectOfType<XRTrainingManager>();

        if (m_Input != null && manager != null)
            m_Input.text = manager.userId;
    }

    void OnEndEdit(string value)
    {
        if (manager != null)
            manager.SetUserId(value);
    }
}
