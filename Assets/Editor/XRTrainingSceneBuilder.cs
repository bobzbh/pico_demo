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
        var red = GetOrCreateMaterial("Training_Red", new Color(1f, 0.12f, 0.12f, 1f));
        var green = GetOrCreateMaterial("Training_Green", new Color(0.1f, 0.78f, 0.28f, 1f));
        var blue = GetOrCreateMaterial("Training_Blue", new Color(0.1f, 0.35f, 1f, 1f));
        var yellow = GetOrCreateMaterial("Training_Yellow", new Color(1f, 0.88f, 0.1f, 1f));
        var purple = GetOrCreateMaterial("Training_Purple", new Color(0.58f, 0.22f, 0.95f, 1f));
        var distractor = GetOrCreateMaterial("Training_Distractor", new Color(0.2f, 0.2f, 0.2f, 1f));
        var platform = GetOrCreateMaterial("Training_Platform", new Color(0.35f, 0.38f, 0.42f, 1f));
        var start = GetOrCreateMaterial("Training_Start", new Color(0.18f, 0.45f, 0.95f, 1f));
        var finish = GetOrCreateMaterial("Training_Finish", new Color(0.92f, 0.72f, 0.16f, 1f));
        var panel = GetOrCreateMaterial("Training_Panel", new Color(0.08f, 0.09f, 0.11f, 0.96f));

        DeleteIfExists(RootName);
        DeleteSceneObjectsByName("VR Task HUD", "VR Operation Panel", "VR Task Panel", "World Task Status Board", "Board Difficulty", "Board Timer", "Board Score", "Board Selected", "Board Status", "Board Instructions", "Board Completion");
        MoveLegacyObjectsAside();

        var root = new GameObject(RootName);
        var zonesRoot = new GameObject("Zones").transform;
        var objectsRoot = new GameObject("Objects").transform;
        var targetsRoot = new GameObject("Targets").transform;
        var uiRoot = new GameObject("UI").transform;
        var flowRoot = new GameObject("Flow").transform;
        zonesRoot.SetParent(root.transform, false);
        objectsRoot.SetParent(root.transform, false);
        targetsRoot.SetParent(root.transform, false);
        uiRoot.SetParent(root.transform, false);
        flowRoot.SetParent(root.transform, false);

        var origin = EnsureXROrigin();
        ConfigureOriginStart(origin);
        var mainCamera = EnsureMainCamera(origin);
        EnsureInteractionManager();
        EnsureEventSystem();
        EnsureInputActions();
        EnsureSimulator();
        EnsureControllerRayVisuals();
        RestoreControllerVisualRenderers();
        var teleportationProvider = EnsureLocomotion(origin);
        var sceneLight = EnsureLight();

        var startArea = CreatePlatform("Start Zone", zonesRoot, new Vector3(0f, -0.025f, 0f), new Vector3(2.5f, 0.05f, 2.2f), start, true, teleportationProvider);
        var operationArea = CreatePlatform("Operation Zone", zonesRoot, new Vector3(0f, -0.025f, 4.25f), new Vector3(7.4f, 0.05f, 4.35f), platform, true, teleportationProvider);
        var finishArea = CreatePlatform("Finish Zone", zonesRoot, new Vector3(0f, -0.025f, 8f), new Vector3(2.6f, 0.05f, 2.2f), finish, false, teleportationProvider);
        CreateWorldLabel("Start", zonesRoot, new Vector3(0f, 0.05f, -0.7f), Color.white);
        CreateWorldLabel("Task Area", zonesRoot, new Vector3(0f, 0.05f, 2.55f), Color.white);
        CreateWorldLabel("Finish", zonesRoot, new Vector3(0f, 0.05f, 7.3f), Color.black);

        var managerObject = new GameObject("XR Training Flow Manager");
        managerObject.transform.SetParent(flowRoot, false);
        var manager = managerObject.AddComponent<XRTrainingManager>();
        manager.xrOrigin = origin != null ? origin.transform : null;
        manager.headTransform = mainCamera != null ? mainCamera.transform : null;
        manager.leftControllerTransform = FindTransform("XR Origin (XR Rig)/Camera Offset/Left Controller");
        manager.rightControllerTransform = FindTransform("XR Origin (XR Rig)/Camera Offset/Right Controller");
        manager.sceneLight = sceneLight;
        manager.finishTeleportArea = finishArea.GetComponent<TeleportationArea>();
        manager.selectedDifficulty = XRTrainingDifficulty.Easy;
        manager.easyConfig = XRTrainingDifficultyConfig.Easy();
        manager.normalConfig = XRTrainingDifficultyConfig.Normal();
        manager.hardConfig = XRTrainingDifficultyConfig.Hard();
        manager.dataLogger = managerObject.GetComponent<XRTrainingDataLogger>();
        if (manager.dataLogger == null)
            manager.dataLogger = managerObject.AddComponent<XRTrainingDataLogger>();

        manager.dataLogger.outputFolderName = "XRTrainingExperimentData";
        manager.teleportTracker = managerObject.GetComponent<XRTrainingTeleportTracker>();
        if (manager.teleportTracker == null)
            manager.teleportTracker = managerObject.AddComponent<XRTrainingTeleportTracker>();

        manager.teleportTracker.Configure(manager, manager.xrOrigin);
        manager.dataLogger.ConfigurePoseSources(manager.headTransform, manager.leftControllerTransform, manager.rightControllerTransform);
        var mouseGrabber = managerObject.AddComponent<XRTrainingMouseGrabber>();
        mouseGrabber.manager = manager;
        mouseGrabber.eventCamera = mainCamera;

        var finishTrigger = new GameObject("Finish Detection Zone");
        finishTrigger.transform.SetParent(zonesRoot, false);
        finishTrigger.transform.position = new Vector3(0f, 1f, 8f);
        finishTrigger.transform.localScale = new Vector3(2.8f, 2f, 2.4f);
        var finishCollider = finishTrigger.AddComponent<BoxCollider>();
        finishCollider.isTrigger = true;
        manager.finishZone = finishCollider;

        var grabbables = new[]
        {
            CreateTrainingCube("Red Cube", XRTrainingColorId.Red, objectsRoot, new Vector3(-1.35f, 0.55f, 3.25f), red, manager, false),
            CreateTrainingCube("Green Cube", XRTrainingColorId.Green, objectsRoot, new Vector3(-0.68f, 0.55f, 3.25f), green, manager, false),
            CreateTrainingCube("Blue Cube", XRTrainingColorId.Blue, objectsRoot, new Vector3(0f, 0.55f, 3.25f), blue, manager, false),
            CreateTrainingCube("Yellow Cube", XRTrainingColorId.Yellow, objectsRoot, new Vector3(0.68f, 0.55f, 3.25f), yellow, manager, false),
            CreateTrainingCube("Purple Cube", XRTrainingColorId.Purple, objectsRoot, new Vector3(1.35f, 0.55f, 3.25f), purple, manager, false),
            CreateTrainingCube("Gray Distractor A", XRTrainingColorId.Red, objectsRoot, new Vector3(-2.05f, 0.55f, 4.1f), distractor, manager, true),
            CreateTrainingCube("Gray Distractor B", XRTrainingColorId.Blue, objectsRoot, new Vector3(2.05f, 0.55f, 4.1f), distractor, manager, true)
        };

        var targetZones = new[]
        {
            CreateTargetZone("Red Target", XRTrainingColorId.Red, targetsRoot, new Vector3(-2.1f, 0.08f, 5.35f), red, manager),
            CreateTargetZone("Green Target", XRTrainingColorId.Green, targetsRoot, new Vector3(-1.05f, 0.08f, 5.35f), green, manager),
            CreateTargetZone("Blue Target", XRTrainingColorId.Blue, targetsRoot, new Vector3(0f, 0.08f, 5.35f), blue, manager),
            CreateTargetZone("Yellow Target", XRTrainingColorId.Yellow, targetsRoot, new Vector3(1.05f, 0.08f, 5.35f), yellow, manager),
            CreateTargetZone("Purple Target", XRTrainingColorId.Purple, targetsRoot, new Vector3(2.1f, 0.08f, 5.35f), purple, manager)
        };

        manager.grabbables = grabbables;
        manager.targetZones = targetZones;

        BuildWorldCanvas(uiRoot, manager, panel);

        foreach (var grabbable in grabbables)
            grabbable.CaptureInitialState();

        FixLegacyGrabColliders();
        ConfigureMainCameraForSimulator(mainCamera);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = root;
        Debug.Log("XR training scene built: XR Origin, Interaction Manager, controller rays, simulator, UI, grab task, matching task, teleport flow.");
    }

    static XROrigin EnsureXROrigin()
    {
        var origin = Object.FindObjectOfType<XROrigin>();
        if (origin != null)
            return origin;

        var originObject = new GameObject("XR Origin (XR Rig)");
        origin = originObject.AddComponent<XROrigin>();

        var cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(originObject.transform, false);
        cameraOffset.transform.localPosition = new Vector3(0f, 1.35f, 0f);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.transform.SetParent(cameraOffset.transform, false);
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = cameraOffset;

        new GameObject("Left Controller").transform.SetParent(cameraOffset.transform, false);
        new GameObject("Right Controller").transform.SetParent(cameraOffset.transform, false);
        return origin;
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

        camera.clearFlags = CameraClearFlags.Skybox;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 100f;
        return camera;
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

        var characterController = origin.GetComponent<CharacterController>();
        if (characterController == null)
            characterController = origin.gameObject.AddComponent<CharacterController>();

        characterController.enabled = false;
        characterController.height = 1.65f;
        characterController.radius = 0.25f;
        characterController.center = new Vector3(0f, 0.825f, 0f);
        characterController.stepOffset = 0.25f;
        characterController.skinWidth = 0.03f;
        characterController.enabled = true;
    }

    static void EnsureInteractionManager()
    {
        if (Object.FindObjectOfType<XRInteractionManager>() != null)
            return;

        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
    }

    static void EnsureEventSystem()
    {
        var eventSystem = Object.FindObjectOfType<EventSystem>();
        if (eventSystem == null)
            eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();

        foreach (var module in eventSystem.GetComponents<BaseInputModule>())
        {
            if (module is XRUIInputModule)
                continue;

            Object.DestroyImmediate(module);
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
    }

    static void EnsureInputActions()
    {
        var manager = Object.FindObjectOfType<InputActionManager>();
        if (manager == null)
            manager = new GameObject("Input Action Manager").AddComponent<InputActionManager>();

        var assets = new List<InputActionAsset>();
        AddActionAsset(assets, DefaultInputActionsPath);
        AddActionAsset(assets, SimulatorInputActionsPath);
        AddActionAsset(assets, ControllerInputActionsPath);
        manager.actionAssets = assets;
    }

    static void EnsureSimulator()
    {
        if (Object.FindObjectOfType<XRInteractionSimulator>() != null)
            return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("XR simulator prefab was not found. Import XRI XR Interaction Simulator sample if simulation is missing.");
            return;
        }

        var simulator = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (simulator != null)
            simulator.name = "XR Device Simulator (XRI 3.5)";
    }

    static void EnsureControllerRayVisuals()
    {
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Left Controller/Near-Far Interactor", "Left Training Ray");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Right Controller/Near-Far Interactor", "Right Training Ray");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Left Controller/Teleport Interactor", "Left Teleport Training Ray");
        EnsureRayVisualFor("XR Origin (XR Rig)/Camera Offset/Right Controller/Teleport Interactor", "Right Teleport Training Ray");

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
            curveCaster.castDistance = 8f;
        }

        foreach (var rayInteractor in Object.FindObjectsOfType<XRRayInteractor>(true))
        {
            rayInteractor.enableUIInteraction = true;
            rayInteractor.maxRaycastDistance = 7f;
        }
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

    static void EnsureRayVisualFor(string path, string visualName)
    {
        var interactor = GameObject.Find(path);
        if (interactor == null)
            return;

        if (interactor.GetComponent<XRTrainingRayVisual>() == null)
            interactor.AddComponent<XRTrainingRayVisual>();

        var lineRenderer = interactor.GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = interactor.AddComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = 0.006f;
        lineRenderer.endWidth = 0.002f;
        interactor.name = interactor.name.Contains(visualName) ? interactor.name : interactor.name;
    }

    static Light EnsureLight()
    {
        var light = Object.FindObjectOfType<Light>();
        if (light != null)
            return light;

        var lightObject = new GameObject("Directional Light");
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        return light;
    }

    static GameObject CreatePlatform(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool teleportEnabled, TeleportationProvider provider)
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

    static XRTrainingGrabbable CreateTrainingCube(string name, XRTrainingColorId colorId, Transform parent, Vector3 position, Material material, XRTrainingManager manager, bool isDistractor)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * 0.38f;
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
        training.isDistractor = isDistractor;

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
        collider.size = new Vector3(1f, 3f, 1f);
        collider.center = new Vector3(0f, 1.2f, 0f);

        var target = zone.AddComponent<XRTrainingTargetZone>();
        target.colorId = colorId;
        target.manager = manager;

        return target;
    }

    static void BuildWorldCanvas(Transform parent, XRTrainingManager manager, Material panelMaterial)
    {
        var canvasObject = new GameObject("VR Task Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(parent, false);
        canvasObject.transform.position = new Vector3(0f, 2.02f, 1.82f);
        canvasObject.transform.localRotation = Quaternion.identity;
        canvasObject.transform.localScale = Vector3.one * 0.00155f;

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 35;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 1500f;

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(760f, 430f);

        CreateUIImage("Panel Background", canvasObject.transform, new Vector2(760f, 430f), Vector2.zero, new Color(0.04f, 0.05f, 0.06f, 0.9f));

        CreateUIText("Title", canvasObject.transform, "Color Block VR Task", 30, new Vector2(0f, 179f), new Vector2(700f, 36f), TextAnchor.MiddleCenter, Color.white);
        manager.difficultyText = CreateUIText("Difficulty", canvasObject.transform, "Difficulty: Easy", 19, new Vector2(0f, 142f), new Vector2(700f, 26f), TextAnchor.MiddleLeft, new Color(0.92f, 0.96f, 1f, 1f));
        manager.timerText = CreateUIText("Timer", canvasObject.transform, "Time: 0.0s", 19, new Vector2(0f, 114f), new Vector2(700f, 26f), TextAnchor.MiddleLeft, Color.white);
        manager.scoreText = CreateUIText("Score", canvasObject.transform, "Score: 0", 19, new Vector2(0f, 86f), new Vector2(700f, 26f), TextAnchor.MiddleLeft, Color.white);
        manager.selectedObjectText = CreateUIText("Selected Object", canvasObject.transform, "Selected: none", 18, new Vector2(0f, 58f), new Vector2(700f, 24f), TextAnchor.MiddleLeft, Color.white);
        manager.statusText = CreateUIText("Status", canvasObject.transform, "Waiting to start.", 18, new Vector2(0f, 24f), new Vector2(700f, 38f), TextAnchor.MiddleLeft, new Color(0.82f, 0.92f, 1f, 1f));
        manager.instructionText = CreateUIText("Instructions", canvasObject.transform, "Choose Easy or Normal. Press Start Task or Enter. Hold RIGHT MOUSE on a cube to drag it, then release on the matching target.", 16, new Vector2(0f, -24f), new Vector2(700f, 48f), TextAnchor.MiddleLeft, new Color(0.88f, 0.9f, 0.84f, 1f));
        manager.completionText = CreateUIText("Completion", canvasObject.transform, "Task not started", 18, new Vector2(0f, -68f), new Vector2(700f, 26f), TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.35f, 1f));
        manager.resultText = CreateUIText("Result", canvasObject.transform, "Results will use the current round data.", 14, new Vector2(0f, -130f), new Vector2(700f, 70f), TextAnchor.UpperLeft, new Color(0.9f, 0.95f, 0.92f, 1f));

        var smallButtonSize = new Vector2(138f, 36f);
        var largeButtonSize = new Vector2(156f, 36f);
        manager.easyButton = CreateUIButton("Easy Button", canvasObject.transform, "Easy", new Vector2(-228f, -173f), smallButtonSize);
        manager.normalButton = CreateUIButton("Normal Button", canvasObject.transform, "Normal", new Vector2(-80f, -173f), smallButtonSize);
        manager.startTaskButton = CreateUIButton("Start Task Button", canvasObject.transform, "Start Task", new Vector2(84f, -173f), largeButtonSize);
        manager.restartButton = CreateUIButton("Reset Button", canvasObject.transform, "Reset", new Vector2(255f, -173f), smallButtonSize);

        UnityEventTools.AddPersistentListener(manager.easyButton.onClick, manager.SetEasyDifficulty);
        UnityEventTools.AddPersistentListener(manager.normalButton.onClick, manager.SetNormalDifficulty);
        UnityEventTools.AddPersistentListener(manager.startTaskButton.onClick, manager.StartTask);
        UnityEventTools.AddPersistentListener(manager.restartButton.onClick, manager.RestartTask);
    }

    static void BuildWorldStatusBoard(Transform parent, XRTrainingManager manager, Material panelMaterial)
    {
        var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "World Task Status Board";
        board.transform.SetParent(parent, false);
        board.transform.position = new Vector3(0f, 2.15f, 2.72f);
        board.transform.localScale = new Vector3(4.45f, 1.55f, 0.05f);
        AssignMaterial(board, panelMaterial);

        manager.difficultyMesh = CreateBoardText("Board Difficulty", parent, "Difficulty: Easy", new Vector3(-2.05f, 2.78f, 2.66f), 0.031f, new Color(0.92f, 0.96f, 1f, 1f));
        manager.timerMesh = CreateBoardText("Board Timer", parent, "Time: 0.0s", new Vector3(-2.05f, 2.62f, 2.66f), 0.031f, Color.white);
        manager.scoreMesh = CreateBoardText("Board Score", parent, "Score: 0", new Vector3(-2.05f, 2.46f, 2.66f), 0.031f, Color.white);
        manager.selectedObjectMesh = CreateBoardText("Board Selected", parent, "Selected: none", new Vector3(-2.05f, 2.30f, 2.66f), 0.028f, Color.white);
        manager.statusMesh = CreateBoardText("Board Status", parent, "Waiting to start.", new Vector3(-2.05f, 2.08f, 2.66f), 0.027f, new Color(0.82f, 0.92f, 1f, 1f));
        manager.instructionMesh = CreateBoardText("Board Instructions", parent, "Choose difficulty, then press Start.", new Vector3(-2.05f, 1.83f, 2.66f), 0.025f, new Color(0.88f, 0.9f, 0.84f, 1f));
        manager.completionMesh = CreateBoardText("Board Completion", parent, "Task not started", new Vector3(-2.05f, 1.57f, 2.66f), 0.03f, new Color(1f, 0.9f, 0.35f, 1f));
    }

    static TextMesh CreateBoardText(string name, Transform parent, string text, Vector3 position, float characterSize, Color color)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.position = position;
        textObject.transform.rotation = Quaternion.identity;

        var textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textMesh.fontSize = 64;
        textMesh.characterSize = characterSize;
        textMesh.anchor = TextAnchor.UpperLeft;
        textMesh.alignment = TextAlignment.Left;
        textMesh.color = color;
        return textMesh;
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

        var label = textObject.GetComponent<Text>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = fontSize;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = Mathf.Max(10, fontSize - 6);
        label.resizeTextMaxSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    static Button CreateUIButton(string name, Transform parent, string text, Vector2 anchoredPosition, Vector2 size)
    {
        var buttonObject = CreateUIImage(name, parent, size, anchoredPosition, new Color(0.18f, 0.34f, 0.72f, 0.62f));
        var button = buttonObject.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(0.28f, 0.48f, 0.95f, 0.75f);
        colors.pressedColor = new Color(0.1f, 0.22f, 0.56f, 0.85f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        CreateUIText(text + " Text", buttonObject.transform, text, 18, Vector2.zero, size, TextAnchor.MiddleCenter, Color.white);
        return button;
    }

    static TextMesh CreateCanvasTextMesh(string name, Transform parent, string text, Vector2 anchoredPosition, float characterSize, TextAnchor anchor, TextAlignment alignment, Color color)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = new Vector3(anchoredPosition.x, anchoredPosition.y, -8f);
        textObject.transform.localRotation = Quaternion.identity;

        var textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textMesh.fontSize = 72;
        textMesh.characterSize = characterSize;
        textMesh.anchor = anchor;
        textMesh.alignment = alignment;
        textMesh.color = color;
        return textMesh;
    }

    static void CreateWorldLabel(string text, Transform parent, Vector3 position, Color color)
    {
        var labelObject = new GameObject(text + " Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.position = position;
        labelObject.transform.rotation = Quaternion.Euler(65f, 0f, 0f);

        var textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.fontSize = 64;
        textMesh.characterSize = 0.023f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = color;
    }

    static void MoveLegacyObjectsAside()
    {
        var legacyRoot = GetOrCreateLegacyRoot();

        var names = new[] { "Canvas", "Grab Interactable", "Grab Interactable (1)", "Teleportation Area", "panel" };
        foreach (var name in names)
        {
            var go = FindSceneObject(name);
            if (go == null || go.transform.IsChildOf(legacyRoot.transform))
                continue;

            go.transform.SetParent(legacyRoot.transform, true);
            go.SetActive(false);
        }

        legacyRoot.SetActive(false);
    }

    static GameObject GetOrCreateLegacyRoot()
    {
        var roots = FindSceneObjects(LegacyRootName);
        GameObject primary = null;

        foreach (var root in roots)
        {
            if (primary == null)
            {
                primary = root;
                continue;
            }

            while (root.transform.childCount > 0)
                root.transform.GetChild(0).SetParent(primary.transform, true);

            Object.DestroyImmediate(root);
        }

        return primary != null ? primary : new GameObject(LegacyRootName);
    }

    static void FixLegacyGrabColliders()
    {
        foreach (var grab in Object.FindObjectsOfType<XRGrabInteractable>(true))
        {
            if (grab.GetComponent<Collider>() == null)
                grab.gameObject.AddComponent<BoxCollider>();
        }
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

        allowXRRendering.boolValue = false;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    static void DeleteIfExists(string name)
    {
        foreach (var existing in FindSceneObjects(name))
            Object.DestroyImmediate(existing);
    }

    static void DeleteSceneObjectsByName(params string[] names)
    {
        if (names == null || names.Length == 0)
            return;

        var namesToDelete = new HashSet<string>(names);
        var toDelete = new List<GameObject>();
        foreach (var sceneObject in Object.FindObjectsOfType<Transform>(true))
        {
            var go = sceneObject.gameObject;
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
        var path = MaterialFolder + "/" + name + ".mat";
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
