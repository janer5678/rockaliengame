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
            var container = MakePrefab("Container", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<Container>();
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

            // cars and horses: whoever drives owns it and moves it
            var vehicle = MakePrefab("Vehicle", go =>
            {
                go.AddComponent<NetworkObject>();
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                go.AddComponent<Vehicle>();
            });

            var prefabs = new[] { player, netGame, structure, node, ball, container, vehicle };
            RefreshNetworkHashes(prefabs);

            BuildScene(player, netGame, structure, node, ball, container, vehicle, baseMat, ghostMat);

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

        /// <summary>Where the Windows build goes (its data folder is "Alien Rock Game_Data" next to it).</summary>
        public const string ExePath = "Builds/Windows/Alien Rock Game.exe";

        [MenuItem("Rock Game/Build Windows Player")]
        public static void BuildWindows()
        {
            EnsurePostFxVariants();
            // no Unity splash on launch (Unity 6 allows this on every licence)
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            // The exe is "Alien Rock Game.exe" but productName stays "Rock Base Brawl": on Windows PlayerPrefs live under
            // HKCU\Software\<company>\<product> (persistentDataPath uses it too), so renaming the product would reset
            // everyone's saved settings, binds and looks. (An old RockBaseBrawl.exe + RockBaseBrawl_Data next to it is
            // just a stale build: safe to delete by hand.)
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            // steam_appid.txt next to the exe (SteamConfig.cs): the build runs on Steam's free relay without being started by
            // Steam. (A build uploaded to Steam leaves this file out.)
            if (report.summary.result == BuildResult.Succeeded)
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ExePath), "steam_appid.txt"), RockGame.SteamConfig.AppId.ToString());
            Debug.Log($"[RockGame] Build result: {report.summary.result}, errors: {report.summary.totalErrors}, size: {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Where the Mac build goes: one app for Intel and Apple Silicon Macs.</summary>
        public const string MacAppPath = "Builds/Mac/Alien Rock Game.app";

        /// <summary>
        /// The Mac build (needs Unity's "Mac Build Support (Mono)" module). It isn't signed, so on the Mac the first launch
        /// is right-click > Open (or System Settings > Privacy & Security > Open Anyway). Steam: SteamBoot sets SteamAppId,
        /// so no steam_appid.txt is needed inside the app.
        /// </summary>
        [MenuItem("Rock Game/Build Mac Player")]
        public static void BuildMac()
        {
            EnsurePostFxVariants();
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            // the Mac asks before the game gets the microphone, with this line (Unity keeps it under iOS for both)
            PlayerSettings.iOS.microphoneUsageDescription = "Voice chat with the other players.";
            // Intel + Apple Silicon in one app (what UnityEditor.OSXStandalone.UserBuildSettings.architecture sets; written
            // this way so the editor still compiles without the Mac module)
            EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "x64ARM64");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = MacAppPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result == BuildResult.Succeeded) ZipMacApp();
            Debug.Log($"[RockGame] Mac build result: {report.summary.result}, errors: {report.summary.totalErrors}, size: {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>
        /// Builds/Mac/Alien Rock Game.zip: the app with its Mac file permissions. Windows has no "executable" flag, so the
        /// .app folder copied over as it is won't start; this zip marks the programs (Mach-O files) executable - send the
        /// zip, double-click it on the Mac.
        /// </summary>
        static void ZipMacApp()
        {
            string app = System.IO.Path.GetFullPath(MacAppPath);
            string zip = System.IO.Path.ChangeExtension(app, ".zip");
            string root = System.IO.Path.GetDirectoryName(app);
            if (System.IO.File.Exists(zip)) System.IO.File.Delete(zip);
            using (var archive = new System.IO.Compression.ZipArchive(System.IO.File.Create(zip), System.IO.Compression.ZipArchiveMode.Create))
            foreach (string file in System.IO.Directory.GetFiles(app, "*", System.IO.SearchOption.AllDirectories))
            {
                string name = file.Substring(root.Length + 1).Replace('\\', '/');
                var entry = archive.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
                bool exe = IsMachO(file);
                // unix mode in the high 16 bits: regular file, 755 for programs, 644 for the rest
                entry.ExternalAttributes = (int)((0x8000u | (exe ? 0x1EDu : 0x1A4u)) << 16);
                using var src = System.IO.File.OpenRead(file);
                using var dst = entry.Open();
                src.CopyTo(dst);
            }
            MarkZipMadeOnUnix(zip);
        }

        /// <summary>
        /// .NET on Windows writes "made by: Windows" in each entry, and the Mac's unzipper then ignores the permissions:
        /// this sets it to Unix (the high byte of "version made by" in every central directory entry).
        /// </summary>
        static void MarkZipMadeOnUnix(string zip)
        {
            using var f = new System.IO.FileStream(zip, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
            using var r = new System.IO.BinaryReader(f);
            long eocd = f.Length - 22;
            for (; eocd >= 0; eocd--) { f.Position = eocd; if (r.ReadUInt32() == 0x06054b50) break; }
            if (eocd < 0) throw new System.Exception("Mac zip: no end of central directory");
            f.Position = eocd + 10;
            int count = r.ReadUInt16();
            f.Position = eocd + 16;
            long pos = r.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                f.Position = pos;
                if (r.ReadUInt32() != 0x02014b50) throw new System.Exception("Mac zip: bad central directory");
                f.Position = pos + 5;
                f.WriteByte(3);
                f.Position = pos + 28;
                int n = r.ReadUInt16(), e = r.ReadUInt16(), c = r.ReadUInt16();
                pos += 46 + n + e + c;
            }
        }

        static bool IsMachO(string file)
        {
            var b = new byte[4];
            using (var f = System.IO.File.OpenRead(file)) if (f.Read(b, 0, 4) < 4) return false;
            uint m = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            return m == 0xCAFEBABE || m == 0xFEEDFACF || m == 0xCFFAEDFE || m == 0xFEEDFACE || m == 0xCEFAEDFE;
        }

        /// <summary>
        /// URP strips the post processing shader variants that no volume profile in Assets uses. The extra looks in
        /// Settings > Display (PostFx.cs) make their profile at runtime, so this profile - never used in the game - is
        /// only here so depth of field, film grain and chromatic aberration stay in the build.
        /// </summary>
        static void EnsurePostFxVariants()
        {
            const string path = "Assets/Settings/PostFxVariants.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) return;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            foreach (var t in new[] { typeof(UnityEngine.Rendering.Universal.DepthOfField), typeof(UnityEngine.Rendering.Universal.FilmGrain), typeof(UnityEngine.Rendering.Universal.ChromaticAberration) })
            {
                var c = p.Add(t, false);
                c.name = t.Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
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

        static void BuildScene(GameObject player, GameObject netGame, GameObject structure, GameObject node, GameObject ball, GameObject container, GameObject vehicle, Material baseMat, Material ghostMat)
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
            boot.containerPrefab = container;
            boot.vehiclePrefab = vehicle;
            boot.baseMaterial = baseMat;
            boot.ghostMaterial = ghostMat;
            net.AddComponent<Hud>();
            net.AddComponent<AutoTest>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
