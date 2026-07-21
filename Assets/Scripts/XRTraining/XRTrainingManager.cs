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

    [Header("Task Flow")]
    public float instructionSeconds = 1f;
    public float timeLimitSeconds = 5f;
    public string userId = "P001";
    public string taskId = "ColorBlockTask";
    public XRTrainingDifficultyConfig difficultyConfig = new XRTrainingDifficultyConfig();

    [Header("Optional Recording")]
    public XRTrainingDataLogger dataLogger;
    public XRTrainingTeleportTracker teleportTracker;

    [Header("World UI")]
    public Text selectedObjectText;
    public Text scoreText;
    public Text difficultyText;
    public Text statusText;
    public Text completionText;
    public TextMesh selectedObjectMeshText;
    public TextMesh scoreMeshText;
    public TextMesh difficultyMeshText;
    public TextMesh statusMeshText;
    public TextMesh completionMeshText;
    public Button startTaskButton;
    public Button easyDifficultyButton;
    public Button normalDifficultyButton;
    public Button resetButton;
    public Button lightButton;
    public Button finishButton;

    readonly XRTrainingRuntimeStats m_Stats = new XRTrainingRuntimeStats();
    Vector3 m_InitialOriginPosition;
    Quaternion m_InitialOriginRotation;
    bool m_HasCapturedStart;
    bool m_HasAlignedScene;
    bool m_TimerRunning;
    bool m_TrialRecordingActive;
    bool m_CompletionEventLogged;
    bool m_ResultsEventLogged;
    float m_TaskStartTime;
    float m_StateEnteredTime;
    int m_TrialNumber;
    string m_FailureReason = "";

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
        ResetTaskInternal(false);
    }

    void Update()
    {
        HandleInstructionCountdown();
        UpdateTimer();
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
        RestartTask();
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

    public void ToggleLight()
    {
        if (sceneLight == null)
            return;

        sceneLight.enabled = !sceneLight.enabled;
        ShowStatus(sceneLight.enabled ? "Light on." : "Light off.");
        LogEvent(XRTrainingEventType.LightToggled, "Light", Vector3.zero, sceneLight.enabled ? "on" : "off");
        RefreshUI();
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
        LogEvent(XRTrainingEventType.ObjectRelease, grabbable.displayName, grabbable.transform.position, "release");
        EvaluatePlacement(grabbable);
        RefreshUI();
    }

    public void ReportInvalidObjectOperation(XRTrainingGrabbable grabbable, string reason)
    {
        string objectName = grabbable != null ? grabbable.displayName : "Object";
        ShowStatus(objectName + ": " + reason);
        RefreshUI();
    }

    public void ReportTeleport(Vector3 position)
    {
        if (CurrentState != XRTrainingTaskState.Running && CurrentState != XRTrainingTaskState.Completed)
            return;

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
        SetAllObjectInteraction(false);
        ResetObjectsOnly();
        SetFinishUnlocked(false);
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

        SetAllObjectInteraction(false);
        ResetObjectsOnly();
        SetFinishUnlocked(false);
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
        SetText(completionText, completionMeshText, "State: Results. Score " + m_Stats.correctPlacements + " / " + RequiredScore() + ", Time " + TimerText() + ".");
        LogEvent(XRTrainingEventType.TaskEnded, "Finish", finishPosition, "finish reached");
        LogEvent(XRTrainingEventType.ResultsShown, "Results", finishPosition, "score=" + m_Stats.correctPlacements);
        dataLogger?.CompleteTrial(CurrentState, m_Stats, XRTrainingEventType.TaskEnded.ToString(), "finish reached");
        m_TrialRecordingActive = false;
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

    void BeginTrialRecording()
    {
        if (dataLogger == null)
            return;

        m_TrialNumber++;
        XRTrainingDifficulty difficulty = difficultyConfig != null ? difficultyConfig.difficulty : XRTrainingDifficulty.Easy;
        string label = DifficultyLabel();
        dataLogger.BeginTrial(userId, taskId, m_TrialNumber, difficulty, label);
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
        SetText(scoreText, scoreMeshText, "Score: " + m_Stats.correctPlacements + " / " + RequiredScore() + "   Time: " + TimerText());
        SetText(difficultyText, difficultyMeshText, DifficultyDisplayText());
        SetText(completionText, completionMeshText, CompletionTextForState());

        if (startTaskButton != null)
            startTaskButton.interactable = CurrentState == XRTrainingTaskState.WaitingToStart || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results;

        bool canChangeDifficulty = CanChangeDifficulty();
        if (easyDifficultyButton != null)
            easyDifficultyButton.interactable = canChangeDifficulty && CurrentDifficulty() != XRTrainingDifficulty.Easy;

        if (normalDifficultyButton != null)
            normalDifficultyButton.interactable = canChangeDifficulty && CurrentDifficulty() != XRTrainingDifficulty.Normal;

        if (resetButton != null)
            resetButton.interactable = true;

        if (lightButton != null)
            lightButton.interactable = true;

        if (finishButton != null)
            finishButton.interactable = CurrentState == XRTrainingTaskState.Completed;
    }

    string CompletionTextForState()
    {
        switch (CurrentState)
        {
            case XRTrainingTaskState.WaitingToStart:
                return "State: Waiting to start.";
            case XRTrainingTaskState.Instructions:
                return "State: Instructions. Starting soon. Limit " + TimerLimitText() + ".";
            case XRTrainingTaskState.Running:
                return "State: Running. Match all cubes before " + TimerLimitText() + ".";
            case XRTrainingTaskState.Completed:
                return "State: Completed. Finish unlocked.";
            case XRTrainingTaskState.Failed:
                return "State: Failed. " + FailureText() + " Click Reset.";
            case XRTrainingTaskState.Results:
                return "State: Results. Score " + m_Stats.correctPlacements + " / " + RequiredScore() + ", Time " + TimerText() + ".";
            case XRTrainingTaskState.Restarting:
                return "State: Restarting.";
            default:
                return "State: " + CurrentState + ".";
        }
    }

    void ShowStatus(string message)
    {
        SetText(statusText, statusMeshText, message);
    }

    void LogEvent(XRTrainingEventType eventType, string objectName, Vector3 position, string details)
    {
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
