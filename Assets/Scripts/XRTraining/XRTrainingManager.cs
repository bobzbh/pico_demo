using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public sealed class XRTrainingManager : MonoBehaviour
{
    [Header("Participant")]
    public string userId = "P001";
    public string taskId = "ColorBlockTask";

    [Header("Difficulty")]
    public XRTrainingDifficulty selectedDifficulty = XRTrainingDifficulty.Easy;
    public XRTrainingDifficultyConfig easyConfig = XRTrainingDifficultyConfig.Easy();
    public XRTrainingDifficultyConfig normalConfig = XRTrainingDifficultyConfig.Normal();
    public XRTrainingDifficultyConfig hardConfig = XRTrainingDifficultyConfig.Hard();

    [Header("Task Objects")]
    public XRTrainingGrabbable[] grabbables;
    public XRTrainingTargetZone[] targetZones;
    public TeleportationArea finishTeleportArea;
    public BoxCollider finishZone;
    public Transform xrOrigin;
    public Transform headTransform;
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;
    public Light sceneLight;

    [Header("Recording")]
    public XRTrainingDataLogger dataLogger;
    public XRTrainingTeleportTracker teleportTracker;

    [Header("UI Text")]
    public Text selectedObjectText;
    public Text scoreText;
    public Text statusText;
    public Text completionText;
    public Text difficultyText;
    public Text timerText;
    public Text instructionText;
    public Text resultText;

    [Header("UI Mesh Text")]
    public TextMesh selectedObjectMesh;
    public TextMesh scoreMesh;
    public TextMesh statusMesh;
    public TextMesh completionMesh;
    public TextMesh difficultyMesh;
    public TextMesh timerMesh;
    public TextMesh instructionMesh;
    public TextMesh resultMesh;

    [Header("UI Buttons")]
    public Button startTaskButton;
    public Button instructionsButton;
    public Button easyButton;
    public Button normalButton;
    public Button hardButton;
    public Button restartButton;
    public Button mainMenuButton;
    public Button switchDifficultyButton;
    public Button teleportButton;

    readonly XRTrainingRuntimeStats m_Stats = new XRTrainingRuntimeStats();

    Vector3 m_InitialOriginPosition;
    Quaternion m_InitialOriginRotation;
    Vector3[] m_InitialGrabbablePositions;
    Quaternion[] m_InitialGrabbableRotations;
    Vector3[] m_InitialGrabbableScales;
    Vector3[] m_InitialTargetPositions;
    Quaternion[] m_InitialTargetRotations;
    bool m_LayoutCaptured;

    XRTrainingDifficultyConfig m_CurrentConfig;
    float m_TrialStartTime;
    float m_ElapsedAtEnd;
    float m_LastActionTime;
    float m_LastIdleHintTime;
    int m_TimeWarningStage;
    int m_TrialCounter;
    int m_CurrentTrialNumber;
    bool m_EndEventWritten;
    Coroutine m_ShowResultsRoutine;

    public XRTrainingTaskState CurrentState { get; private set; } = XRTrainingTaskState.WaitingToStart;
    public bool CanInteractWithObjects => CurrentState == XRTrainingTaskState.Running;

    public float ElapsedSeconds
    {
        get
        {
            if (CurrentState == XRTrainingTaskState.Running)
                return Time.unscaledTime - m_TrialStartTime;

            return m_ElapsedAtEnd;
        }
    }

    void Awake()
    {
        ResolveReferences();
        CaptureInitialLayout();
    }

    void Start()
    {
        PrepareWaitingState(false);
    }

    void Update()
    {
        HandleKeyboardShortcuts();

        if (CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.elapsedSeconds = ElapsedSeconds;
        dataLogger?.TickPoseRecording(CurrentState, ElapsedSeconds);
        UpdateTimerText();
        CheckTimeLimit();
        CheckIdleHint();
    }

    void OnDestroy()
    {
        dataLogger?.EndTrial();
    }

    public void ShowInstructions()
    {
        if (CurrentState == XRTrainingTaskState.Running)
        {
            ShowStatus("Task is running. Finish or restart before reading instructions.");
            return;
        }

        SetTaskState(XRTrainingTaskState.Instructions);
    }

    public void StartTask()
    {
        if (CurrentState == XRTrainingTaskState.Running)
            return;

        if (CurrentState == XRTrainingTaskState.Completed || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results)
            PrepareWaitingState(true);

        ResolveReferences();
        CaptureInitialLayout();
        m_CurrentConfig = GetDifficultyConfig(selectedDifficulty);
        m_CurrentTrialNumber = ++m_TrialCounter;
        m_Stats.Clear();
        m_EndEventWritten = false;
        m_ElapsedAtEnd = 0f;
        m_TimeWarningStage = 0;
        m_LastActionTime = Time.unscaledTime;
        m_LastIdleHintTime = Time.unscaledTime;

        ApplyDifficultyLayout();
        SetAllObjectInteraction(false);
        dataLogger.BeginTrial(userId, taskId, m_CurrentTrialNumber, selectedDifficulty, m_CurrentConfig.displayName);
        dataLogger.WritePoseSample(XRTrainingTaskState.Running, 0f);

        m_TrialStartTime = Time.unscaledTime;
        SetTaskState(XRTrainingTaskState.Running);
        SetAllObjectInteraction(true);
        LogEvent(XRTrainingEventType.TaskStart, "TaskStart", Vector3.zero, "start");
        ShowStatus("Task started. Hold RIGHT MOUSE on a cube, or grab with the controller, then release over the matching target.");
    }

    void HandleKeyboardShortcuts()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            StartTask();

        if (keyboard.fKey.wasPressedThisFrame)
            TryTeleportToFinish();
    }

    public void ResetTask()
    {
        RestartTask();
    }

    public void RestartTask()
    {
        if (m_ShowResultsRoutine != null)
        {
            StopCoroutine(m_ShowResultsRoutine);
            m_ShowResultsRoutine = null;
        }

        if (CurrentState == XRTrainingTaskState.Running && dataLogger != null && dataLogger.IsRecording)
        {
            m_Stats.resetCount++;
            m_ElapsedAtEnd = ElapsedSeconds;
            m_Stats.elapsedSeconds = m_ElapsedAtEnd;
            LogEvent(XRTrainingEventType.TaskReset, "TaskReset", Vector3.zero, "manual restart");
            dataLogger.EndTrial();
        }

        SetTaskState(XRTrainingTaskState.Restarting);
        PrepareWaitingState(true);
    }

    public void ReturnToMainMenu()
    {
        if (CurrentState == XRTrainingTaskState.Running)
        {
            RestartTask();
            return;
        }

        PrepareWaitingState(false);
    }

    public void SwitchDifficulty()
    {
        if (CurrentState == XRTrainingTaskState.Running)
        {
            ShowStatus("Difficulty can be changed before the task starts.");
            return;
        }

        if (selectedDifficulty == XRTrainingDifficulty.Easy)
            SetDifficulty(XRTrainingDifficulty.Normal);
        else if (selectedDifficulty == XRTrainingDifficulty.Normal)
            SetDifficulty(XRTrainingDifficulty.Hard);
        else
            SetDifficulty(XRTrainingDifficulty.Easy);

        PrepareWaitingState(false);
    }

    public void SetEasyDifficulty()
    {
        SetDifficulty(XRTrainingDifficulty.Easy);
    }

    public void SetNormalDifficulty()
    {
        SetDifficulty(XRTrainingDifficulty.Normal);
    }

    public void SetHardDifficulty()
    {
        SetDifficulty(XRTrainingDifficulty.Hard);
    }

    public void SetDifficulty(XRTrainingDifficulty difficulty)
    {
        if (CurrentState == XRTrainingTaskState.Running)
        {
            ShowStatus("Difficulty is locked during the running task.");
            return;
        }

        selectedDifficulty = difficulty;
        m_CurrentConfig = GetDifficultyConfig(selectedDifficulty);
        if (CurrentState == XRTrainingTaskState.Completed || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results)
        {
            PrepareWaitingState(false);
            return;
        }

        ApplyDifficultyLayout();
        SetAllObjectInteraction(false);
        LogEvent(XRTrainingEventType.DifficultyChanged, "Difficulty", Vector3.zero, m_CurrentConfig.displayName);
        RefreshUI();
    }

    public void ShowObjectName(string objectName)
    {
        if (CurrentState != XRTrainingTaskState.Running)
            return;

        SetText(selectedObjectText, selectedObjectMesh, "Selected: " + objectName);
        ShowStatus("Object selected. Release it inside a matching target.");
    }

    public void ReportGrab(XRTrainingGrabbable grabbable)
    {
        if (grabbable == null || CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.grabCount++;
        m_LastActionTime = Time.unscaledTime;
        SetText(selectedObjectText, selectedObjectMesh, "Selected: " + grabbable.displayName);
        ShowStatus("Grabbed: " + grabbable.displayName);
        LogEvent(XRTrainingEventType.ObjectGrab, grabbable.displayName, grabbable.transform.position, "grab");
    }

    public void ReportRelease(XRTrainingGrabbable grabbable)
    {
        if (grabbable == null || CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.releaseCount++;
        m_LastActionTime = Time.unscaledTime;
        LogEvent(XRTrainingEventType.ObjectRelease, grabbable.displayName, grabbable.transform.position, "release");
        EvaluatePlacement(grabbable, grabbable.CurrentZone);
        RefreshUI();
    }

    public void ReportInvalidObjectOperation(XRTrainingGrabbable grabbable, string reason)
    {
        string objectName = grabbable != null ? grabbable.displayName : "Object";
        ShowStatus(objectName + ": " + reason);
    }

    public void TryScore(XRTrainingGrabbable grabbable, XRTrainingTargetZone zone)
    {
        EvaluatePlacement(grabbable, zone);
    }

    public void ReportTeleport(Vector3 position)
    {
        if (CurrentState != XRTrainingTaskState.Running)
            return;

        m_Stats.teleportCount++;
        m_LastActionTime = Time.unscaledTime;
        ShowStatus("Teleport recorded.");
        LogEvent(XRTrainingEventType.Teleport, "Teleport", position, "teleport");
        RefreshUI();
    }

    public void ReportInvalidTeleport(Vector3 position, string reason)
    {
        ShowStatus("Invalid teleport: " + reason);
        LogEvent(XRTrainingEventType.InvalidTeleport, "InvalidTeleport", position, reason);
    }

    public void TryTeleportToFinish()
    {
        if (finishZone == null || xrOrigin == null)
        {
            ReportInvalidTeleport(Vector3.zero, "finish not configured");
            return;
        }

        if (!m_Stats.success && CurrentState != XRTrainingTaskState.Completed && CurrentState != XRTrainingTaskState.Results)
        {
            ReportInvalidTeleport(xrOrigin.position, "finish locked");
            return;
        }

        Vector3 destination = finishZone.bounds.center;
        destination.y = m_InitialOriginPosition.y;
        xrOrigin.position = destination;
        if (teleportTracker != null)
            teleportTracker.RecordTeleport(destination);
        else
            ReportTeleport(destination);
    }

    public void ToggleLight()
    {
        if (sceneLight == null)
            return;

        sceneLight.enabled = !sceneLight.enabled;
        ShowStatus(sceneLight.enabled ? "Light on." : "Light off.");
    }

    void PrepareWaitingState(bool resetOrigin)
    {
        ResolveReferences();
        m_CurrentConfig = GetDifficultyConfig(selectedDifficulty);
        m_Stats.Clear();
        m_ElapsedAtEnd = 0f;
        m_EndEventWritten = false;
        m_TimeWarningStage = 0;
        m_LastActionTime = Time.unscaledTime;
        ApplyDifficultyLayout();
        SetAllObjectInteraction(false);

        if (finishTeleportArea != null)
            finishTeleportArea.enabled = false;

        if (resetOrigin && xrOrigin != null)
            xrOrigin.SetPositionAndRotation(m_InitialOriginPosition, m_InitialOriginRotation);

        SetText(selectedObjectText, selectedObjectMesh, "Selected: none");
        SetTaskState(XRTrainingTaskState.WaitingToStart);
    }

    void EvaluatePlacement(XRTrainingGrabbable grabbable, XRTrainingTargetZone zone)
    {
        if (CurrentState != XRTrainingTaskState.Running || grabbable == null || grabbable.Scored)
            return;

        if (zone == null)
            zone = FindContainingTarget(grabbable.transform.position);

        if (zone == null)
        {
            ShowStatus(grabbable.displayName + " released outside targets.");
            return;
        }

        if (grabbable.isDistractor || grabbable.colorId != zone.colorId)
        {
            RegisterWrongPlacement(grabbable, zone);
            return;
        }

        RegisterCorrectPlacement(grabbable, zone);
    }

    void RegisterCorrectPlacement(XRTrainingGrabbable grabbable, XRTrainingTargetZone zone)
    {
        grabbable.SnapToTarget(zone);
        grabbable.MarkScored();
        zone.ShowCorrectFeedback();

        m_Stats.correctPlacements++;
        m_Stats.score += m_CurrentConfig.scorePerCorrect;
        m_LastActionTime = Time.unscaledTime;

        ShowStatus("Correct: " + grabbable.displayName);
        LogEvent(XRTrainingEventType.CorrectPlacement, grabbable.displayName, grabbable.transform.position, zone.name);

        if (PlacedRequiredCount() >= RequiredScore())
            CompleteTask();
    }

    void RegisterWrongPlacement(XRTrainingGrabbable grabbable, XRTrainingTargetZone zone)
    {
        zone.ShowWrongFeedback();
        m_Stats.wrongPlacements++;
        m_Stats.score = Mathf.Max(0, m_Stats.score - m_CurrentConfig.penaltyPerWrong);
        m_LastActionTime = Time.unscaledTime;

        string targetName = zone != null ? zone.name : "none";
        ShowStatus("Wrong target: " + grabbable.displayName + " -> " + targetName);
        LogEvent(XRTrainingEventType.WrongPlacement, grabbable.displayName, grabbable.transform.position, targetName);
    }

    void CompleteTask()
    {
        if (m_EndEventWritten || CurrentState != XRTrainingTaskState.Running)
            return;

        FinishCurrentTrial(true, XRTrainingTaskState.Completed, XRTrainingEventType.TaskComplete, "all required cubes matched");
    }

    void FailTask(string reason)
    {
        if (m_EndEventWritten || CurrentState != XRTrainingTaskState.Running)
            return;

        FinishCurrentTrial(false, XRTrainingTaskState.Failed, XRTrainingEventType.TaskFailed, reason);
    }

    void FinishCurrentTrial(bool success, XRTrainingTaskState endState, XRTrainingEventType eventType, string reason)
    {
        m_EndEventWritten = true;
        m_ElapsedAtEnd = ElapsedSeconds;
        m_Stats.elapsedSeconds = m_ElapsedAtEnd;
        m_Stats.success = success;
        SetAllObjectInteraction(false);

        if (finishTeleportArea != null)
            finishTeleportArea.enabled = success;

        SetTaskState(endState);
        LogEvent(eventType, eventType.ToString(), Vector3.zero, reason);
        dataLogger?.WritePoseSample(endState, m_ElapsedAtEnd);
        dataLogger?.EndTrial();

        if (m_ShowResultsRoutine != null)
            StopCoroutine(m_ShowResultsRoutine);

        m_ShowResultsRoutine = StartCoroutine(ShowResultsAfterDelay());
    }

    IEnumerator ShowResultsAfterDelay()
    {
        yield return new WaitForSecondsRealtime(0.35f);
        SetTaskState(XRTrainingTaskState.Results);
        m_ShowResultsRoutine = null;
    }

    void CheckTimeLimit()
    {
        if (m_CurrentConfig == null || m_CurrentConfig.timeLimitSeconds <= 0f)
            return;

        float remaining = m_CurrentConfig.timeLimitSeconds - ElapsedSeconds;
        if (remaining <= 0f)
        {
            FailTask("time limit reached");
            return;
        }

        if (remaining <= 5f && m_TimeWarningStage < 3)
        {
            m_TimeWarningStage = 3;
            ShowStatus("Time warning: 5 seconds remaining.");
        }
        else if (remaining <= 10f && m_TimeWarningStage < 2)
        {
            m_TimeWarningStage = 2;
            ShowStatus("Time warning: 10 seconds remaining.");
        }
        else if (remaining <= 20f && m_TimeWarningStage < 1)
        {
            m_TimeWarningStage = 1;
            ShowStatus("Time warning: 20 seconds remaining.");
        }
    }

    void CheckIdleHint()
    {
        if (Time.unscaledTime - m_LastActionTime < 12f)
            return;

        if (Time.unscaledTime - m_LastIdleHintTime < 12f)
            return;

        m_LastIdleHintTime = Time.unscaledTime;
        ShowStatus("Hint: hold RIGHT MOUSE on a cube and drag it to the same-color target.");
    }

    void ApplyDifficultyLayout()
    {
        if (grabbables == null || targetZones == null)
            return;

        if (!m_LayoutCaptured)
            CaptureInitialLayout();

        m_CurrentConfig = GetDifficultyConfig(selectedDifficulty);
        int requiredBlocks = Mathf.Min(Mathf.Min(m_CurrentConfig.blockCount, CountNonDistractorGrabbables()), targetZones.Length);
        float blockRowZ = BaseBlockZ();
        float blockY = BaseBlockY();
        float targetY = BaseTargetY();
        float blockStartX = -((requiredBlocks - 1) * m_CurrentConfig.blockSpacing) * 0.5f;
        float targetStartX = -((requiredBlocks - 1) * m_CurrentConfig.targetSpacing) * 0.5f;

        int colorIndex = 0;
        int distractorIndex = 0;
        for (int i = 0; i < grabbables.Length; i++)
        {
            var grabbable = grabbables[i];
            if (grabbable == null)
                continue;

            grabbable.manager = this;
            bool active;
            Vector3 position;
            Vector3 scale = GetInitialGrabbableScale(i);

            if (grabbable.isDistractor)
            {
                active = distractorIndex < m_CurrentConfig.distractorCount;
                position = new Vector3((distractorIndex % 2 == 0 ? -1f : 1f) * (1.55f + distractorIndex * 0.35f), blockY, blockRowZ + 0.85f);
                distractorIndex++;
            }
            else
            {
                active = colorIndex < requiredBlocks;
                position = new Vector3(blockStartX + colorIndex * m_CurrentConfig.blockSpacing, blockY, blockRowZ);
                if (m_CurrentConfig.randomizeInitialPositions)
                    position += RandomHorizontalOffset(m_CurrentConfig.randomRadius);

                colorIndex++;
            }

            grabbable.SetRoundStartPose(position, Quaternion.identity, scale, active);
            grabbable.SetInteractionEnabled(false);
        }

        for (int i = 0; i < targetZones.Length; i++)
        {
            var zone = targetZones[i];
            if (zone == null)
                continue;

            zone.manager = this;
            bool active = i < requiredBlocks;
            Vector3 position = new Vector3(targetStartX + i * m_CurrentConfig.targetSpacing, targetY, blockRowZ + m_CurrentConfig.targetDistance);
            zone.transform.SetPositionAndRotation(position, Quaternion.identity);
            zone.ApplyDifficultyVisual(m_CurrentConfig.targetRadius, active);
            zone.ResetFeedback();
        }
    }

    void CaptureInitialLayout()
    {
        if (m_LayoutCaptured)
            return;

        if (xrOrigin != null)
        {
            m_InitialOriginPosition = xrOrigin.position;
            m_InitialOriginRotation = xrOrigin.rotation;
        }

        int grabbableCount = grabbables != null ? grabbables.Length : 0;
        m_InitialGrabbablePositions = new Vector3[grabbableCount];
        m_InitialGrabbableRotations = new Quaternion[grabbableCount];
        m_InitialGrabbableScales = new Vector3[grabbableCount];
        for (int i = 0; i < grabbableCount; i++)
        {
            if (grabbables[i] == null)
                continue;

            m_InitialGrabbablePositions[i] = grabbables[i].transform.position;
            m_InitialGrabbableRotations[i] = grabbables[i].transform.rotation;
            m_InitialGrabbableScales[i] = grabbables[i].transform.localScale;
        }

        int targetCount = targetZones != null ? targetZones.Length : 0;
        m_InitialTargetPositions = new Vector3[targetCount];
        m_InitialTargetRotations = new Quaternion[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            if (targetZones[i] == null)
                continue;

            m_InitialTargetPositions[i] = targetZones[i].transform.position;
            m_InitialTargetRotations[i] = targetZones[i].transform.rotation;
        }

        m_LayoutCaptured = true;
    }

    void ResolveReferences()
    {
        if (headTransform == null && Camera.main != null)
            headTransform = Camera.main.transform;

        if (leftControllerTransform == null)
        {
            var left = GameObject.Find("Left Controller");
            if (left != null)
                leftControllerTransform = left.transform;
        }

        if (rightControllerTransform == null)
        {
            var right = GameObject.Find("Right Controller");
            if (right != null)
                rightControllerTransform = right.transform;
        }

        if (dataLogger == null)
            dataLogger = GetComponent<XRTrainingDataLogger>();

        if (dataLogger == null)
            dataLogger = gameObject.AddComponent<XRTrainingDataLogger>();

        dataLogger.ConfigurePoseSources(headTransform, leftControllerTransform, rightControllerTransform);

        if (teleportTracker == null)
            teleportTracker = GetComponent<XRTrainingTeleportTracker>();

        if (teleportTracker == null)
            teleportTracker = gameObject.AddComponent<XRTrainingTeleportTracker>();

        teleportTracker.Configure(this, xrOrigin);
    }

    void SetAllObjectInteraction(bool enabled)
    {
        if (grabbables == null)
            return;

        foreach (var grabbable in grabbables)
        {
            if (grabbable == null)
                continue;

            grabbable.SetInteractionEnabled(enabled);
        }
    }

    XRTrainingTargetZone FindContainingTarget(Vector3 position)
    {
        if (targetZones == null)
            return null;

        foreach (var zone in targetZones)
        {
            if (zone != null && zone.gameObject.activeInHierarchy && zone.ContainsPoint(position))
                return zone;
        }

        return null;
    }

    int RequiredScore()
    {
        if (grabbables == null)
            return 0;

        int count = 0;
        foreach (var grabbable in grabbables)
        {
            if (grabbable != null && grabbable.gameObject.activeSelf && !grabbable.isDistractor)
                count++;
        }

        return count;
    }

    int PlacedRequiredCount()
    {
        if (grabbables == null)
            return 0;

        int count = 0;
        foreach (var grabbable in grabbables)
        {
            if (grabbable != null && grabbable.gameObject.activeSelf && !grabbable.isDistractor && grabbable.Scored)
                count++;
        }

        return count;
    }

    int CountNonDistractorGrabbables()
    {
        if (grabbables == null)
            return 0;

        int count = 0;
        foreach (var grabbable in grabbables)
        {
            if (grabbable != null && !grabbable.isDistractor)
                count++;
        }

        return count;
    }

    Vector3 GetInitialGrabbableScale(int index)
    {
        if (m_InitialGrabbableScales != null && index >= 0 && index < m_InitialGrabbableScales.Length && m_InitialGrabbableScales[index] != Vector3.zero)
            return m_InitialGrabbableScales[index];

        return Vector3.one * 0.38f;
    }

    float BaseBlockZ()
    {
        if (m_InitialGrabbablePositions != null && m_InitialGrabbablePositions.Length > 0)
            return m_InitialGrabbablePositions[0].z;

        return 3.25f;
    }

    float BaseBlockY()
    {
        if (m_InitialGrabbablePositions != null && m_InitialGrabbablePositions.Length > 0)
            return m_InitialGrabbablePositions[0].y;

        return 0.55f;
    }

    float BaseTargetY()
    {
        if (m_InitialTargetPositions != null && m_InitialTargetPositions.Length > 0)
            return m_InitialTargetPositions[0].y;

        return 0.08f;
    }

    Vector3 RandomHorizontalOffset(float radius)
    {
        Vector2 offset = Random.insideUnitCircle * radius;
        return new Vector3(offset.x, 0f, offset.y);
    }

    XRTrainingDifficultyConfig GetDifficultyConfig(XRTrainingDifficulty difficulty)
    {
        if (difficulty == XRTrainingDifficulty.Normal)
            return normalConfig ?? XRTrainingDifficultyConfig.Normal();

        if (difficulty == XRTrainingDifficulty.Hard)
            return hardConfig ?? XRTrainingDifficultyConfig.Hard();

        return easyConfig ?? XRTrainingDifficultyConfig.Easy();
    }

    void SetTaskState(XRTrainingTaskState state)
    {
        CurrentState = state;
        RefreshUI();
    }

    void RefreshUI()
    {
        m_CurrentConfig = GetDifficultyConfig(selectedDifficulty);
        UpdateScoreText();
        UpdateTimerText();
        SetText(difficultyText, difficultyMesh, "Difficulty: " + m_CurrentConfig.displayName);
        SetText(instructionText, instructionMesh, InstructionForState());
        SetText(completionText, completionMesh, CompletionForState());
        SetText(resultText, resultMesh, ResultSummary());
        UpdateButtons();
    }

    void UpdateButtons()
    {
        bool running = CurrentState == XRTrainingTaskState.Running;
        bool ended = CurrentState == XRTrainingTaskState.Completed || CurrentState == XRTrainingTaskState.Failed || CurrentState == XRTrainingTaskState.Results;
        bool menu = CurrentState == XRTrainingTaskState.WaitingToStart || CurrentState == XRTrainingTaskState.Instructions || CurrentState == XRTrainingTaskState.Results;

        if (startTaskButton != null)
            startTaskButton.interactable = !running;

        if (instructionsButton != null)
            instructionsButton.interactable = !running;

        if (easyButton != null)
            easyButton.interactable = !running;

        if (normalButton != null)
            normalButton.interactable = !running;

        if (hardButton != null)
            hardButton.interactable = !running;

        if (restartButton != null)
            restartButton.interactable = running || ended;

        if (mainMenuButton != null)
            mainMenuButton.interactable = menu;

        if (switchDifficultyButton != null)
            switchDifficultyButton.interactable = !running;

        if (teleportButton != null)
            teleportButton.interactable = m_Stats.success || ended;
    }

    void UpdateScoreText()
    {
        SetText(scoreText, scoreMesh, "Score: " + m_Stats.score + " | OK: " + PlacedRequiredCount() + "/" + RequiredScore() + " | Err: " + m_Stats.wrongPlacements);
    }

    void UpdateTimerText()
    {
        string limit = m_CurrentConfig != null && m_CurrentConfig.timeLimitSeconds > 0f ? " / " + m_CurrentConfig.timeLimitSeconds.ToString("F0") + "s" : "";
        SetText(timerText, timerMesh, "Time: " + ElapsedSeconds.ToString("F1") + "s" + limit);
    }

    void ShowStatus(string message)
    {
        SetText(statusText, statusMesh, StateLabel(CurrentState) + ": " + message);
    }

    string InstructionForState()
    {
        if (CurrentState == XRTrainingTaskState.WaitingToStart)
            return "1. Choose Easy or Normal. 2. Press Start Task or Enter. 3. Hold RIGHT MOUSE on a cube to drag it.";

        if (CurrentState == XRTrainingTaskState.Instructions)
            return "Instructions: match each cube to the same-color target.";

        if (CurrentState == XRTrainingTaskState.Running)
            return "Use RIGHT MOUSE drag or controller grip/select, then release over the same-color target.";

        if (CurrentState == XRTrainingTaskState.Completed)
            return "Completed: all required cubes are matched.";

        if (CurrentState == XRTrainingTaskState.Failed)
            return "Failed: task ended before all cubes were matched.";

        if (CurrentState == XRTrainingTaskState.Results)
            return "Results: press Start Task for another round, or Reset to return.";

        return "Restarting: resetting task objects and timers.";
    }

    string CompletionForState()
    {
        if (CurrentState == XRTrainingTaskState.Running)
            return "Task in progress";

        if (CurrentState == XRTrainingTaskState.Completed || (CurrentState == XRTrainingTaskState.Results && m_Stats.success))
            return "Task complete";

        if (CurrentState == XRTrainingTaskState.Failed || (CurrentState == XRTrainingTaskState.Results && !m_Stats.success && m_ElapsedAtEnd > 0f))
            return "Task failed";

        return "Task not started";
    }

    string ResultSummary()
    {
        if (CurrentState != XRTrainingTaskState.Results && CurrentState != XRTrainingTaskState.Completed && CurrentState != XRTrainingTaskState.Failed)
            return "Result page will use real data from the current round.";

        string difficultyName = m_CurrentConfig != null ? m_CurrentConfig.displayName : selectedDifficulty.ToString();
        return
            "User: " + userId + " | Task: " + taskId + " | Trial: " + m_CurrentTrialNumber + "\n" +
            "Difficulty: " + difficultyName + "\n" +
            "Score: " + m_Stats.score + " | Time: " + m_Stats.elapsedSeconds.ToString("F1") + "s | Success: " + (m_Stats.success ? "Yes" : "No") + "\n" +
            "Correct: " + m_Stats.correctPlacements + " | Wrong: " + m_Stats.wrongPlacements + " | Grabs: " + m_Stats.grabCount + " | Releases: " + m_Stats.releaseCount + "\n" +
            "Teleports: " + m_Stats.teleportCount + " | Resets: " + m_Stats.resetCount + "\n" +
            "Data saved to project XRTrainingExperimentData.";
    }

    static string StateLabel(XRTrainingTaskState state)
    {
        if (state == XRTrainingTaskState.WaitingToStart)
            return "Waiting";

        if (state == XRTrainingTaskState.Instructions)
            return "Info";

        if (state == XRTrainingTaskState.Running)
            return "Running";

        if (state == XRTrainingTaskState.Completed)
            return "Done";

        if (state == XRTrainingTaskState.Failed)
            return "Failed";

        if (state == XRTrainingTaskState.Results)
            return "Results";

        return "Reset";
    }

    void LogEvent(XRTrainingEventType eventType, string objectName, Vector3 position, string details)
    {
        dataLogger?.LogEvent(eventType, CurrentState, objectName, position, ElapsedSeconds, m_Stats, details);
    }

    static void SetText(Text uiText, TextMesh meshText, string value)
    {
        if (uiText != null)
            uiText.text = value;

        if (meshText != null)
            meshText.text = value;
    }
}
