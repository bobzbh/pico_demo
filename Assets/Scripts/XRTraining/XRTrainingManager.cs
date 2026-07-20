using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public sealed class XRTrainingManager : MonoBehaviour
{
    [Header("Scene References")]
    public XRTrainingGrabbable[] grabbables;
    public XRTrainingTargetZone[] targetZones;
    public TeleportationArea finishTeleportArea;
    public BoxCollider finishZone;
    public Transform xrOrigin;
    public Transform headTransform;
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    public Transform leftRayTransform;
    public Transform rightRayTransform;
    public Transform trainingRoot;
    public Light sceneLight;

    [Header("Optional Recording")]
    public XRTrainingDataLogger dataLogger;
    public XRTrainingTeleportTracker teleportTracker;

    [Header("World UI")]
    public Text selectedObjectText;
    public Text scoreText;
    public Text statusText;
    public Text completionText;
    public TextMesh selectedObjectMeshText;
    public TextMesh scoreMeshText;
    public TextMesh statusMeshText;
    public TextMesh completionMeshText;
    public Button startTaskButton;
    public Button resetButton;
    public Button lightButton;
    public Button finishButton;

    readonly XRTrainingRuntimeStats m_Stats = new XRTrainingRuntimeStats();
    Vector3 m_InitialOriginPosition;
    Quaternion m_InitialOriginRotation;
    bool m_HasCapturedStart;
    bool m_HasAlignedScene;

    public XRTrainingTaskState CurrentState { get; private set; } = XRTrainingTaskState.WaitingToStart;
    public bool CanInteractWithObjects => CurrentState == XRTrainingTaskState.Running;
    public bool TaskSolved => m_Stats.correctPlacements >= RequiredScore();

    void Awake()
    {
        ResolveReferences();
    }

    IEnumerator Start()
    {
        ResolveReferences();
        yield return null;
        yield return null;
        AlignTrainingRootToHeadForward();
        CaptureStartState();
        ResetTask();
    }

    void Update()
    {
        HandleKeyboardShortcuts();
        CheckFinishReached();
    }

    public void StartTask()
    {
        ResolveReferences();
        CurrentState = XRTrainingTaskState.Running;
        m_Stats.Clear();

        ResetObjectsOnly();
        SetAllObjectInteraction(true);
        SetFinishUnlocked(false);

        ShowStatus("Task started. Aim at a cube, use Trigger or Grip to grab, then release it on the matching target.");
        RefreshUI();
        LogEvent(XRTrainingEventType.TaskStart, "TaskStart", Vector3.zero, "start");
    }

    public void ResetTask()
    {
        ResolveReferences();
        CaptureStartState();
        CurrentState = XRTrainingTaskState.WaitingToStart;
        m_Stats.resetCount++;
        m_Stats.score = 0;
        m_Stats.correctPlacements = 0;
        m_Stats.wrongPlacements = 0;
        m_Stats.success = false;

        ResetObjectsOnly();
        SetAllObjectInteraction(false);
        SetFinishUnlocked(false);
        MoveOrigin(m_InitialOriginPosition, m_InitialOriginRotation);

        SetText(selectedObjectText, selectedObjectMeshText, "Selected: none");
        ShowStatus("Press Start, then use the simulator controllers to grab the three color cubes.");
        RefreshUI();
        LogEvent(XRTrainingEventType.TaskReset, "Reset", Vector3.zero, "reset");
    }

    public void RestartTask()
    {
        ResetTask();
    }

    public void ToggleLight()
    {
        if (sceneLight == null)
            return;

        sceneLight.enabled = !sceneLight.enabled;
        ShowStatus(sceneLight.enabled ? "Light on." : "Light off.");
        LogEvent(XRTrainingEventType.LightToggled, "Light", Vector3.zero, sceneLight.enabled ? "on" : "off");
    }

    public void ShowObjectName(string objectName)
    {
        SetText(selectedObjectText, selectedObjectMeshText, "Selected: " + objectName);
        LogEvent(XRTrainingEventType.ObjectSelected, objectName, Vector3.zero, "select");
    }

    public void ReportGrab(XRTrainingGrabbable grabbable)
    {
        if (grabbable == null || CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.grabCount++;
        SetText(selectedObjectText, selectedObjectMeshText, "Selected: " + grabbable.displayName);
        ShowStatus("Grabbed " + grabbable.displayName + ".");
        LogEvent(XRTrainingEventType.ObjectGrab, grabbable.displayName, grabbable.transform.position, "grab");
        RefreshUI();
    }

    public void ReportRelease(XRTrainingGrabbable grabbable)
    {
        if (grabbable == null || CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.releaseCount++;
        EvaluatePlacement(grabbable);
        LogEvent(XRTrainingEventType.ObjectRelease, grabbable.displayName, grabbable.transform.position, "release");
        RefreshUI();
    }

    public void ReportInvalidObjectOperation(XRTrainingGrabbable grabbable, string reason)
    {
        string objectName = grabbable != null ? grabbable.displayName : "Object";
        ShowStatus(objectName + ": " + reason);
    }

    public void ReportTeleport(Vector3 position)
    {
        m_Stats.teleportCount++;
        ShowStatus("Teleported.");
        LogEvent(XRTrainingEventType.Teleport, "Teleport", position, "teleport");
        CheckFinishReached();
        RefreshUI();
    }

    public void ReportInvalidTeleport(Vector3 position, string reason)
    {
        ShowStatus("Cannot teleport: " + reason);
        LogEvent(XRTrainingEventType.InvalidTeleport, "Teleport", position, reason);
    }

    public void TryTeleportFromRay()
    {
        Transform rayTransform = rightRayTransform != null ? rightRayTransform : leftRayTransform;
        if (rayTransform == null)
        {
            ReportInvalidTeleport(Vector3.zero, "ray transform missing");
            return;
        }

        if (!Physics.Raycast(rayTransform.position, rayTransform.forward, out var hitInfo, 12f, ~0, QueryTriggerInteraction.Collide))
        {
            ReportInvalidTeleport(rayTransform.position, "aim at Start, Operation, or Finish floor");
            return;
        }

        var teleportArea = hitInfo.collider.GetComponentInParent<TeleportationArea>();
        if (teleportArea == null)
        {
            ReportInvalidTeleport(hitInfo.point, "only marked floors accept teleport");
            return;
        }

        bool hitFinish = hitInfo.collider.transform.root.name.Contains("Finish") || hitInfo.collider.name.Contains("Finish");
        if (hitFinish && !TaskSolved)
        {
            ReportInvalidTeleport(hitInfo.point, "finish is locked until all cubes are matched");
            return;
        }

        TeleportTo(hitInfo.point);
    }

    public void TryTeleportToFinish()
    {
        if (!TaskSolved)
        {
            ReportInvalidTeleport(xrOrigin != null ? xrOrigin.position : Vector3.zero, "finish is locked until all cubes are matched");
            return;
        }

        if (finishZone == null)
        {
            ReportInvalidTeleport(Vector3.zero, "finish zone missing");
            return;
        }

        TeleportTo(finishZone.bounds.center);
    }

    void EvaluatePlacement(XRTrainingGrabbable grabbable)
    {
        XRTrainingTargetZone zone = grabbable.CurrentZone != null ? grabbable.CurrentZone : FindContainingTarget(grabbable.transform.position);
        if (zone == null)
        {
            ShowStatus(grabbable.displayName + " released outside target zones.");
            return;
        }

        if (grabbable.colorId != zone.colorId)
        {
            m_Stats.wrongPlacements++;
            zone.ShowWrongFeedback();
            ShowStatus(grabbable.displayName + " is on the wrong target.");
            LogEvent(XRTrainingEventType.WrongPlacement, grabbable.displayName, grabbable.transform.position, zone.name);
            return;
        }

        if (grabbable.Scored)
            return;

        grabbable.MarkScored(zone);
        zone.ShowCorrectFeedback();
        m_Stats.correctPlacements++;
        m_Stats.score = m_Stats.correctPlacements;
        ShowStatus("Correct: " + grabbable.displayName + ".");
        LogEvent(XRTrainingEventType.CorrectPlacement, grabbable.displayName, grabbable.transform.position, zone.name);

        if (TaskSolved)
            CompleteMatchingTask();
    }

    void CompleteMatchingTask()
    {
        CurrentState = XRTrainingTaskState.Completed;
        m_Stats.success = true;
        SetAllObjectInteraction(false);
        SetFinishUnlocked(true);
        ShowStatus("Task complete. Aim at the Finish floor and teleport, or click Go Finish.");
        SetText(completionText, completionMeshText, "Task complete. Finish unlocked.");
        LogEvent(XRTrainingEventType.TaskComplete, "Complete", Vector3.zero, "all cubes matched");
        RefreshUI();
    }

    void CheckFinishReached()
    {
        if (!TaskSolved || CurrentState == XRTrainingTaskState.Ended || finishZone == null)
            return;

        Vector3 checkPosition = headTransform != null ? headTransform.position : (xrOrigin != null ? xrOrigin.position : Vector3.zero);
        if (!finishZone.bounds.Contains(checkPosition))
            return;

        CurrentState = XRTrainingTaskState.Ended;
        SetAllObjectInteraction(false);
        SetFinishUnlocked(true);
        ShowStatus("Task finished.");
        SetText(completionText, completionMeshText, "Task finished. Click Restart to run again.");
        LogEvent(XRTrainingEventType.TaskEnded, "Finish", checkPosition, "finish reached");
        RefreshUI();
    }

    void TeleportTo(Vector3 worldPosition)
    {
        if (xrOrigin == null)
            return;

        Vector3 target = worldPosition;
        target.y = m_InitialOriginPosition.y;
        MoveOrigin(target, xrOrigin.rotation);

        if (teleportTracker != null)
            teleportTracker.RecordTeleport(target);
        else
            ReportTeleport(target);
    }

    void MoveOrigin(Vector3 position, Quaternion rotation)
    {
        if (xrOrigin == null)
            return;

        var characterController = xrOrigin.GetComponent<CharacterController>();
        bool controllerWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null)
            characterController.enabled = false;

        xrOrigin.SetPositionAndRotation(position, rotation);

        if (characterController != null)
            characterController.enabled = controllerWasEnabled;
    }

    void ResetObjectsOnly()
    {
        if (grabbables != null)
        {
            foreach (var grabbable in grabbables)
                grabbable?.ResetObject();
        }

        if (targetZones != null)
        {
            foreach (var zone in targetZones)
                zone?.ResetFeedback();
        }
    }

    void SetAllObjectInteraction(bool enabled)
    {
        if (grabbables == null)
            return;

        foreach (var grabbable in grabbables)
            grabbable?.SetInteractionEnabled(enabled);
    }

    void SetFinishUnlocked(bool unlocked)
    {
        if (finishTeleportArea != null)
            finishTeleportArea.enabled = unlocked;

        if (finishButton != null)
            finishButton.interactable = unlocked;
    }

    void HandleKeyboardShortcuts()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            StartTask();

        if (keyboard.rKey.wasPressedThisFrame)
            ResetTask();

        if (keyboard.lKey.wasPressedThisFrame)
            ToggleLight();

        if (keyboard.fKey.wasPressedThisFrame)
            TryTeleportFromRay();
    }

    void CaptureStartState()
    {
        if (m_HasCapturedStart || xrOrigin == null)
            return;

        m_InitialOriginPosition = xrOrigin.position;
        m_InitialOriginRotation = xrOrigin.rotation;
        m_HasCapturedStart = true;

        if (grabbables == null)
            return;

        foreach (var grabbable in grabbables)
            grabbable?.CaptureInitialState();
    }

    void AlignTrainingRootToHeadForward()
    {
        if (m_HasAlignedScene || trainingRoot == null || headTransform == null || xrOrigin == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        Vector3 originPosition = xrOrigin.position;
        trainingRoot.position = new Vector3(originPosition.x, 0f, originPosition.z);
        trainingRoot.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        m_HasAlignedScene = true;
    }

    void ResolveReferences()
    {
        if (xrOrigin == null)
        {
            var origin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
                xrOrigin = origin.transform;
        }

        if (headTransform == null && Camera.main != null)
            headTransform = Camera.main.transform;

        if (dataLogger == null)
            dataLogger = GetComponent<XRTrainingDataLogger>();

        if (teleportTracker == null)
            teleportTracker = GetComponent<XRTrainingTeleportTracker>();

        if (teleportTracker != null)
            teleportTracker.Configure(this, xrOrigin);
    }

    XRTrainingTargetZone FindContainingTarget(Vector3 position)
    {
        if (targetZones == null)
            return null;

        foreach (var targetZone in targetZones)
        {
            if (targetZone != null && targetZone.ContainsPoint(position))
                return targetZone;
        }

        return null;
    }

    int RequiredScore()
    {
        int count = 0;
        if (grabbables != null)
        {
            foreach (var grabbable in grabbables)
            {
                if (grabbable != null)
                    count++;
            }
        }

        return Mathf.Max(1, count);
    }

    void RefreshUI()
    {
        SetText(scoreText, scoreMeshText, "Score: " + m_Stats.correctPlacements + " / " + RequiredScore());

        string completion = "Task not complete.";
        if (CurrentState == XRTrainingTaskState.Completed)
            completion = "Task complete. Finish unlocked.";
        else if (CurrentState == XRTrainingTaskState.Ended)
            completion = "Task ended. Restart to practice again.";

        SetText(completionText, completionMeshText, completion);

        if (startTaskButton != null)
            startTaskButton.interactable = CurrentState != XRTrainingTaskState.Running;

        if (resetButton != null)
            resetButton.interactable = true;

        if (lightButton != null)
            lightButton.interactable = true;

        if (finishButton != null)
            finishButton.interactable = TaskSolved;
    }

    void ShowStatus(string message)
    {
        SetText(statusText, statusMeshText, message);
    }

    void LogEvent(XRTrainingEventType eventType, string objectName, Vector3 position, string details)
    {
        dataLogger?.LogEvent(eventType, CurrentState, objectName, position, 0f, m_Stats, details);
    }

    static void SetText(Text target, TextMesh meshTarget, string value)
    {
        if (target != null)
            target.text = value;

        if (meshTarget != null)
            meshTarget.text = WrapWorldText(value, 36);
    }

    static string WrapWorldText(string value, int maxLineLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLineLength)
            return value;

        var words = value.Split(' ');
        var result = new System.Text.StringBuilder(value.Length + 8);
        int lineLength = 0;
        foreach (string word in words)
        {
            if (lineLength > 0 && lineLength + word.Length + 1 > maxLineLength)
            {
                result.AppendLine();
                lineLength = 0;
            }

            if (lineLength > 0)
            {
                result.Append(' ');
                lineLength++;
            }

            result.Append(word);
            lineLength += word.Length;
        }

        return result.ToString();
    }
}
