using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    public Transform panelRoot;
    public Light sceneLight;

    [Header("Task Flow")]
    public float instructionSeconds = 1f;
    public float timeLimitSeconds = 5f;
    public float resultPanelDistance = 2f;
    public float resultPanelHeightOffset = 0.02f;
    public string userId = "P001";
    public string taskId = "ColorBlockTask";
    public XRTrainingDifficultyConfig difficultyConfig = new XRTrainingDifficultyConfig();
    public XRTrainingExperimentCondition experimentCondition = XRTrainingExperimentCondition.LLMAssisted;
    public float aiIdleHintSeconds = 12f;

    [Header("Optional Recording")]
    public XRTrainingDataLogger dataLogger;
    public XRTrainingTeleportTracker teleportTracker;
    public XRTrainingAIAssistant aiAssistant;
    public InputField userIdInput;

    [Header("World UI")]
    public Text selectedObjectText;
    public Text scoreText;
    public Text difficultyText;
    public Text conditionText;
    public Text statusText;
    public Text completionText;
    public Text aiText;
    public TextMesh selectedObjectMeshText;
    public TextMesh scoreMeshText;
    public TextMesh difficultyMeshText;
    public TextMesh conditionMeshText;
    public TextMesh statusMeshText;
    public TextMesh completionMeshText;
    public TextMesh aiMeshText;
    public Button startTaskButton;
    public Button easyDifficultyButton;
    public Button normalDifficultyButton;
    public Button conditionButton;
    public Button hintButton;
    public Button resetButton;
    public Button lightButton;
    public Button finishButton;

    readonly XRTrainingRuntimeStats m_Stats = new XRTrainingRuntimeStats();
    Vector3 m_InitialOriginPosition;
    Quaternion m_InitialOriginRotation;
    Vector3 m_InitialPanelLocalPosition;
    Quaternion m_InitialPanelLocalRotation;
    Vector3 m_InitialPanelLocalScale;
    bool m_HasCapturedStart;
    bool m_HasCapturedPanel;
    bool m_HasAlignedScene;
    bool m_TimerRunning;
    bool m_TrialRecordingActive;
    bool m_CompletionEventLogged;
    bool m_ResultsEventLogged;
    float m_TaskStartTime;
    float m_StateEnteredTime;
    float m_LastMeaningfulActionTime;
    int m_TrialNumber;
    string m_FailureReason = "";
    string m_CurrentAIHintText = "Start a round and request help if needed.";
    string m_CurrentAISummaryText = "AI summary will appear after the round.";
    string m_LastEventType = "";
    string m_LastEventObjectName = "";
    string m_LastEventDetails = "";
    string m_LastAITrigger = "";
    string m_LastAISnapshotJson = "";
    bool m_LastAIRequestWasSummary;
    bool m_IdleHintRequested;
    bool m_SummaryRequested;
    readonly List<string> m_RecentEvents = new List<string>(12);

    public XRTrainingTaskState CurrentState { get; private set; } = XRTrainingTaskState.WaitingToStart;
    public bool CanInteractWithObjects => CurrentState == XRTrainingTaskState.Running;
    public bool TaskSolved => m_Stats.correctPlacements >= RequiredScore();
    public string CurrentConditionLabel => experimentCondition == XRTrainingExperimentCondition.LLMAssisted ? "LLM-Assisted" : "No AI";

    void Awake()
    {
        ResolveReferences();
        m_LastMeaningfulActionTime = Time.unscaledTime;
    }

    IEnumerator Start()
    {
        ResolveReferences();
        yield return null;
        yield return null;
        AlignTrainingRootToHeadForward();
        CaptureStartState();
        ResetTaskInternal(false);
    }

    void Update()
    {
        HandleInstructionCountdown();
        UpdateTimer();
        HandleAIIdleTrigger();
        HandleKeyboardShortcuts();
        CheckFinishReached();
    }

    public void StartTask()
    {
        ResolveReferences();

        if (CurrentState == XRTrainingTaskState.Instructions || CurrentState == XRTrainingTaskState.Running)
            return;

        if (CurrentState == XRTrainingTaskState.Completed)
        {
            ShowStatus("Task already complete. Go to Finish or click Reset for a new round.");
            RefreshUI();
            return;
        }

        PrepareRoundForStart();
        BeginTrialRecording();
        m_LastMeaningfulActionTime = Time.unscaledTime;
        m_IdleHintRequested = false;
        m_SummaryRequested = false;
        m_CurrentAIHintText = experimentCondition == XRTrainingExperimentCondition.LLMAssisted
            ? "AI: task started. You can request a hint."
            : "AI: disabled for this trial.";
        m_CurrentAISummaryText = "AI summary will appear after the round.";
        m_LastAISnapshotJson = "";
        EnterState(XRTrainingTaskState.Instructions, "Read the goal: put each color cube on the matching target.");
        SetText(selectedObjectText, selectedObjectMeshText, "Selected: none");
        SetText(completionText, completionMeshText, "State: Instructions. Task will begin shortly.");
        LogEvent(XRTrainingEventType.TaskInstruction, "Instructions", Vector3.zero, "color matching instructions");
        RefreshUI();

        if (instructionSeconds <= 0f)
            BeginRunningState();
    }

    public void ResetTask()
    {
        if (IsResultPageState())
            ReturnToMainMenu();
        else
            RestartTask();
    }

    public void ReturnToMainMenu()
    {
        RestartTask();
        ShowStatus("Main menu. Choose a difficulty or start the next task.");
        RefreshUI();
    }

    public void RestartTask()
    {
        ResolveReferences();

        if (CurrentState != XRTrainingTaskState.WaitingToStart)
            EnterState(XRTrainingTaskState.Restarting, "Restarting task. Resetting score, timer, objects, and state.");

        if (m_TrialRecordingActive)
        {
            StopTimer();
            m_Stats.success = false;
            m_Stats.resetCount++;
            LogEvent(XRTrainingEventType.TaskReset, "Reset", Vector3.zero, "manual restart");
            dataLogger?.CompleteTrial(CurrentState, m_Stats, XRTrainingEventType.TaskReset.ToString(), "manual restart");
            m_TrialRecordingActive = false;
        }

        ResetTaskInternal(true);
    }

    public void SelectEasyDifficulty()
    {
        SelectDifficulty(XRTrainingDifficulty.Easy);
    }

    public void SelectNormalDifficulty()
    {
        SelectDifficulty(XRTrainingDifficulty.Normal);
    }

    public void SelectDifficulty(XRTrainingDifficulty difficulty)
    {
        ResolveReferences();

        if (!CanChangeDifficulty())
        {
            ShowStatus("Difficulty can be changed before a task starts.");
            RefreshUI();
            return;
        }

        difficultyConfig = CreateDifficultyConfig(difficulty);
        timeLimitSeconds = difficultyConfig.timeLimitSeconds;
        ResetTaskInternal(true);
        ShowStatus("Difficulty set: " + DifficultyLabel() + ".");
        RefreshUI();
    }

    public void SetUserId(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "P001" : value.Trim();
        userId = normalized;
        if (userIdInput != null && userIdInput.text != normalized)
            userIdInput.text = normalized;
        RefreshUI();
    }

    public void ToggleExperimentCondition()
    {
        if (!CanChangeDifficulty())
        {
            ShowStatus("Condition can be changed before a task starts.");
            RefreshUI();
            return;
        }

        experimentCondition = experimentCondition == XRTrainingExperimentCondition.NoAI
            ? XRTrainingExperimentCondition.LLMAssisted
            : XRTrainingExperimentCondition.NoAI;
        LogEvent(XRTrainingEventType.ExperimentConditionChanged, "Condition", Vector3.zero, CurrentConditionLabel);
        ShowStatus("Experiment condition: " + CurrentConditionLabel + ".");
        RefreshUI();
    }

    public void RequestAIHint()
    {
        if (experimentCondition != XRTrainingExperimentCondition.LLMAssisted)
        {
            ShowStatus("AI is disabled in No-AI condition.");
            RefreshUI();
            return;
        }

        if (CurrentState != XRTrainingTaskState.Running && CurrentState != XRTrainingTaskState.Completed && CurrentState != XRTrainingTaskState.Failed && CurrentState != XRTrainingTaskState.Results)
        {
            ShowStatus("Start the task before requesting an AI hint.");
            RefreshUI();
            return;
        }

        RequestAIResponse(false, "UserRequestedHint");
    }

    public void ToggleLight()
    {
        if (IsResultPageState())
        {
            SwitchDifficulty();
            return;
        }

        if (sceneLight == null)
            return;

        sceneLight.enabled = !sceneLight.enabled;
        ShowStatus(sceneLight.enabled ? "Light on." : "Light off.");
        LogEvent(XRTrainingEventType.LightToggled, "Light", Vector3.zero, sceneLight.enabled ? "on" : "off");
        RefreshUI();
    }

    public void SwitchDifficulty()
    {
        XRTrainingDifficulty next = CurrentDifficulty() == XRTrainingDifficulty.Easy
            ? XRTrainingDifficulty.Normal
            : XRTrainingDifficulty.Easy;

        SelectDifficulty(next);
    }

    public void ShowObjectName(string objectName)
    {
        if (CurrentState != XRTrainingTaskState.Running)
            return;

        SetText(selectedObjectText, selectedObjectMeshText, "Selected: " + objectName);
        LogEvent(XRTrainingEventType.ObjectSelected, objectName, Vector3.zero, "select");
    }

    public void ReportGrab(XRTrainingGrabbable grabbable)
    {
        if (grabbable == null || CurrentState != XRTrainingTaskState.Running)
            return;

        MarkMeaningfulAction();
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

        MarkMeaningfulAction();
        m_Stats.releaseCount++;
        LogEvent(XRTrainingEventType.ObjectRelease, grabbable.displayName, grabbable.transform.position, "release");
        EvaluatePlacement(grabbable);
        RefreshUI();
    }

    public void ReportInvalidObjectOperation(XRTrainingGrabbable grabbable, string reason)
    {
        if (CurrentState == XRTrainingTaskState.Running)
            MarkMeaningfulAction();
        string objectName = grabbable != null ? grabbable.displayName : "Object";
        ShowStatus(objectName + ": " + reason);
        RefreshUI();
    }

    public void ReportTeleport(Vector3 position)
    {
        if (CurrentState != XRTrainingTaskState.Running && CurrentState != XRTrainingTaskState.Completed)
            return;

        MarkMeaningfulAction();
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
        RefreshUI();
    }

    public void TryTeleportFromRay()
    {
        if (CurrentState != XRTrainingTaskState.Running && CurrentState != XRTrainingTaskState.Completed)
        {
            ReportInvalidTeleport(xrOrigin != null ? xrOrigin.position : Vector3.zero, "start the task before teleporting");
            return;
        }

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
        if (hitFinish && CurrentState != XRTrainingTaskState.Completed)
        {
            ReportInvalidTeleport(hitInfo.point, "finish is locked until all cubes are matched");
            return;
        }

        TeleportTo(hitInfo.point);
    }

    public void TryTeleportToFinish()
    {
        if (CurrentState != XRTrainingTaskState.Completed)
        {
            ReportInvalidTeleport(xrOrigin != null ? xrOrigin.position : Vector3.zero, "finish is locked until the task is complete");
            return;
        }

        if (finishZone == null)
        {
            ReportInvalidTeleport(Vector3.zero, "finish zone missing");
            return;
        }

        TeleportTo(finishZone.bounds.center);
    }

    public void FailTask(string reason)
    {
        if (CurrentState != XRTrainingTaskState.Running && CurrentState != XRTrainingTaskState.Instructions)
            return;

        StopTimer();
        m_Stats.success = false;
        m_FailureReason = reason;
        SetAllObjectInteraction(false);
        SetFinishUnlocked(false);
        EnterState(XRTrainingTaskState.Failed, "Task failed: " + FailureStatusText(reason));
        PlaceResultPanelInFrontOfHead();
        SetText(completionText, completionMeshText, "State: Failed. Click Reset to try again.");
        LogEvent(XRTrainingEventType.TaskFailed, "Failed", Vector3.zero, reason);
        dataLogger?.CompleteTrial(CurrentState, m_Stats, XRTrainingEventType.TaskFailed.ToString(), reason);
        m_TrialRecordingActive = false;
        RefreshUI();
    }

    void HandleInstructionCountdown()
    {
        if (CurrentState != XRTrainingTaskState.Instructions)
            return;

        if (Time.unscaledTime - m_StateEnteredTime >= Mathf.Max(0f, instructionSeconds))
            BeginRunningState();
        else
            RefreshUI();
    }

    void BeginRunningState()
    {
        if (CurrentState != XRTrainingTaskState.Instructions)
            return;

        m_TaskStartTime = Time.unscaledTime;
        m_Stats.elapsedSeconds = 0f;
        m_TimerRunning = true;
        m_LastMeaningfulActionTime = Time.unscaledTime;
        m_IdleHintRequested = false;
        m_LastAISnapshotJson = "";
        SetAllObjectInteraction(true);
        SetFinishUnlocked(false);
        EnterState(XRTrainingTaskState.Running, "Task running. Grab cubes and place them on matching targets before time runs out.");
        LogEvent(XRTrainingEventType.TaskStart, "TaskStart", Vector3.zero, "timer started; limit=" + TimerLimitText());
        dataLogger?.WritePoseSample(CurrentState, m_Stats.elapsedSeconds);
        RefreshUI();
    }

    void PrepareRoundForStart()
    {
        ApplyDifficultyLayout();
        m_Stats.Clear();
        m_TimerRunning = false;
        m_CompletionEventLogged = false;
        m_ResultsEventLogged = false;
        m_FailureReason = "";
        m_IdleHintRequested = false;
        m_SummaryRequested = false;
        m_CurrentAIHintText = experimentCondition == XRTrainingExperimentCondition.LLMAssisted
            ? "Start a round and request a hint when needed."
            : "AI: disabled for this trial.";
        m_CurrentAISummaryText = "AI summary will appear after the round.";
        m_LastAISnapshotJson = "";
        m_RecentEvents.Clear();
        SetAllObjectInteraction(false);
        ResetObjectsOnly();
        SetFinishUnlocked(false);
        RestorePanelPlacement();
        MoveOrigin(m_InitialOriginPosition, m_InitialOriginRotation);
    }

    void ResetTaskInternal(bool userInitiated)
    {
        ResolveReferences();
        ApplyDifficultyLayout();
        CaptureStartState();
        StopTimer();
        dataLogger?.EndTrial();
        m_TrialRecordingActive = false;
        m_CompletionEventLogged = false;
        m_ResultsEventLogged = false;
        m_FailureReason = "";
        m_Stats.Clear();
        m_LastAISnapshotJson = "";
        m_RecentEvents.Clear();
        m_CurrentAIHintText = experimentCondition == XRTrainingExperimentCondition.LLMAssisted
            ? "Start a round and request a hint when needed."
            : "AI: disabled for this trial.";
        m_CurrentAISummaryText = "AI summary will appear after the round.";

        SetAllObjectInteraction(false);
        ResetObjectsOnly();
        SetFinishUnlocked(false);
        RestorePanelPlacement();
        MoveOrigin(m_InitialOriginPosition, m_InitialOriginRotation);

        SetText(selectedObjectText, selectedObjectMeshText, "Selected: none");
        EnterState(XRTrainingTaskState.WaitingToStart, userInitiated ? "Reset complete. Click Start for the next round." : "Click Start to begin the training task.");
        SetText(completionText, completionMeshText, "State: Waiting to start.");
        RefreshUI();
    }

    void EvaluatePlacement(XRTrainingGrabbable grabbable)
    {
        if (CurrentState != XRTrainingTaskState.Running || m_CompletionEventLogged)
            return;

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
            MarkMeaningfulAction();
            if (experimentCondition == XRTrainingExperimentCondition.LLMAssisted)
                RequestAIResponse(false, "WrongPlacement");
            return;
        }

        if (grabbable.Scored)
            return;

        grabbable.MarkScored(zone);
        zone.ShowCorrectFeedback();
        m_Stats.correctPlacements++;
        m_Stats.score = m_Stats.correctPlacements;
        MarkMeaningfulAction();
        ShowStatus("Correct: " + grabbable.displayName + ".");
        LogEvent(XRTrainingEventType.CorrectPlacement, grabbable.displayName, grabbable.transform.position, zone.name);

        if (TaskSolved)
            CompleteMatchingTask();
    }

    void CompleteMatchingTask()
    {
        if (CurrentState != XRTrainingTaskState.Running || m_CompletionEventLogged)
            return;

        StopTimer();
        m_CompletionEventLogged = true;
        m_Stats.success = true;
        SetAllObjectInteraction(false);
        SetFinishUnlocked(true);
        EnterState(XRTrainingTaskState.Completed, "Task complete. Finish unlocked. Go to Finish for results.");
        SetText(completionText, completionMeshText, "State: Completed. Finish unlocked.");
        LogEvent(XRTrainingEventType.TaskComplete, "Complete", Vector3.zero, "all cubes matched");
        dataLogger?.WriteTrialSummary(CurrentState, m_Stats, XRTrainingEventType.TaskComplete.ToString(), "all cubes matched");
        if (experimentCondition == XRTrainingExperimentCondition.LLMAssisted)
            RequestAIResponse(true, "TaskComplete");
        RefreshUI();
    }

    void CheckFinishReached()
    {
        if (CurrentState != XRTrainingTaskState.Completed || finishZone == null || m_ResultsEventLogged)
            return;

        Vector3 checkPosition = headTransform != null ? headTransform.position : (xrOrigin != null ? xrOrigin.position : Vector3.zero);
        if (!finishZone.bounds.Contains(checkPosition))
            return;

        ShowResults(checkPosition);
    }

    void ShowResults(Vector3 finishPosition)
    {
        if (m_ResultsEventLogged)
            return;

        m_ResultsEventLogged = true;
        SetAllObjectInteraction(false);
        SetFinishUnlocked(true);
        EnterState(XRTrainingTaskState.Results, "Results shown. Click Reset to run another round.");
        PlaceResultPanelInFrontOfHead();
        SetText(completionText, completionMeshText, "State: Results. Score " + m_Stats.correctPlacements + " / " + RequiredScore() + ", Time " + TimerText() + ".");
        LogEvent(XRTrainingEventType.TaskEnded, "Finish", finishPosition, "finish reached");
        LogEvent(XRTrainingEventType.ResultsShown, "Results", finishPosition, "score=" + m_Stats.correctPlacements);
        dataLogger?.CompleteTrial(CurrentState, m_Stats, XRTrainingEventType.TaskEnded.ToString(), "finish reached");
        m_TrialRecordingActive = false;
        if (experimentCondition == XRTrainingExperimentCondition.LLMAssisted && !m_SummaryRequested)
            RequestAIResponse(true, "TaskEnded");
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
        {
            if (grabbable != null && grabbable.gameObject.activeInHierarchy)
                grabbable.SetInteractionEnabled(enabled);
        }
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
            RestartTask();

        if (keyboard.lKey.wasPressedThisFrame)
            ToggleLight();

        if (keyboard.fKey.wasPressedThisFrame)
            TryTeleportFromRay();

        if (keyboard.hKey.wasPressedThisFrame)
            RequestAIHint();

        if (keyboard.cKey.wasPressedThisFrame)
            ToggleExperimentCondition();
    }

    void CaptureStartState()
    {
        if (m_HasCapturedStart || xrOrigin == null)
            return;

        m_InitialOriginPosition = xrOrigin.position;
        m_InitialOriginRotation = xrOrigin.rotation;
        m_HasCapturedStart = true;
        CapturePanelState();

        if (grabbables == null)
            return;

        foreach (var grabbable in grabbables)
            grabbable?.CaptureInitialState();
    }

    void CapturePanelState()
    {
        if (m_HasCapturedPanel || panelRoot == null)
            return;

        m_InitialPanelLocalPosition = panelRoot.localPosition;
        m_InitialPanelLocalRotation = panelRoot.localRotation;
        m_InitialPanelLocalScale = panelRoot.localScale;
        m_HasCapturedPanel = true;
    }

    void RestorePanelPlacement()
    {
        ResolveReferences();
        if (panelRoot == null)
            return;

        CapturePanelState();
        panelRoot.localPosition = m_InitialPanelLocalPosition;
        panelRoot.localRotation = m_InitialPanelLocalRotation;
        panelRoot.localScale = m_InitialPanelLocalScale;
    }

    void PlaceResultPanelInFrontOfHead()
    {
        ResolveReferences();
        if (panelRoot == null || headTransform == null)
            return;

        CapturePanelState();

        Vector3 forward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(panelRoot.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 localPanelCenter = ResultPanelLocalCenter();
        Vector3 panelCenter = headTransform.position + forward * Mathf.Max(1.2f, resultPanelDistance);
        panelCenter.y = headTransform.position.y + resultPanelHeightOffset;
        panelRoot.SetPositionAndRotation(panelCenter - rotation * localPanelCenter, rotation);
    }

    Vector3 ResultPanelLocalCenter()
    {
        Transform panel = FindChildByName(panelRoot, "VR Task Panel");
        return panel != null ? panel.localPosition : new Vector3(0f, 2.18f, 2.85f);
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

        if (aiAssistant == null)
            aiAssistant = GetComponent<XRTrainingAIAssistant>();

        if (panelRoot == null)
        {
            if (trainingRoot != null)
                panelRoot = FindChildByName(trainingRoot, "UI");

            if (panelRoot == null)
            {
                var panelObject = GameObject.Find("VR Task Panel");
                if (panelObject != null && panelObject.transform.parent != null)
                    panelRoot = panelObject.transform.parent;
            }
        }
    }

    void BeginTrialRecording()
    {
        if (dataLogger == null)
            return;

        m_TrialNumber++;
        XRTrainingDifficulty difficulty = difficultyConfig != null ? difficultyConfig.difficulty : XRTrainingDifficulty.Easy;
        string label = DifficultyLabel();
        dataLogger.BeginTrial(userId, taskId, m_TrialNumber, difficulty, label, experimentCondition);
        m_TrialRecordingActive = true;
    }

    void EnterState(XRTrainingTaskState nextState, string message)
    {
        if (CurrentState == nextState)
        {
            ShowStatus(message);
            return;
        }

        CurrentState = nextState;
        m_StateEnteredTime = Time.unscaledTime;
        ShowStatus(message);
        LogEvent(XRTrainingEventType.StateChanged, nextState.ToString(), Vector3.zero, message);
    }

    void HandleAIIdleTrigger()
    {
        if (experimentCondition != XRTrainingExperimentCondition.LLMAssisted || CurrentState != XRTrainingTaskState.Running || aiIdleHintSeconds <= 0f || m_IdleHintRequested)
            return;

        if (Time.unscaledTime - m_LastMeaningfulActionTime < aiIdleHintSeconds)
            return;

        m_IdleHintRequested = true;
        RequestAIResponse(false, "Idle12Seconds");
    }

    void MarkMeaningfulAction()
    {
        m_LastMeaningfulActionTime = Time.unscaledTime;
        m_IdleHintRequested = false;
    }

    void RequestAIResponse(bool summary, string trigger)
    {
        if (experimentCondition != XRTrainingExperimentCondition.LLMAssisted)
            return;

        ResolveReferences();
        if (aiAssistant == null)
        {
            ShowAIFallback(XRTrainingAIAssistantFallback(summary), trigger, "AI assistant component missing");
            return;
        }

        if (aiAssistant.IsBusy)
        {
            ShowStatus("AI request already in progress.");
            RefreshUI();
            return;
        }

        m_LastAITrigger = trigger;
        m_LastAIRequestWasSummary = summary;
        XRTrainingAIStateSnapshot requestSnapshot = BuildAIStateSnapshot();
        m_LastAISnapshotJson = JsonUtility.ToJson(requestSnapshot);
        if (summary)
            m_SummaryRequested = true;
        Vector3 requestPosition = headTransform != null ? headTransform.position : Vector3.zero;
        if (!summary)
            LogEvent(XRTrainingEventType.AIHintRequested, "AI", requestPosition, trigger);
        LogEvent(XRTrainingEventType.AIRequestSent, "AI", requestPosition, trigger);
        StartCoroutine(summary
            ? aiAssistant.RequestSummary(requestSnapshot, trigger, OnAIResponse)
            : aiAssistant.RequestHint(requestSnapshot, trigger, OnAIResponse));
    }

    void OnAIResponse(XRTrainingAIResponse response, string rawResponseOrError, bool networkSuccess)
    {
        bool summary = m_LastAIRequestWasSummary;
        string snapshot = string.IsNullOrWhiteSpace(m_LastAISnapshotJson) ? JsonUtility.ToJson(BuildAIStateSnapshot()) : m_LastAISnapshotJson;
        string responseJson = response != null ? JsonUtility.ToJson(response) : string.Empty;
        dataLogger?.LogAIExchange(m_LastAITrigger, networkSuccess, snapshot, responseJson, rawResponseOrError);

        if (response == null)
            return;

        if (summary && !string.IsNullOrWhiteSpace(response.summaryText))
        {
            m_SummaryRequested = true;
            m_CurrentAISummaryText = CompactLine(response.summaryText, 34);
            LogEvent(networkSuccess ? XRTrainingEventType.AISummaryReceived : XRTrainingEventType.AIFallbackUsed, "AI Summary", Vector3.zero, responseJson);
        }
        else
        {
            m_CurrentAIHintText = CompactLine(response.hintText, 34);
            LogEvent(networkSuccess ? XRTrainingEventType.AIHintReceived : XRTrainingEventType.AIFallbackUsed, response.targetObject, Vector3.zero, responseJson);
            ShowStatus("AI: " + m_CurrentAIHintText);
        }

        RefreshUI();
    }

    void ShowAIFallback(XRTrainingAIResponse response, string trigger, string error)
    {
        m_LastAITrigger = trigger;
        m_CurrentAIHintText = response != null ? CompactLine(response.hintText, 34) : "AI unavailable. Match each cube to the same color target.";
        m_CurrentAISummaryText = response != null ? CompactLine(response.summaryText, 34) : m_CurrentAISummaryText;
        string snapshot = string.IsNullOrWhiteSpace(m_LastAISnapshotJson) ? JsonUtility.ToJson(BuildAIStateSnapshot()) : m_LastAISnapshotJson;
        dataLogger?.LogAIExchange(trigger, false, snapshot, response != null ? JsonUtility.ToJson(response) : string.Empty, error);
        LogEvent(XRTrainingEventType.AIFallbackUsed, "AI", Vector3.zero, error);
        ShowStatus("AI fallback: " + m_CurrentAIHintText);
        RefreshUI();
    }

    XRTrainingAIResponse XRTrainingAIAssistantFallback(bool summary)
    {
        string remaining = FirstRemainingObjectName();
        return summary
            ? new XRTrainingAIResponse { summaryText = "Result: " + (m_Stats.success ? "success" : "not complete") + ".", nextRoundSuggestion = "Match each cube color with its target." }
            : new XRTrainingAIResponse { hintText = string.IsNullOrEmpty(remaining) ? "Move to Finish to show results." : "Grab " + remaining + " and place it on the matching target.", targetObject = remaining, suggestedAction = "GrabAndPlace", reason = "Local fallback." };
    }

    XRTrainingAIStateSnapshot BuildAIStateSnapshot()
    {
        var activeObjects = new List<XRTrainingAIObjectSnapshot>();
        var remainingObjects = new List<XRTrainingAIObjectSnapshot>();
        var remainingGoals = new List<string>();
        if (grabbables != null)
        {
            foreach (var grabbable in grabbables)
            {
                if (grabbable == null || !grabbable.gameObject.activeInHierarchy)
                    continue;

                var snapshot = new XRTrainingAIObjectSnapshot
                {
                    name = grabbable.displayName,
                    color = grabbable.colorId.ToString(),
                    scored = grabbable.Scored,
                    position = XRTrainingAIVector3.From(grabbable.transform.position),
                    currentZone = grabbable.CurrentZone != null ? grabbable.CurrentZone.name : string.Empty
                };
                activeObjects.Add(snapshot);
                if (!snapshot.scored)
                {
                    remainingObjects.Add(snapshot);
                    remainingGoals.Add("Place " + grabbable.displayName + " on " + MatchingTargetName(grabbable.colorId));
                }
            }
        }

        if (remainingGoals.Count == 0 && TaskSolved)
            remainingGoals.Add("Go to the Finish zone to complete the round.");

        return new XRTrainingAIStateSnapshot
        {
            userId = SafeUserId(),
            taskId = SafeTaskId(),
            trialNumber = m_TrialNumber,
            condition = CurrentConditionLabel,
            difficulty = DifficultyLabel(),
            state = CurrentState.ToString(),
            elapsedSeconds = m_Stats.elapsedSeconds,
            timeLimitSeconds = timeLimitSeconds,
            score = m_Stats.score,
            requiredScore = RequiredScore(),
            correctCount = m_Stats.correctPlacements,
            wrongCount = m_Stats.wrongPlacements,
            grabCount = m_Stats.grabCount,
            releaseCount = m_Stats.releaseCount,
            teleportCount = m_Stats.teleportCount,
            resetCount = m_Stats.resetCount,
            success = m_Stats.success,
            lastEventType = m_LastEventType,
            lastObjectName = m_LastEventObjectName,
            lastEventDetails = m_LastEventDetails,
            recentEvents = m_RecentEvents.ToArray(),
            remainingGoals = remainingGoals.ToArray(),
            objects = activeObjects.ToArray(),
            remainingObjects = remainingObjects.ToArray()
        };
    }

    string FirstRemainingObjectName()
    {
        if (grabbables == null)
            return string.Empty;

        foreach (var grabbable in grabbables)
        {
            if (grabbable != null && grabbable.gameObject.activeInHierarchy && !grabbable.Scored)
                return grabbable.displayName;
        }

        return string.Empty;
    }

    string MatchingTargetName(XRTrainingColorId colorId)
    {
        if (targetZones != null)
        {
            foreach (var zone in targetZones)
            {
                if (zone != null && zone.gameObject.activeInHierarchy && zone.colorId == colorId)
                    return zone.name;
            }
        }

        return colorId + " Target";
    }

    void UpdateTimer()
    {
        if (!m_TimerRunning)
            return;

        m_Stats.elapsedSeconds = CurrentElapsedSeconds();
        if (HasTimeLimit() && m_Stats.elapsedSeconds >= timeLimitSeconds && !TaskSolved)
        {
            FailTask("Time limit reached.");
            return;
        }

        dataLogger?.TickPoseRecording(CurrentState, m_Stats.elapsedSeconds);
        RefreshUI();
    }

    void StopTimer()
    {
        if (!m_TimerRunning)
            return;

        m_Stats.elapsedSeconds = CurrentElapsedSeconds();
        m_TimerRunning = false;
        dataLogger?.WritePoseSample(CurrentState, m_Stats.elapsedSeconds);
    }

    void ApplyDifficultyLayout()
    {
        difficultyConfig = difficultyConfig ?? XRTrainingDifficultyConfig.Easy();
        timeLimitSeconds = difficultyConfig.timeLimitSeconds;

        int activeCount = ActiveBlockCount();
        bool normal = CurrentDifficulty() == XRTrainingDifficulty.Normal;
        float cubeSpacing = normal ? 1.32f : 1.05f;
        float targetSpacing = normal ? 1.55f : 1.05f;
        float cubeZ = normal ? 3.25f : 3.75f;
        float targetZ = normal ? 6.1f : 5.55f;

        if (grabbables != null)
        {
            for (int i = 0; i < grabbables.Length; i++)
            {
                var grabbable = grabbables[i];
                if (grabbable == null)
                    continue;

                bool active = i < activeCount;
                grabbable.gameObject.SetActive(active);
                if (!active)
                    continue;

                Vector3 position = new Vector3(CenteredOffset(i, activeCount, cubeSpacing), 0.55f, cubeZ + NormalDepthOffset(i));
                grabbable.transform.SetPositionAndRotation(position, Quaternion.identity);
                grabbable.CaptureInitialState();
                grabbable.SetInteractionEnabled(false);
            }
        }

        if (targetZones != null)
        {
            for (int i = 0; i < targetZones.Length; i++)
            {
                var zone = targetZones[i];
                if (zone == null)
                    continue;

                bool active = i < activeCount;
                zone.SetLayoutActive(active);
                if (!active)
                    continue;

                Vector3 position = new Vector3(CenteredOffset(i, activeCount, targetSpacing), 0.08f, targetZ);
                zone.transform.position = position;
                zone.transform.localScale = new Vector3(0.85f, 0.08f, 0.85f);
                zone.ResetFeedback();
                zone.UpdateLabelPosition(position + new Vector3(0f, 0.08f, -0.48f));
            }
        }
    }

    XRTrainingTargetZone FindContainingTarget(Vector3 position)
    {
        if (targetZones == null)
            return null;

        foreach (var targetZone in targetZones)
        {
            if (targetZone != null && targetZone.gameObject.activeInHierarchy && targetZone.ContainsPoint(position))
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
                if (grabbable != null && grabbable.gameObject.activeInHierarchy)
                    count++;
            }
        }

        return Mathf.Max(1, count);
    }

    int ActiveBlockCount()
    {
        int available = Mathf.Min(grabbables != null ? grabbables.Length : 0, targetZones != null ? targetZones.Length : 0);
        int configured = difficultyConfig != null ? difficultyConfig.blockCount : 3;
        return Mathf.Clamp(configured, 1, Mathf.Max(1, available));
    }

    bool CanChangeDifficulty()
    {
        return CurrentState == XRTrainingTaskState.WaitingToStart || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results;
    }

    XRTrainingDifficulty CurrentDifficulty()
    {
        return difficultyConfig != null ? difficultyConfig.difficulty : XRTrainingDifficulty.Easy;
    }

    string DifficultyLabel()
    {
        return difficultyConfig != null && !string.IsNullOrEmpty(difficultyConfig.displayName) ? difficultyConfig.displayName : CurrentDifficulty().ToString();
    }

    string DifficultyDisplayText()
    {
        return "Difficulty: " + DifficultyLabel() + "   Blocks: " + ActiveBlockCount();
    }

    static XRTrainingDifficultyConfig CreateDifficultyConfig(XRTrainingDifficulty difficulty)
    {
        switch (difficulty)
        {
            case XRTrainingDifficulty.Normal:
                return XRTrainingDifficultyConfig.Normal();
            case XRTrainingDifficulty.Hard:
                return XRTrainingDifficultyConfig.Hard();
            default:
                return XRTrainingDifficultyConfig.Easy();
        }
    }

    static float CenteredOffset(int index, int count, float spacing)
    {
        return (index - (count - 1) * 0.5f) * spacing;
    }

    static float NormalDepthOffset(int index)
    {
        return index % 2 == 0 ? 0f : 0.32f;
    }

    void RefreshUI()
    {
        if (IsResultPageState())
            RefreshResultPageUI();
        else
        {
            SetText(scoreText, scoreMeshText, CompactLine("Score: " + m_Stats.correctPlacements + "/" + RequiredScore() + "  Time: " + TimerText(), 34));
            SetText(difficultyText, difficultyMeshText, CompactLine(DifficultyDisplayText(), 34));
            SetText(completionText, completionMeshText, CompactLine(CompletionTextForState(), 34));
        }

        SetText(conditionText, conditionMeshText, CompactLine("Cond: " + CurrentConditionLabel + "  User: " + SafeUserId(), 34));
        SetText(aiText, aiMeshText, IsResultPageState() ? CompactLine("AI: " + m_CurrentAISummaryText, 34) : CompactLine("AI: " + m_CurrentAIHintText, 34));

        if (startTaskButton != null)
            startTaskButton.interactable = CurrentState == XRTrainingTaskState.WaitingToStart || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results;

        bool canChangeDifficulty = CanChangeDifficulty();
        if (easyDifficultyButton != null)
            easyDifficultyButton.interactable = canChangeDifficulty && CurrentDifficulty() != XRTrainingDifficulty.Easy;

        if (normalDifficultyButton != null)
            normalDifficultyButton.interactable = canChangeDifficulty && CurrentDifficulty() != XRTrainingDifficulty.Normal;

        if (conditionButton != null)
            conditionButton.interactable = canChangeDifficulty;

        if (hintButton != null)
        {
            bool canRequestHint = CurrentState == XRTrainingTaskState.Running ||
                                  CurrentState == XRTrainingTaskState.Completed ||
                                  CurrentState == XRTrainingTaskState.Failed ||
                                  CurrentState == XRTrainingTaskState.Results;
            hintButton.interactable = experimentCondition == XRTrainingExperimentCondition.LLMAssisted && canRequestHint && (aiAssistant == null || !aiAssistant.IsBusy);
        }

        if (resetButton != null)
            resetButton.interactable = true;

        if (lightButton != null)
            lightButton.interactable = true;

        if (finishButton != null)
            finishButton.interactable = CurrentState == XRTrainingTaskState.Completed;

        RefreshButtonLabels();
    }

    void RefreshResultPageUI()
    {
        SetText(difficultyText, difficultyMeshText, CompactLine("Result | " + DifficultyLabel() + " | " + CurrentConditionLabel, 34));
        SetText(selectedObjectText, selectedObjectMeshText, CompactLine("User: " + SafeUserId() + "  Task: " + SafeTaskId(), 34));
        SetText(scoreText, scoreMeshText, CompactLine("Score: " + m_Stats.score + "/" + RequiredScore() + "  Time: " + FormatTime(m_Stats.elapsedSeconds), 34));
        SetText(statusText, statusMeshText, CompactLine("C:" + m_Stats.correctPlacements + " W:" + m_Stats.wrongPlacements + " G:" + m_Stats.grabCount + " TP:" + m_Stats.teleportCount, 34));
        SetText(completionText, completionMeshText, CompactLine("Success: " + (m_Stats.success ? "Yes" : "No") + "  Replay/Menu/Diff", 34));
    }

    string CompletionTextForState()
    {
        switch (CurrentState)
        {
            case XRTrainingTaskState.WaitingToStart:
                return "State: Waiting. Press Start.";
            case XRTrainingTaskState.Instructions:
                return "State: Instructions. Limit " + TimerLimitText() + ".";
            case XRTrainingTaskState.Running:
                return "State: Running. Finish before " + TimerLimitText() + ".";
            case XRTrainingTaskState.Completed:
                return "State: Completed. Finish unlocked.";
            case XRTrainingTaskState.Failed:
                return "State: Failed. " + FailureText();
            case XRTrainingTaskState.Results:
                return "State: Results. " + m_Stats.correctPlacements + "/" + RequiredScore() + " in " + TimerText() + ".";
            case XRTrainingTaskState.Restarting:
                return "State: Restarting...";
            default:
                return "State: " + CurrentState + ".";
        }
    }

    void ShowStatus(string message)
    {
        SetText(statusText, statusMeshText, message);
    }

    void RefreshButtonLabels()
    {
        bool resultPage = IsResultPageState();
        SetButtonLabel(startTaskButton, resultPage ? "Again" : "Start", "Start Button World Text");
        SetButtonLabel(resetButton, resultPage ? "Menu" : "Reset", "Reset Button World Text");
        SetButtonLabel(lightButton, resultPage ? "Switch" : "Light", "Light Button World Text");
        SetButtonLabel(finishButton, CurrentState == XRTrainingTaskState.Results ? "Done" : "Go Finish", "Go Finish Button World Text");
        SetButtonLabel(conditionButton, experimentCondition == XRTrainingExperimentCondition.LLMAssisted ? "AI On" : "AI Off", "Condition Button World Text");
        SetButtonLabel(hintButton, "Hint", "Hint Button World Text");
    }

    void SetButtonLabel(Button button, string label, string worldTextName)
    {
        if (button != null)
        {
            var uiText = button.GetComponentInChildren<Text>(true);
            if (uiText != null)
                uiText.text = label;
        }

        TextMesh worldText = FindWorldText(worldTextName);
        if (worldText != null)
            worldText.text = label;
    }

    TextMesh FindWorldText(string objectName)
    {
        if (trainingRoot == null || string.IsNullOrEmpty(objectName))
            return null;

        var texts = trainingRoot.GetComponentsInChildren<TextMesh>(true);
        foreach (var text in texts)
        {
            if (text != null && text.name == objectName)
                return text;
        }

        return null;
    }

    bool IsResultPageState()
    {
        return CurrentState == XRTrainingTaskState.Results || CurrentState == XRTrainingTaskState.Failed;
    }

    string SafeUserId()
    {
        return string.IsNullOrWhiteSpace(userId) ? "P001" : userId;
    }

    string SafeTaskId()
    {
        return string.IsNullOrWhiteSpace(taskId) ? "ColorBlockTask" : taskId;
    }

    void LogEvent(XRTrainingEventType eventType, string objectName, Vector3 position, string details)
    {
        m_LastEventType = eventType.ToString();
        m_LastEventObjectName = objectName ?? string.Empty;
        m_LastEventDetails = details ?? string.Empty;
        if (m_RecentEvents.Count >= 12)
            m_RecentEvents.RemoveAt(0);
        m_RecentEvents.Add(m_LastEventType + ":" + m_LastEventObjectName + ":" + m_LastEventDetails);
        dataLogger?.LogEvent(eventType, CurrentState, objectName, position, m_Stats.elapsedSeconds, m_Stats, details);
    }

    float CurrentElapsedSeconds()
    {
        float elapsed = Mathf.Max(0f, Time.unscaledTime - m_TaskStartTime);
        return HasTimeLimit() ? Mathf.Min(elapsed, timeLimitSeconds) : elapsed;
    }

    bool HasTimeLimit()
    {
        return timeLimitSeconds > 0f;
    }

    string TimerText()
    {
        return HasTimeLimit() ? FormatTime(m_Stats.elapsedSeconds) + " / " + TimerLimitText() : FormatTime(m_Stats.elapsedSeconds);
    }

    string TimerLimitText()
    {
        return HasTimeLimit() ? FormatTime(timeLimitSeconds) : "unlimited";
    }

    string FailureText()
    {
        return string.IsNullOrEmpty(m_FailureReason) ? "Task failed." : m_FailureReason;
    }

    static string FailureStatusText(string reason)
    {
        return string.IsNullOrEmpty(reason) ? "Task failed." : reason;
    }

    static string FormatTime(float seconds)
    {
        return seconds.ToString("0.0") + "s";
    }

    static string CompactLine(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string compact = value.Replace("\r", " ").Replace("\n", " ");
        while (compact.Contains("  "))
            compact = compact.Replace("  ", " ");

        compact = compact.Trim();
        if (compact.Length <= maxLength)
            return compact;

        int cut = Mathf.Max(0, maxLength - 1);
        return compact.Substring(0, cut).TrimEnd() + "…";
    }

    static void SetText(Text target, TextMesh meshTarget, string value)
    {
        if (target != null)
            target.text = value;

        if (meshTarget != null)
            meshTarget.text = WrapWorldText(value, 36);
    }

    static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform match = FindChildByName(child, childName);
            if (match != null)
                return match;
        }

        return null;
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
