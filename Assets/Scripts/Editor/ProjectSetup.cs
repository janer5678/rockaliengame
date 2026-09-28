using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RockGame.EditorTools
{
    /// <summary>
    /// Generates everything the game needs (materials, network prefabs, the Game scene, player settings).
    /// Re-run any time from the "Rock Game" menu; it is idempotent.
    /// </summary>
    public static class ProjectSetup
    {
        const string GameDir = "Assets/Game";
        const string PrefabDir = GameDir + "/Prefabs";
        const string MatDir = GameDir + "/Materials";
        public const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Rock Game/Rebuild Scene, Prefabs and Settings")]
        public static void Setup()
        {
            EnsureFolder("Assets", "Game");
            EnsureFolder(GameDir, "Prefabs");
            EnsureFolder(GameDir, "Materials");
            EnsureFolder("Assets", "Scenes");

            var baseMat = CreateMaterial(MatDir + "/Base.mat", false);
            var ghostMat = CreateMaterial(MatDir + "/Ghost.mat", true);

            var player = MakePrefab("Player", go =>
            {
                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.4f;
                cc.center = new Vector3(0, 0.9f, 0);
                cc.slopeLimit = 50f;
                cc.stepOffset = 0.45f;
                cc.skinWidth = 0.05f;
                go.AddComponent<NetworkObject>();
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                go.AddComponent<PlayerNet>();
                go.AddComponent<PlayerController>();
            });
            var netGame = MakePrefab("NetGame", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<NetGame>();
            });
            var structure = MakePrefab("Structure", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<Structure>();
            });
            var node = MakePrefab("ResourceNode", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<ResourceNode>();
            });
            var ball = MakePrefab("Ball", go =>
            {
                go.AddComponent<NetworkObject>();
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 3f;
                rb.linearDamping = 0.4f;
                rb.angularDamping = 2f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                go.AddComponent<SphereCollider>().radius = 0.62f;
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                go.AddComponent<Ball>();
            });

            var prefabs = new[] { player, netGame, structure, node, ball };
            RefreshNetworkHashes(prefabs);

            BuildScene(player, netGame, structure, node, ball, baseMat, ghostMat);

            PlayerSettings.productName = "Rock Base Brawl";
            PlayerSettings.companyName = "RockGame";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[RockGame] Setup complete.");
        }

        [MenuItem("Rock Game/Build Windows Player")]
        public static void BuildWindows()
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/RockBaseBrawl.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[RockGame] Build result: {report.summary.result}, errors: {report.summary.totalErrors}, size: {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Batch entry point: setup then build.</summary>
        public static void SetupAndBuild()
        {
            Setup();
            BuildWindows();
        }

        // ------------------------------------------------------------------

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static Material CreateMaterial(string path, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", transparent ? new Color(0.3f, 1f, 0.4f, 0.4f) : Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f); // transparent
                mat.SetFloat("_Blend", 0f);   // alpha
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ReceiveShadows", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject MakePrefab(string name, System.Action<GameObject> build)
        {
            var go = new GameObject(name);
            build(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>NetworkObject computes its GlobalObjectIdHash in OnValidate; force it for freshly generated prefab assets.</summary>
        static void RefreshNetworkHashes(GameObject[] prefabs)
        {
            AssetDatabase.SaveAssets();
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var p in prefabs)
            {
                var no = p.GetComponent<NetworkObject>();
                onValidate?.Invoke(no, null);
                EditorUtility.SetDirty(p);
                Debug.Log($"[RockGame] {p.name} GlobalObjectIdHash = {hashField?.GetValue(no)}");
            }
            AssetDatabase.SaveAssets();
        }

        static void BuildScene(GameObject player, GameObject netGame, GameObject structure, GameObject node, GameObject ball, Material baseMat, Material ghostMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0, 55, -120);
            camGo.transform.LookAt(Vector3.zero);

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.52f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            RenderSettings.sun = light;

            var net = new GameObject("Network");
            var nm = net.AddComponent<NetworkManager>();
            var ut = net.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = ut;
            nm.NetworkConfig.PlayerPrefab = player;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = false;
            nm.NetworkConfig.TickRate = 30;
            var boot = net.AddComponent<Bootstrap>();
            boot.playerPrefab = player;
            boot.netGamePrefab = netGame;
            boot.structurePrefab = structure;
            boot.nodePrefab = node;
            boot.ballPrefab = ball;
            boot.baseMaterial = baseMat;
            boot.ghostMaterial = ghostMat;
            net.AddComponent<Hud>();
            net.AddComponent<AutoTest>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
