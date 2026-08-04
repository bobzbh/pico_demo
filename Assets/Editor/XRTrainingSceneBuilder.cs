using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class XRTrainingSceneBuilder
{
    const string RootName = "XR Training Demo";
    const string LegacyRootName = "Legacy Previous Test Objects";
    const string MaterialFolder = "Assets/materials/XRTraining";
    const string SimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.0/XR Interaction Simulator/XR Interaction Simulator.prefab";
    const string DefaultInputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.0/Starter Assets/XRI Default Input Actions.inputactions";
    const string SimulatorInputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.0/XR Interaction Simulator/XR Interaction Simulator Controls.inputactions";
    const string ControllerInputActionsPath = "Assets/Samples/XR Interaction Toolkit/3.5.0/XR Interaction Simulator/XR Interaction Controller Controls.inputactions";

    [MenuItem("Tools/Codex/Build XR Training Scene")]
    public static void BuildScene()
    {
        EnsureMaterialFolder();
        var red = GetOrCreateMaterial("Training_Red", new Color(0.95f, 0.12f, 0.1f, 1f));
        var green = GetOrCreateMaterial("Training_Green", new Color(0.1f, 0.75f, 0.24f, 1f));
        var blue = GetOrCreateMaterial("Training_Blue", new Color(0.12f, 0.36f, 0.95f, 1f));
        var yellow = GetOrCreateMaterial("Training_Yellow", new Color(0.98f, 0.86f, 0.12f, 1f));
        var start = GetOrCreateMaterial("Training_Start", new Color(0.18f, 0.47f, 0.95f, 1f));
        var operation = GetOrCreateMaterial("Training_Operation", new Color(0.42f, 0.45f, 0.5f, 1f));
        var finish = GetOrCreateMaterial("Training_Finish", new Color(0.96f, 0.76f, 0.22f, 1f));
        var panel = GetOrCreateMaterial("Training_Panel", new Color(0.08f, 0.09f, 0.11f, 1f));

        DeleteGeneratedTrainingObjects();

        var root = new GameObject(RootName);
        var zonesRoot = CreateChild(root.transform, "Teleport Zones");
        var objectsRoot = CreateChild(root.transform, "Grabbable Cubes");
        var targetsRoot = CreateChild(root.transform, "Color Targets");
        var uiRoot = CreateChild(root.transform, "VR UI");
        var flowRoot = CreateChild(root.transform, "Task Flow");

        var origin = EnsureXROrigin();
        ConfigureOriginStart(origin);
        var mainCamera = EnsureMainCamera(origin);
        EnsureInteractionManager();
        EnsureEventSystem();
        EnsureInputActions();
        EnsureSimulator();
        EnsureControllerRaySetup();
        RestoreControllerVisualRenderers();
        var teleportationProvider = EnsureLocomotion(origin);
        var sceneLight = EnsureLight();

        var managerObject = new GameObject("XR Training Manager");
        managerObject.transform.SetParent(flowRoot, false);
        var manager = managerObject.AddComponent<XRTrainingManager>();
        manager.trainingRoot = root.transform;
        manager.panelRoot = uiRoot;
        manager.instructionSeconds = 1f;
        manager.difficultyConfig = XRTrainingDifficultyConfig.Easy();
        manager.timeLimitSeconds = 10f;
        manager.experimentCondition = XRTrainingExperimentCondition.LLMAssisted;
        manager.aiIdleHintSeconds = 4f;
        manager.xrOrigin = origin != null ? origin.transform : null;
        manager.headTransform = mainCamera != null ? mainCamera.transform : null;
        manager.leftControllerTransform = FindTransform("XR Origin (XR Rig)/Camera Offset/Left Controller");
        manager.rightControllerTransform = FindTransform("XR Origin (XR Rig)/Camera Offset/Right Controller");
        manager.leftRayTransform = FindBestRay("Left Controller");
        manager.rightRayTransform = FindBestRay("Right Controller");
        manager.sceneLight = sceneLight;

        var aiAssistant = managerObject.AddComponent<XRTrainingAIAssistant>();
        aiAssistant.enableNetworkRequests = true;
        aiAssistant.requestTimeoutSeconds = 8f;
        manager.aiAssistant = aiAssistant;

        var dataLogger = managerObject.AddComponent<XRTrainingDataLogger>();
        dataLogger.outputFolderName = "XRTrainingExperimentData";
        dataLogger.maxSavedTrialRecords = 5;
        dataLogger.poseSampleIntervalSeconds = 0.1f;
        dataLogger.ConfigurePoseSources(manager.headTransform, manager.leftControllerTransform, manager.rightControllerTransform);
        manager.dataLogger = dataLogger;

        var teleportTracker = managerObject.AddComponent<XRTrainingTeleportTracker>();
        teleportTracker.Configure(manager, manager.xrOrigin);
        manager.teleportTracker = teleportTracker;

        var mouseGrabber = managerObject.AddComponent<XRTrainingMouseGrabber>();
        mouseGrabber.manager = manager;
        mouseGrabber.eventCamera = mainCamera;
        mouseGrabber.autoStartOnMouseGrab = false;

        CreateTeleportPlatform("Start Zone", zonesRoot, new Vector3(0f, -0.05f, 0f), new Vector3(2.4f, 0.1f, 2.0f), start, true, teleportationProvider);
        CreateTeleportPlatform("Operation Zone", zonesRoot, new Vector3(0f, -0.05f, 4.2f), new Vector3(6.4f, 0.1f, 4.4f), operation, true, teleportationProvider);
        var finishZone = CreateTeleportPlatform("Finish Zone", zonesRoot, new Vector3(0f, -0.05f, 8.2f), new Vector3(2.5f, 0.1f, 2.0f), finish, false, teleportationProvider);

        manager.finishTeleportArea = finishZone.GetComponent<TeleportationArea>();
        manager.finishZone = CreateFinishDetector(zonesRoot, manager);

        CreateWorldLabel("Start", zonesRoot, new Vector3(0f, 0.06f, -0.55f), Color.white);
        CreateWorldLabel("Operation", zonesRoot, new Vector3(0f, 0.06f, 2.45f), Color.white);
        CreateWorldLabel("Finish", zonesRoot, new Vector3(0f, 0.06f, 7.58f), Color.black);

        manager.grabbables = new[]
        {
            CreateTrainingCube("Red Cube", XRTrainingColorId.Red, objectsRoot, new Vector3(-1.05f, 0.55f, 3.75f), red, manager),
            CreateTrainingCube("Green Cube", XRTrainingColorId.Green, objectsRoot, new Vector3(0f, 0.55f, 3.75f), green, manager),
            CreateTrainingCube("Blue Cube", XRTrainingColorId.Blue, objectsRoot, new Vector3(1.05f, 0.55f, 3.75f), blue, manager),
            CreateTrainingCube("Yellow Cube", XRTrainingColorId.Yellow, objectsRoot, new Vector3(1.98f, 0.55f, 3.55f), yellow, manager)
        };

        manager.targetZones = new[]
        {
            CreateTargetZone("Red Target", XRTrainingColorId.Red, targetsRoot, new Vector3(-1.05f, 0.08f, 5.55f), red, manager),
            CreateTargetZone("Green Target", XRTrainingColorId.Green, targetsRoot, new Vector3(0f, 0.08f, 5.55f), green, manager),
            CreateTargetZone("Blue Target", XRTrainingColorId.Blue, targetsRoot, new Vector3(1.05f, 0.08f, 5.55f), blue, manager),
            CreateTargetZone("Yellow Target", XRTrainingColorId.Yellow, targetsRoot, new Vector3(1.55f, 0.08f, 6.1f), yellow, manager)
        };

        manager.grabbables[3].gameObject.SetActive(false);
        manager.targetZones[3].SetLayoutActive(false);

        BuildWorldPanel(uiRoot, manager, panel);

        foreach (var grabbable in manager.grabbables)
        {
            grabbable.CaptureInitialState();
            grabbable.SetInteractionEnabled(false);
        }

        foreach (var targetZone in manager.targetZones)
            targetZone.ResetFeedback();

        ConfigureMainCameraForSimulator(mainCamera);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = root;
        Debug.Log("XR training scene rebuilt: XR Origin, Interaction Manager, controllers, rays, simulator, UI, grab/release, color matching, and teleport flow.");
    }

    static Transform CreateChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    static XROrigin EnsureXROrigin()
    {
        var origin = Object.FindObjectOfType<XROrigin>();
        if (origin == null)
        {
            var originObject = new GameObject("XR Origin (XR Rig)");
            origin = originObject.AddComponent<XROrigin>();

            var cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(cameraOffset.transform, false);
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = cameraOffset;

            new GameObject("Left Controller").transform.SetParent(cameraOffset.transform, false);
            new GameObject("Right Controller").transform.SetParent(cameraOffset.transform, false);
        }

        origin.gameObject.name = "XR Origin (XR Rig)";
        return origin;
    }

    static void ConfigureOriginStart(XROrigin origin)
    {
        if (origin == null)
            return;

        origin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        if (origin.CameraFloorOffsetObject != null)
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, 1.36f, 0f);

        if (origin.Camera != null)
            origin.Camera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        var rigidbody = origin.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.velocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        var characterController = origin.GetComponent<CharacterController>();
        if (characterController == null)
            characterController = origin.gameObject.AddComponent<CharacterController>();

        characterController.enabled = false;
        characterController.height = 1.65f;
        characterController.radius = 0.25f;
        characterController.center = new Vector3(0f, 0.825f, 0f);
        characterController.slopeLimit = 60f;
        characterController.stepOffset = 0.25f;
        characterController.skinWidth = 0.03f;
        characterController.enabled = true;
    }

    static Camera EnsureMainCamera(XROrigin origin)
    {
        var camera = Camera.main;
        if (camera == null && origin != null)
            camera = origin.Camera;

        if (camera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 100f;
        return camera;
    }

    static void EnsureInteractionManager()
    {
        if (Object.FindObjectOfType<XRInteractionManager>() == null)
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
    }

    static void EnsureEventSystem()
    {
        var eventSystem = Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
            eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();

        foreach (var inputModule in eventSystem.GetComponents<BaseInputModule>())
        {
            if (inputModule is XRUIInputModule)
                continue;

            Object.DestroyImmediate(inputModule);
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
    }

    static void EnsureInputActions()
    {
        var actionManager = Object.FindObjectOfType<InputActionManager>();
        if (actionManager == null)
            actionManager = new GameObject("Input Action Manager").AddComponent<InputActionManager>();

        var assets = new List<InputActionAsset>();
        AddActionAsset(assets, DefaultInputActionsPath);
        AddActionAsset(assets, SimulatorInputActionsPath);
        AddActionAsset(assets, ControllerInputActionsPath);
        actionManager.actionAssets = assets;
    }

    static void EnsureSimulator()
    {
        if (Object.FindObjectOfType<XRInteractionSimulator>() != null)
            return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("XR Interaction Simulator prefab was not found. Import the XR Interaction Toolkit simulator sample if simulation controls are missing.");
            return;
        }

        var simulator = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (simulator != null)
            simulator.name = "XR Device Simulator (XRI 3.5)";
    }

    static void EnsureControllerRaySetup()
    {
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Left Controller/Near-Far Interactor");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Right Controller/Near-Far Interactor");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Left Controller/Teleport Interactor");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Right Controller/Teleport Interactor");

        foreach (var nearFarInteractor in Object.FindObjectsOfType<NearFarInteractor>(true))
        {
            nearFarInteractor.enableUIInteraction = true;
            nearFarInteractor.interactionLayers = InteractionLayerMask.GetMask("Default");
        }

        foreach (var sphereCaster in Object.FindObjectsOfType<SphereInteractionCaster>(true))
            sphereCaster.physicsLayerMask = Physics.AllLayers;

        foreach (var curveCaster in Object.FindObjectsOfType<CurveInteractionCaster>(true))
        {
            curveCaster.raycastMask = Physics.AllLayers;
            curveCaster.castDistance = 10f;
        }

        foreach (var rayInteractor in Object.FindObjectsOfType<XRRayInteractor>(true))
        {
            rayInteractor.enableUIInteraction = true;
            rayInteractor.maxRaycastDistance = 10f;
            rayInteractor.interactionLayers = rayInteractor.name.Contains("Teleport")
                ? InteractionLayerMask.GetMask("Teleport")
                : InteractionLayerMask.GetMask("Default");
        }
    }

    static void EnsureRayVisualFor(string path)
    {
        var ray = FindTransform(path);
        if (ray == null)
            return;

        var lineRenderer = ray.GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = ray.gameObject.AddComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = 0.01f;
        lineRenderer.endWidth = 0.003f;

        if (ray.GetComponent<XRTrainingRayVisual>() == null)
            ray.gameObject.AddComponent<XRTrainingRayVisual>();
    }

    static Transform FindBestRay(string controllerName)
    {
        var teleport = FindTransform("XR Origin (XR Rig)/Camera Offset/" + controllerName + "/Teleport Interactor");
        if (teleport != null)
            return teleport;

        var nearFar = FindTransform("XR Origin (XR Rig)/Camera Offset/" + controllerName + "/Near-Far Interactor");
        if (nearFar != null)
            return nearFar;

        return FindTransform("XR Origin (XR Rig)/Camera Offset/" + controllerName);
    }

    static void RestoreControllerVisualRenderers()
    {
        RestoreRenderersUnder("XR Origin (XR Rig)/Camera Offset/Left Controller");
        RestoreRenderersUnder("XR Origin (XR Rig)/Camera Offset/Right Controller");
    }

    static void RestoreRenderersUnder(string path)
    {
        var controller = FindTransform(path);
        if (controller == null)
            return;

        foreach (var objectRenderer in controller.GetComponentsInChildren<Renderer>(true))
            objectRenderer.enabled = true;
    }

    static TeleportationProvider EnsureLocomotion(XROrigin origin)
    {
        var mediator = Object.FindObjectOfType<LocomotionMediator>();
        if (mediator == null)
        {
            var locomotion = new GameObject("Locomotion");
            if (origin != null)
                locomotion.transform.SetParent(origin.transform, false);

            mediator = locomotion.AddComponent<LocomotionMediator>();
        }

        var bodyTransformer = mediator.GetComponent<XRBodyTransformer>();
        if (bodyTransformer != null && origin != null)
            bodyTransformer.xrOrigin = origin;

        var provider = Object.FindObjectOfType<TeleportationProvider>();
        if (provider == null)
        {
            var providerObject = new GameObject("Teleportation Provider");
            providerObject.transform.SetParent(mediator.transform, false);
            provider = providerObject.AddComponent<TeleportationProvider>();
        }

        provider.mediator = mediator;
        provider.delayTime = 0f;
        return provider;
    }

    static Light EnsureLight()
    {
        var light = Object.FindObjectOfType<Light>();
        if (light != null)
        {
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            return light;
        }

        var lightObject = new GameObject("Directional Light");
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        return light;
    }

    static GameObject CreateTeleportPlatform(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool teleportEnabled, TeleportationProvider provider)
    {
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = name;
        platform.transform.SetParent(parent, false);
        platform.transform.position = position;
        platform.transform.localScale = scale;
        AssignMaterial(platform, material);

        var area = platform.AddComponent<TeleportationArea>();
        area.enabled = teleportEnabled;
        area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
        area.teleportationProvider = provider;
        area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        return platform;
    }

    static BoxCollider CreateFinishDetector(Transform parent, XRTrainingManager manager)
    {
        var detector = new GameObject("Finish Completion Zone");
        detector.transform.SetParent(parent, false);
        detector.transform.position = new Vector3(0f, 1f, 8.2f);
        detector.transform.localScale = new Vector3(2.6f, 2f, 2.1f);
        var collider = detector.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        return collider;
    }

    static XRTrainingGrabbable CreateTrainingCube(string name, XRTrainingColorId colorId, Transform parent, Vector3 position, Material material, XRTrainingManager manager)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.42f;
        AssignMaterial(cube, material);

        var rigidbody = cube.AddComponent<Rigidbody>();
        rigidbody.mass = 0.35f;
        rigidbody.drag = 0.2f;
        rigidbody.angularDrag = 0.15f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

        var grab = cube.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.throwOnDetach = true;
        grab.interactionLayers = InteractionLayerMask.GetMask("Default");
        grab.colliders.Clear();
        grab.colliders.Add(cube.GetComponent<Collider>());

        var training = cube.AddComponent<XRTrainingGrabbable>();
        training.colorId = colorId;
        training.displayName = name;
        training.manager = manager;

        var selectable = cube.AddComponent<XRTrainingSelectable>();
        selectable.manager = manager;
        selectable.displayName = name;
        selectable.hoverColor = Color.Lerp(Color.white, ReadMaterialColor(material), 0.35f);
        return training;
    }

    static XRTrainingTargetZone CreateTargetZone(string name, XRTrainingColorId colorId, Transform parent, Vector3 position, Material material, XRTrainingManager manager)
    {
        var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = name;
        zone.transform.SetParent(parent, false);
        zone.transform.position = position;
        zone.transform.localScale = new Vector3(0.85f, 0.08f, 0.85f);
        AssignMaterial(zone, material);

        var collider = zone.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(1f, 3.6f, 1f);
        collider.center = new Vector3(0f, 1.35f, 0f);

        var target = zone.AddComponent<XRTrainingTargetZone>();
        target.colorId = colorId;
        target.manager = manager;
        target.labelObject = CreateWorldLabel(name, parent, position + new Vector3(0f, 0.08f, -0.48f), Color.white);
        return target;
    }

    static void BuildWorldPanel(Transform parent, XRTrainingManager manager, Material panelMaterial)
    {
        var canvasObject = new GameObject("VR Task Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.position = new Vector3(0f, 2.18f, 2.85f);
        canvasObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        canvasObject.transform.localScale = Vector3.one * 0.00175f;

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 1500f;

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1040f, 820f);

        var panelBackground = new Color(0.075f, 0.085f, 0.105f, 0.94f);
        var cardBackground = new Color(0.12f, 0.145f, 0.18f, 0.78f);
        var bodyColor = new Color(0.92f, 0.95f, 0.99f, 1f);
        var mutedColor = new Color(0.72f, 0.81f, 0.9f, 1f);
        var accentColor = new Color(0.24f, 0.75f, 0.8f, 1f);

        CreateUIImage("Panel Background", canvasObject.transform, new Vector2(1040f, 820f), Vector2.zero, panelBackground);
        CreateUIImage("Header Accent", canvasObject.transform, new Vector2(960f, 6f), new Vector2(0f, 354f), accentColor);
        CreateUIImage("Task Details Card", canvasObject.transform, new Vector2(960f, 376f), new Vector2(0f, 84f), cardBackground);
        CreateUIImage("Control Divider", canvasObject.transform, new Vector2(960f, 2f), new Vector2(0f, -160f), new Color(0.3f, 0.38f, 0.48f, 0.65f));

        CreateUIText("Title", canvasObject.transform, "XR Training Tasks", 34, new Vector2(0f, 308f), new Vector2(920f, 54f), TextAnchor.MiddleCenter, Color.white);
        CreateUIText("User ID Label", canvasObject.transform, "User ID", 17, new Vector2(-385f, 252f), new Vector2(120f, 38f), TextAnchor.MiddleLeft, mutedColor);
        manager.userIdInput = CreateUIInputField("User ID Input", canvasObject.transform, manager.userId, "Enter user ID", new Vector2(-240f, 252f), new Vector2(255f, 42f));
        var userIdBinder = manager.userIdInput.gameObject.AddComponent<XRTrainingUserIdInput>();
        userIdBinder.manager = manager;

        manager.conditionText = CreateUIText("Condition", canvasObject.transform, "Condition: LLM-Assisted", 17, new Vector2(182f, 252f), new Vector2(430f, 38f), TextAnchor.MiddleLeft, new Color(0.72f, 0.94f, 0.92f, 1f));
        manager.difficultyText = CreateUIText("Difficulty", canvasObject.transform, "Difficulty: Easy   Blocks: 3", 18, new Vector2(0f, 206f), new Vector2(900f, 36f), TextAnchor.MiddleCenter, new Color(0.92f, 0.98f, 1f, 1f));
        manager.selectedObjectText = CreateUIText("Selected Object", canvasObject.transform, "Selected: none", 18, new Vector2(0f, 160f), new Vector2(900f, 34f), TextAnchor.MiddleLeft, bodyColor);
        manager.scoreText = CreateUIText("Score", canvasObject.transform, "Score: 0 / 3   Time: 0.0s / 5.0s", 18, new Vector2(0f, 120f), new Vector2(900f, 34f), TextAnchor.MiddleLeft, bodyColor);
        manager.statusText = CreateUIText("Status", canvasObject.transform, "Press Start to begin.", 16, new Vector2(0f, 68f), new Vector2(900f, 48f), TextAnchor.MiddleLeft, mutedColor);
        CreateUIText("AI Label", canvasObject.transform, "AI Assistance", 15, new Vector2(-376f, 14f), new Vector2(160f, 28f), TextAnchor.MiddleLeft, accentColor);
        manager.aiText = CreateUIText("AI Text", canvasObject.transform, "AI: start a round and request help if needed.", 16, new Vector2(30f, 2f), new Vector2(790f, 72f), TextAnchor.MiddleLeft, new Color(0.9f, 0.97f, 1f, 1f));
        manager.completionText = CreateUIText("Completion", canvasObject.transform, "State: Waiting to start.", 18, new Vector2(0f, -94f), new Vector2(900f, 58f), TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.34f, 1f));

        var easyColor = new Color(0.13f, 0.39f, 0.83f, 0.94f);
        var normalColor = new Color(0.55f, 0.25f, 0.66f, 0.94f);
        var conditionColor = new Color(0.08f, 0.55f, 0.5f, 0.94f);
        var startColor = new Color(0.08f, 0.57f, 0.32f, 0.96f);
        var resetColor = new Color(0.34f, 0.38f, 0.46f, 0.96f);
        var lightColor = new Color(0.76f, 0.49f, 0.1f, 0.96f);
        var finishColor = new Color(0.8f, 0.31f, 0.2f, 0.96f);

        manager.easyDifficultyButton = CreateUIButton("Easy Difficulty Button", canvasObject.transform, "Easy", new Vector2(-246f, -202f), new Vector2(150f, 48f), easyColor);
        manager.normalDifficultyButton = CreateUIButton("Normal Difficulty Button", canvasObject.transform, "Normal", new Vector2(-60f, -202f), new Vector2(160f, 48f), normalColor);
        manager.conditionButton = CreateUIButton("Condition Button", canvasObject.transform, "AI On", new Vector2(140f, -202f), new Vector2(150f, 48f), conditionColor);
        manager.startTaskButton = CreateUIButton("Start Button", canvasObject.transform, "Start", new Vector2(-300f, -276f), new Vector2(140f, 50f), startColor);
        manager.resetButton = CreateUIButton("Reset Button", canvasObject.transform, "Reset", new Vector2(-128f, -276f), new Vector2(140f, 50f), resetColor);
        manager.lightButton = CreateUIButton("Light Button", canvasObject.transform, "Light", new Vector2(44f, -276f), new Vector2(140f, 50f), lightColor);
        manager.finishButton = CreateUIButton("Go Finish Button", canvasObject.transform, "Go Finish", new Vector2(244f, -276f), new Vector2(174f, 50f), finishColor);

        const float panelScale = 0.00175f;
        var panelCenter = new Vector3(0f, 2.18f, 2.85f);
        manager.difficultyMeshText = CreatePanelWorldText("Difficulty World Text", parent, "Difficulty: Easy   Blocks: 3", PanelWorldPoint(panelCenter, new Vector2(0f, 206f), panelScale), 0.0096f, new Color(0.88f, 0.98f, 1f, 1f));
        manager.selectedObjectMeshText = CreatePanelWorldText("Selected Object World Text", parent, "Selected: none", PanelWorldPoint(panelCenter, new Vector2(0f, 160f), panelScale), 0.0102f, bodyColor);
        manager.scoreMeshText = CreatePanelWorldText("Score World Text", parent, "Score: 0 / 3   Time: 0.0s / 5.0s", PanelWorldPoint(panelCenter, new Vector2(0f, 120f), panelScale), 0.0102f, bodyColor);
        manager.conditionMeshText = CreatePanelWorldText("Condition World Text", parent, "Condition: LLM-Assisted", PanelWorldPoint(panelCenter, new Vector2(182f, 252f), panelScale), 0.0092f, new Color(0.72f, 0.94f, 0.92f, 1f));
        manager.statusMeshText = CreatePanelWorldText("Status World Text", parent, "Press Start to begin.", PanelWorldPoint(panelCenter, new Vector2(0f, 68f), panelScale), 0.0086f, mutedColor);
        manager.aiMeshText = CreatePanelWorldText("AI World Text", parent, "AI: start a round and request help if needed.", PanelWorldPoint(panelCenter, new Vector2(30f, 2f), panelScale), 0.0086f, new Color(0.9f, 0.97f, 1f, 1f));
        manager.completionMeshText = CreatePanelWorldText("Completion World Text", parent, "State: Waiting to start.", PanelWorldPoint(panelCenter, new Vector2(0f, -94f), panelScale), 0.0096f, new Color(1f, 0.88f, 0.34f, 1f));

        CreatePanelWorldText("Title World Text", parent, "XR Training Tasks", PanelWorldPoint(panelCenter, new Vector2(0f, 296f), panelScale), 0.0155f, Color.white);
        CreatePanelWorldText("User ID Label World Text", parent, "User ID", PanelWorldPoint(panelCenter, new Vector2(-365f, 246f), panelScale), 0.0095f, mutedColor);
        CreatePanelWorldText("AI Label World Text", parent, "AI Assistance", PanelWorldPoint(panelCenter, new Vector2(-356f, 10f), panelScale), 0.008f, accentColor);
        CreatePanelWorldText("Easy Difficulty Button World Text", parent, "Easy", PanelWorldPoint(panelCenter, new Vector2(-246f, -202f), panelScale), 0.0105f, Color.white);
        CreatePanelWorldText("Normal Difficulty Button World Text", parent, "Normal", PanelWorldPoint(panelCenter, new Vector2(-60f, -202f), panelScale), 0.0098f, Color.white);
        CreatePanelWorldText("Condition Button World Text", parent, "AI On", PanelWorldPoint(panelCenter, new Vector2(140f, -202f), panelScale), 0.0096f, Color.white);
        CreatePanelWorldText("Start Button World Text", parent, "Start", PanelWorldPoint(panelCenter, new Vector2(-300f, -276f), panelScale), 0.0108f, Color.white);
        CreatePanelWorldText("Reset Button World Text", parent, "Reset", PanelWorldPoint(panelCenter, new Vector2(-128f, -276f), panelScale), 0.0108f, Color.white);
        CreatePanelWorldText("Light Button World Text", parent, "Light", PanelWorldPoint(panelCenter, new Vector2(44f, -276f), panelScale), 0.0108f, Color.white);
        CreatePanelWorldText("Go Finish Button World Text", parent, "Go Finish", PanelWorldPoint(panelCenter, new Vector2(244f, -276f), panelScale), 0.0098f, Color.white);

        CreatePanelButtonHitbox("Easy Difficulty Button Hitbox", parent, manager, manager.easyDifficultyButton, XRTrainingPanelAction.EasyDifficulty, PanelWorldPoint(panelCenter, new Vector2(-246f, -202f), panelScale), new Vector2(150f, 48f), panelScale);
        CreatePanelButtonHitbox("Normal Difficulty Button Hitbox", parent, manager, manager.normalDifficultyButton, XRTrainingPanelAction.NormalDifficulty, PanelWorldPoint(panelCenter, new Vector2(-60f, -202f), panelScale), new Vector2(160f, 48f), panelScale);
        CreatePanelButtonHitbox("Condition Button Hitbox", parent, manager, manager.conditionButton, XRTrainingPanelAction.ToggleCondition, PanelWorldPoint(panelCenter, new Vector2(140f, -202f), panelScale), new Vector2(150f, 48f), panelScale);
        CreatePanelButtonHitbox("Start Button Hitbox", parent, manager, manager.startTaskButton, XRTrainingPanelAction.Start, PanelWorldPoint(panelCenter, new Vector2(-300f, -276f), panelScale), new Vector2(140f, 50f), panelScale);
        CreatePanelButtonHitbox("Reset Button Hitbox", parent, manager, manager.resetButton, XRTrainingPanelAction.Reset, PanelWorldPoint(panelCenter, new Vector2(-128f, -276f), panelScale), new Vector2(140f, 50f), panelScale);
        CreatePanelButtonHitbox("Light Button Hitbox", parent, manager, manager.lightButton, XRTrainingPanelAction.ToggleLight, PanelWorldPoint(panelCenter, new Vector2(44f, -276f), panelScale), new Vector2(140f, 50f), panelScale);
        CreatePanelButtonHitbox("Go Finish Button Hitbox", parent, manager, manager.finishButton, XRTrainingPanelAction.GoFinish, PanelWorldPoint(panelCenter, new Vector2(244f, -276f), panelScale), new Vector2(174f, 50f), panelScale);

        UnityEventTools.AddPersistentListener(manager.startTaskButton.onClick, manager.StartTask);
        UnityEventTools.AddPersistentListener(manager.easyDifficultyButton.onClick, manager.SelectEasyDifficulty);
        UnityEventTools.AddPersistentListener(manager.normalDifficultyButton.onClick, manager.SelectNormalDifficulty);
        UnityEventTools.AddPersistentListener(manager.conditionButton.onClick, manager.ToggleExperimentCondition);
        UnityEventTools.AddPersistentListener(manager.resetButton.onClick, manager.ResetTask);
        UnityEventTools.AddPersistentListener(manager.lightButton.onClick, manager.ToggleLight);
        UnityEventTools.AddPersistentListener(manager.finishButton.onClick, manager.TryTeleportToFinish);
    }

    static Vector3 PanelWorldPoint(Vector3 panelCenter, Vector2 anchoredPosition, float panelScale)
    {
        return panelCenter + new Vector3(-anchoredPosition.x * panelScale, anchoredPosition.y * panelScale, -0.045f);
    }

    static TextMesh CreatePanelWorldText(string name, Transform parent, string text, Vector3 position, float characterSize, Color color)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = position;
        textObject.transform.localRotation = Quaternion.identity;

        var textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = GetBuiltinFont();
        textMesh.fontSize = 64;
        textMesh.characterSize = characterSize;
        textMesh.lineSpacing = 1.12f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;

        var renderer = textObject.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sortingOrder = 100;
            if (textMesh.font != null)
                renderer.sharedMaterial = textMesh.font.material;
        }

        return textMesh;
    }

    static XRTrainingPanelButton CreatePanelButtonHitbox(string name, Transform parent, XRTrainingManager manager, Button visualButton, XRTrainingPanelAction action, Vector3 position, Vector2 pixelSize, float panelScale)
    {
        var hitboxObject = new GameObject(name);
        hitboxObject.transform.SetParent(parent, false);
        hitboxObject.transform.localPosition = position;
        hitboxObject.transform.localRotation = Quaternion.identity;

        var collider = hitboxObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(pixelSize.x * panelScale, pixelSize.y * panelScale, 0.08f);

        var interactable = hitboxObject.AddComponent<XRSimpleInteractable>();
        interactable.interactionLayers = InteractionLayerMask.GetMask("Default");

        var panelButton = hitboxObject.AddComponent<XRTrainingPanelButton>();
        panelButton.manager = manager;
        panelButton.visualButton = visualButton;
        panelButton.action = action;
        return panelButton;
    }

    static GameObject CreateUIImage(string name, Transform parent, Vector2 size, Vector2 anchoredPosition, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        var rect = imageObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        var image = imageObject.GetComponent<Image>();
        image.color = color;
        return imageObject;
    }

    static Text CreateUIText(string name, Transform parent, string text, int fontSize, Vector2 anchoredPosition, Vector2 size, TextAnchor alignment, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;
        rect.localPosition += Vector3.forward * 0.01f;

        var label = textObject.GetComponent<Text>();
        label.text = text;
        label.font = GetBuiltinFont();
        label.fontSize = fontSize;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = Mathf.Max(10, fontSize - 7);
        label.resizeTextMaxSize = fontSize;
        label.lineSpacing = 1.16f;
        label.alignment = alignment;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    static InputField CreateUIInputField(string name, Transform parent, string initialValue, string placeholderValue, Vector2 anchoredPosition, Vector2 size)
    {
        var inputObject = CreateUIImage(name, parent, size, anchoredPosition, new Color(0.04f, 0.055f, 0.075f, 0.95f));
        var input = inputObject.AddComponent<InputField>();
        input.targetGraphic = inputObject.GetComponent<Image>();
        input.lineType = InputField.LineType.SingleLine;
        input.characterLimit = 32;
        input.caretColor = Color.white;
        input.selectionColor = new Color(0.25f, 0.72f, 0.88f, 0.45f);

        var placeholder = CreateUIText("Placeholder", inputObject.transform, placeholderValue, 16, Vector2.zero, new Vector2(size.x - 22f, size.y), TextAnchor.MiddleLeft, new Color(0.52f, 0.6f, 0.7f, 1f));
        placeholder.raycastTarget = false;
        var content = CreateUIText("Input Text", inputObject.transform, initialValue, 17, Vector2.zero, new Vector2(size.x - 22f, size.y), TextAnchor.MiddleLeft, Color.white);
        content.raycastTarget = false;
        input.placeholder = placeholder;
        input.textComponent = content;
        input.text = initialValue;
        return input;
    }

    static Button CreateUIButton(string name, Transform parent, string text, Vector2 anchoredPosition, Vector2 size, Color normalColor)
    {
        var buttonObject = CreateUIImage(name, parent, size, anchoredPosition, normalColor);
        var button = buttonObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(normalColor, Color.black, 0.24f);
        colors.selectedColor = Color.Lerp(normalColor, Color.white, 0.1f);
        colors.disabledColor = new Color(0.16f, 0.17f, 0.2f, 0.55f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        var label = CreateUIText(text + " Text", buttonObject.transform, text, 19, Vector2.zero, size, TextAnchor.MiddleCenter, Color.white);
        label.raycastTarget = false;
        return button;
    }

    static GameObject CreateWorldLabel(string text, Transform parent, Vector3 position, Color color)
    {
        var labelObject = new GameObject(text + " Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.position = position;
        labelObject.transform.rotation = Quaternion.Euler(70f, 0f, 0f);

        var textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = GetBuiltinFont();
        textMesh.fontSize = 64;
        textMesh.characterSize = 0.024f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;
        return labelObject;
    }

    static Font GetBuiltinFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
               Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    static void ConfigureMainCameraForSimulator(Camera camera)
    {
        if (camera == null)
            return;

        var additionalData = camera.GetComponent("UniversalAdditionalCameraData");
        if (additionalData == null)
            return;

        var serializedObject = new SerializedObject(additionalData);
        var allowXRRendering = serializedObject.FindProperty("m_AllowXRRendering");
        if (allowXRRendering == null)
            return;

        allowXRRendering.boolValue = true;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    static void DeleteGeneratedTrainingObjects()
    {
        DeleteIfExists(RootName);
        DeleteIfExists(LegacyRootName);
        DeleteSceneObjectsByName("VR Task Panel", "World Task Status Board", "XR Training Manager", "Finish Completion Zone");
    }

    static void DeleteIfExists(string name)
    {
        foreach (var existing in FindSceneObjects(name))
            Object.DestroyImmediate(existing);
    }

    static void DeleteSceneObjectsByName(params string[] names)
    {
        var namesToDelete = new HashSet<string>(names);
        var toDelete = new List<GameObject>();
        foreach (var transform in Object.FindObjectsOfType<Transform>(true))
        {
            var go = transform.gameObject;
            if (!go.scene.IsValid() || EditorUtility.IsPersistent(go))
                continue;

            if (namesToDelete.Contains(go.name))
                toDelete.Add(go);
        }

        foreach (var go in toDelete)
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }
    }

    static Transform FindTransform(string path)
    {
        var target = GameObject.Find(path);
        if (target != null)
            return target.transform;

        int slash = path.LastIndexOf('/');
        string objectName = slash >= 0 && slash + 1 < path.Length ? path.Substring(slash + 1) : path;
        var sceneObject = FindSceneObject(objectName);
        return sceneObject != null ? sceneObject.transform : null;
    }

    static GameObject FindSceneObject(string name)
    {
        foreach (var go in FindSceneObjects(name))
            return go;

        return null;
    }

    static List<GameObject> FindSceneObjects(string name)
    {
        var matches = new List<GameObject>();
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name != name)
                continue;

            if (EditorUtility.IsPersistent(go) || !go.scene.IsValid())
                continue;

            matches.Add(go);
        }

        return matches;
    }

    static void AddActionAsset(List<InputActionAsset> assets, string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        if (asset != null && !assets.Contains(asset))
            assets.Add(asset);
    }

    static void EnsureMaterialFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/materials"))
            AssetDatabase.CreateFolder("Assets", "materials");

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/materials", "XRTraining");
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        WriteMaterialColor(material, color);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void AssignMaterial(GameObject target, Material material)
    {
        var renderer = target.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
    }

    static Color ReadMaterialColor(Material material)
    {
        if (material != null && material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material != null && material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    static void WriteMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }
}
