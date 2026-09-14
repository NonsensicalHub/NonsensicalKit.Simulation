#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using NonsensicalKit.ScriptAnimation;
using NonsensicalKit.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>一键生成 ScriptAnimation Timeline 演示场景（路网 + 示例设备）。</summary>
    public static class ScriptAnimationDemoSceneMenu
    {
        const string PackageId = "com.nonsensicallab.nonsensicalkit.simulation";
        const string SampleFolderName = "ScriptAnimation";
        const string DefaultCartonMatPath =
            "Packages/com.nonsensicallab.nonsensicalkit.simulation/ScriptAnimation/Runtime/OpenBox/Materials/Carton.mat";

        /// <summary>生成目标写在工程 Assets 下（包内 Samples~ 只读）。</summary>
        private const string SceneDir = "Assets/ScriptAnimationDemo";
        private const string MaterialsDir = SceneDir + "/Materials";
        private const string ScenePath = SceneDir + "/ScriptAnimationDemo.unity";
        private const string TimelinePath = SceneDir + "/ScriptAnimationDemo.playable";

        [MenuItem("Tools/ScriptAnimation/打开 OpenBox 演示场景")]
        public static void OpenOpenBoxDemoScene()
        {
            OpenSampleScene("OpenBox/OpenBox.unity");
        }

        [MenuItem("Tools/ScriptAnimation/打开缠膜机演示场景")]
        public static void OpenFilmWrappingDemoScene()
        {
            OpenSampleScene("FilmWrapping/FilmWrapping.unity");
        }

        [MenuItem("Tools/ScriptAnimation/创建或打开 Demo 场景")]
        public static void CreateOrOpenDemoScene()
        {
            EnsureFolder(SceneDir);
            EnsureFolder(MaterialsDir);

            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null)
                AssetDatabase.DeleteAsset(TimelinePath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(3.6f, 1f, 3.0f);
            ground.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(new Color(0.35f, 0.38f, 0.4f), "SA_Demo_Ground");

            var networkGo = new GameObject("PathNetwork");
            var network = networkGo.AddComponent<PathNetwork>();
            var n0 = CreateNode(networkGo.transform, "N0", new Vector3(-4f, 0f, -3f));
            var n1 = CreateNode(networkGo.transform, "N1", new Vector3(0f, 0f, -3f));
            var n2 = CreateNode(networkGo.transform, "N2", new Vector3(4f, 0f, -3f));
            var n3 = CreateNode(networkGo.transform, "N3", new Vector3(4f, 0f, 2f));
            var n4 = CreateNode(networkGo.transform, "N4", new Vector3(0f, 0f, 2f));
            network.CollectNodesFromChildren();
            network.AutoLinkNeighbors();

            var forkliftGo = CreateBox("DemoForklift", new Vector3(-4f, 0.4f, -3f), new Vector3(1f, 0.8f, 1.6f),
                new Color(0.95f, 0.7f, 0.2f));
            forkliftGo.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

            var forkGo = CreateBox("Fork", Vector3.zero, new Vector3(0.7f, 0.08f, 1.0f),
                new Color(0.75f, 0.75f, 0.78f));
            forkGo.transform.SetParent(forkliftGo.transform, false);
            forkGo.transform.localPosition = new Vector3(0f, 0.15f, 1.0f);

            var stationGo = new GameObject("CargoStation");
            stationGo.transform.position = new Vector3(0f, 0f, 4.2f);
            stationGo.AddComponent<ScriptAnimPoint>();
            var stationMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stationMarker.name = "StationMarker";
            stationMarker.transform.SetParent(stationGo.transform, false);
            stationMarker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            stationMarker.transform.localScale = new Vector3(0.6f, 0.05f, 0.6f);
            Object.DestroyImmediate(stationMarker.GetComponent<Collider>());
            stationMarker.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(new Color(0.3f, 0.85f, 0.4f), "SA_Demo_Station");

            var forkliftAnim = forkliftGo.AddComponent<ForkliftAnim>();
            SetField(forkliftAnim, "m_fork", forkGo.transform);
            SetField(forkliftAnim, "m_moveSpeed", 2.5f);
            SetField(forkliftAnim, "m_forkSpeed", 0.6f);
            SetField(forkliftAnim, "m_rotateSpeed", 120f);
            SetField(forkliftAnim, "m_approachDistance", 1.4f);
            // 先转再走：拐角/开场有明确原地旋转过程
            SetField(forkliftAnim, "m_moveMode", PathMoveMode.RotateThenMove);
            forkliftAnim.CaptureHomeFromCurrent();

            // —— 堆垛机：货位坐标（Int4）→ 世界位置（无 Warehouse 时用 CellPositionTable）——
            var cellA = new Int4(1, 0, 0, 0);
            var cellB = new Int4(2, 1, 2, 0);
            var cellC = new Int4(1, 2, 3, 0);
            Vector3 posA = new Vector3(-8f, 0.5f, -2f);
            Vector3 posB = new Vector3(-5f, 2.2f, 3f);
            Vector3 posC = new Vector3(-10f, 1.0f, 6f);

            var slotsRoot = new GameObject("StackerCells");
            CreateSlotMarker(slotsRoot.transform, "CellA_1-0-0-0", posA, new Color(0.4f, 0.7f, 1f));
            CreateSlotMarker(slotsRoot.transform, "CellB_2-1-2-0", posB, new Color(0.45f, 0.55f, 1f));
            CreateSlotMarker(slotsRoot.transform, "CellC_1-2-3-0", posC, new Color(0.35f, 0.85f, 0.95f));

            var cellTable = slotsRoot.AddComponent<CellPositionTable>();
            cellTable.SetEntries(new[]
            {
                new CellPositionTable.Entry { Cell = cellA, WorldPosition = posA },
                new CellPositionTable.Entry { Cell = cellB, WorldPosition = posB },
                new CellPositionTable.Entry { Cell = cellC, WorldPosition = posC }
            });

            var stackerRoot = new GameObject("DemoStacker");
            stackerRoot.transform.position = new Vector3(posA.x, 0f, posA.z);

            var travelGo = CreateBox("Travel", new Vector3(posA.x, 0.12f, posA.z),
                new Vector3(1.2f, 0.25f, 0.8f),
                new Color(0.25f, 0.55f, 0.9f));
            travelGo.transform.SetParent(stackerRoot.transform, true);

            var mastGo = CreateBox("Mast", Vector3.zero, new Vector3(0.15f, 3.2f, 0.15f),
                new Color(0.35f, 0.4f, 0.5f));
            mastGo.transform.SetParent(travelGo.transform, false);
            mastGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            Object.DestroyImmediate(mastGo.GetComponent<Collider>());

            var liftGo = CreateBox("Lift", Vector3.zero, new Vector3(0.9f, 0.2f, 0.55f),
                new Color(0.55f, 0.8f, 1f));
            liftGo.transform.SetParent(travelGo.transform, false);
            liftGo.transform.localPosition = new Vector3(0.55f, posA.y - travelGo.transform.position.y, 0f);

            // 货叉：挂在升降台上，沿本地 +Z 伸叉（机身不动）
            var stackerForkGo = CreateBox("Fork", Vector3.zero, new Vector3(0.55f, 0.08f, 0.9f),
                new Color(0.7f, 0.78f, 0.85f));
            stackerForkGo.transform.SetParent(liftGo.transform, false);
            stackerForkGo.transform.localPosition = new Vector3(0.35f, 0f, 0f);
            Object.DestroyImmediate(stackerForkGo.GetComponent<Collider>());

            var stackerAnim = stackerRoot.AddComponent<StackerAnim>();
            SetField(stackerAnim, "m_cellTable", cellTable);
            SetField(stackerAnim, "m_travelAxis", travelGo.transform);
            SetField(stackerAnim, "m_liftAxis", liftGo.transform);
            SetField(stackerAnim, "m_primaryFork", stackerForkGo.transform);
            SetField(stackerAnim, "m_primaryForkAxisLocal", Vector3.forward);
            SetField(stackerAnim, "m_travelSpeed", 2.2f);
            SetField(stackerAnim, "m_travelAcceleration", 1.8f);
            SetField(stackerAnim, "m_liftSpeed", 1.4f);
            SetField(stackerAnim, "m_liftAcceleration", 1.2f);
            SetField(stackerAnim, "m_primaryForkSpeed", 0.7f);
            stackerAnim.ApplySlotPosition(posA);
            stackerAnim.CaptureHomeFromCurrent();

            // —— 机械臂：配置连杆长度，Clip 设起终点 IK 补全动作（Demo 默认 6 轴） ——
            Vector3 armBasePos = new Vector3(2.8f, 0f, 1.2f);
            var armRoot = new GameObject("DemoRobotArm");
            armRoot.transform.position = armBasePos;
            Transform[] armJoints = CreateDemoRobotArmHierarchy(armRoot.transform);
            var robotArm = armRoot.AddComponent<RobotArmAnim>();
            ConfigureDemoRobotArm(robotArm, armJoints);

            // 最大可达约 1.7m；以下相对基座偏移均落在工作空间内
            var armTargetsRoot = new GameObject("ArmTargets");
            Transform armTargetA = CreateArmTarget(armTargetsRoot.transform, "ArmTargetA_MidFront",
                armBasePos + new Vector3(0.55f, 0.85f, 0.55f), new Color(1f, 0.45f, 0.35f));
            Transform armTargetB = CreateArmTarget(armTargetsRoot.transform, "ArmTargetB_LowRight",
                armBasePos + new Vector3(0.85f, 0.28f, -0.25f), new Color(1f, 0.75f, 0.3f));
            Transform armTargetC = CreateArmTarget(armTargetsRoot.transform, "ArmTargetC_MidLeft",
                armBasePos + new Vector3(-0.55f, 0.7f, 0.55f), new Color(0.95f, 0.4f, 0.7f));
            Transform armTargetD = CreateArmTarget(armTargetsRoot.transform, "ArmTargetD_HighNear",
                armBasePos + new Vector3(0.25f, 1.35f, 0.35f), new Color(0.55f, 0.9f, 0.4f));
            Transform armTargetE = CreateArmTarget(armTargetsRoot.transform, "ArmTargetE_FarFront",
                armBasePos + new Vector3(0.15f, 0.55f, 1.15f), new Color(0.4f, 0.75f, 1f));
            Transform armTargetF = CreateArmTarget(armTargetsRoot.transform, "ArmTargetF_NearTuck",
                armBasePos + new Vector3(0.35f, 0.55f, 0.2f), new Color(0.7f, 0.55f, 1f));
            Transform armTargetG = CreateArmTarget(armTargetsRoot.transform, "ArmTargetG_FarLeftBack",
                armBasePos + new Vector3(-0.9f, 0.45f, -0.55f), new Color(1f, 0.5f, 0.55f));
            Transform armTargetH = CreateArmTarget(armTargetsRoot.transform, "ArmTargetH_LowFront",
                armBasePos + new Vector3(0.4f, 0.22f, 0.75f), new Color(0.95f, 0.85f, 0.35f));

            // —— 剪叉举升小车：平台走 ForkliftAnim，剪叉由 ScissorLiftSync 跟底/顶铰点 ——
            ForkliftAnim scissorAnim = CreateDemoScissorLift(
                new Vector3(1.5f, 0f, -6.2f),
                out Transform scissorStation,
                out float scissorRestHeight,
                out float scissorLiftHeight);

            FilmWrapAnim filmWrapAnim = CreateDemoFilmWrap(new Vector3(4.2f, 0f, -7.0f));
            OpenBoxAnim openBoxAnim = CreateDemoOpenBox(new Vector3(-4.5f, 0f, -7.0f));

            // —— 五轴机械臂：RobotArm5Anim + Timeline 点位巡航（与六轴并行）——
            RobotArm5Anim robotArm5 = CreateDemoRobotArm5(
                "DemoRobot_5Axis",
                new Vector3(-7.2f, 0f, -6.5f),
                lengths: new[] { 0.32f, 0.48f, 0.38f, 0.14f, 0.10f },
                rotAxes: new[]
                {
                    Vector3.up, Vector3.right, Vector3.right,
                    Vector3.right, Vector3.up
                },
                palette: new Color(0.95f, 0.55f, 0.22f),
                waypoints: new[]
                {
                    new Vector3(0.45f, 0.75f, 0.4f),
                    new Vector3(0.7f, 0.3f, -0.15f),
                    new Vector3(-0.35f, 0.65f, 0.45f),
                    new Vector3(0.2f, 1.0f, 0.25f),
                    new Vector3(0.5f, 0.45f, 0.75f)
                },
                out Transform[] arm5Waypoints);

            // —— 潜伏车：演示 PathMove / 机动 Clip / 平台升降 ——
            LatentAgvAnim latentAgv = CreateDemoLatentAgv(
                new Vector3(0f, 0f, -3f),
                Quaternion.LookRotation(Vector3.right, Vector3.up));

            var directPointA = new GameObject("LatentDirectA");
            directPointA.transform.position = new Vector3(0f, 0f, -3f);
            directPointA.AddComponent<ScriptAnimPoint>();
            var directPointB = new GameObject("LatentDirectB");
            directPointB.transform.position = new Vector3(1.6f, 0f, -3f);
            directPointB.AddComponent<ScriptAnimPoint>();

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            var scriptTrack = timeline.CreateTrack<ScriptMovementTrack>(null, "Forklift");
            var stackerTrack = timeline.CreateTrack<ScriptMovementTrack>(null, "Stacker");
            var robotArmTrack = timeline.CreateTrack<ScriptDedicatedTrack>(null, "RobotArm");
            var robotArm5Track = timeline.CreateTrack<ScriptDedicatedTrack>(null, "RobotArm5");
            var scissorTrack = timeline.CreateTrack<ScriptMovementTrack>(null, "ScissorLift");
            var filmWrapTrack = timeline.CreateTrack<ScriptDedicatedTrack>(null, "FilmWrap");
            var openBoxTrack = timeline.CreateTrack<ScriptDedicatedTrack>(null, "OpenBox");
            var latentTrack = timeline.CreateTrack<ScriptMovementTrack>(null, "LatentAgv");

            var directorGo = new GameObject("ScriptAnimationDirector");
            var director = directorGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.extrapolationMode = DirectorWrapMode.None;
            director.playOnAwake = true;
            director.SetGenericBinding(scriptTrack, forkliftAnim);
            director.SetGenericBinding(stackerTrack, stackerAnim);
            director.SetGenericBinding(robotArmTrack, robotArm);
            director.SetGenericBinding(robotArm5Track, robotArm5);
            director.SetGenericBinding(scissorTrack, scissorAnim);
            director.SetGenericBinding(filmWrapTrack, filmWrapAnim);
            director.SetGenericBinding(openBoxTrack, openBoxAnim);
            director.SetGenericBinding(latentTrack, latentAgv);

            // 移动 N0→N4
            var pathClip1 = scriptTrack.CreateClip<PathMoveClip>();
            pathClip1.displayName = "Move N0→N4";
            pathClip1.start = 0;
            var pathAsset1 = (PathMoveClip)pathClip1.asset;
            BindPathClip(director, pathAsset1, network, n0, n4);
            float d1 = EstimatePath(network, n0, n4, pathAsset1.Data, forkliftAnim);
            if (d1 > 0f) pathClip1.duration = d1;
            EditorUtility.SetDirty(pathAsset1);

            ResolvePathEnd(network, n0, n4, pathAsset1.Data, out Vector3 homePos, out Quaternion homeRot, forkliftAnim);

            // 站点取货（Home = 路径终点）
            var forkClip = scriptTrack.CreateClip<ForkliftClip>();
            forkClip.displayName = "PickUp";
            forkClip.start = pathClip1.end;
            var forkAsset = (ForkliftClip)forkClip.asset;
            forkAsset.Data.Mode = ForkliftMode.PickUp;
            forkAsset.Data.AutoSyncDuration = true;
            forkAsset.Data.ForkStartTravelHeight = 0.15f;
            forkAsset.Data.ForkEndTravelHeight = 0.15f;
            forkAsset.Data.ForkPlaceHeight = 0.15f;
            forkAsset.Data.ForkLiftHeight = 0.55f;
            forkAsset.Station = BindExposed(director, EnsurePoint(stationGo.transform));
            // 前序 Move → Timed 旋转
            float fd = ForkliftSampler.EstimateDuration(
                forkliftAnim, forkAsset.Data, stationGo.transform, homePos, homeRot,
                ForkliftRotateMode.Timed);
            if (fd > 0f) forkClip.duration = fd;
            EditorUtility.SetDirty(forkAsset);

            // PutDown：前序为取货 → Skip 不旋转（Home=上一取货结束朝向货点）
            var putClip = scriptTrack.CreateClip<ForkliftClip>();
            putClip.displayName = "PutDown";
            putClip.start = forkClip.end;
            var putAsset = (ForkliftClip)putClip.asset;
            putAsset.Data.Mode = ForkliftMode.PutDown;
            putAsset.Data.AutoSyncDuration = true;
            putAsset.Data.ForkStartTravelHeight = 0.15f;
            putAsset.Data.ForkEndTravelHeight = 0.15f;
            putAsset.Data.ForkPlaceHeight = 0.15f;
            putAsset.Data.ForkLiftHeight = 0.55f;
            putAsset.Station = BindExposed(director, EnsurePoint(stationGo.transform));
            Vector3 toStation = stationGo.transform.position - homePos;
            Quaternion pickExitRot = forkliftAnim.LookRotation(toStation, homeRot);
            float pd = ForkliftSampler.EstimateDuration(
                forkliftAnim, putAsset.Data, stationGo.transform, homePos, pickExitRot,
                ForkliftRotateMode.Skip);
            if (pd > 0f) putClip.duration = pd;
            EditorUtility.SetDirty(putAsset);

            // 移动 N4→N2
            var pathClip2 = scriptTrack.CreateClip<PathMoveClip>();
            pathClip2.displayName = "Move N4→N2";
            pathClip2.start = putClip.end;
            var pathAsset2 = (PathMoveClip)pathClip2.asset;
            BindPathClip(director, pathAsset2, network, n4, n2);
            // 前序取放货结束朝向货点，开场需转到 N4→N2 第一段
            float d2 = EstimatePath(network, n4, n2, pathAsset2.Data, forkliftAnim, pickExitRot);
            if (d2 > 0f) pathClip2.duration = d2;
            EditorUtility.SetDirty(pathAsset2);

            // Teleport 回 N0：自定义朝向与开场一致（+X），便于循环复用
            var teleportClip = scriptTrack.CreateClip<TeleportClip>();
            teleportClip.displayName = "Teleport→N0";
            teleportClip.start = pathClip2.end;
            var teleportAsset = (TeleportClip)teleportClip.asset;
            BindTeleportClip(director, teleportAsset, n0, TeleportFacingMode.CustomYaw, 90f);
            teleportClip.duration = TeleportSampler.EstimateDuration(timeline);
            EditorUtility.SetDirty(teleportAsset);

            // Stacker: A→B 取货 → B→C 放货 → C→A（坐标经 CellPositionTable）
            var stackClip1 = stackerTrack.CreateClip<StackerClip>();
            stackClip1.displayName = "CellA→CellB";
            stackClip1.start = 0;
            var stackAsset1 = (StackerClip)stackClip1.asset;
            ConfigureStackerClip(stackAsset1, cellA, cellB, hasStart: true);
            float sd1 = StackerSampler.EstimateDuration(stackerAnim, stackAsset1.Data, posA, posB);
            if (sd1 > 0f) stackClip1.duration = sd1;
            EditorUtility.SetDirty(stackAsset1);

            var stackPick = stackerTrack.CreateClip<StackerForkClip>();
            stackPick.displayName = "PickUp@B";
            stackPick.start = stackClip1.end;
            var stackPickAsset = (StackerForkClip)stackPick.asset;
            ConfigureStackerForkClip(stackPickAsset, ForkliftMode.PickUp);
            float sp = StackerForkSampler.EstimateDuration(stackerAnim, stackPickAsset.Data);
            if (sp > 0f) stackPick.duration = sp;
            EditorUtility.SetDirty(stackPickAsset);

            var stackClip2 = stackerTrack.CreateClip<StackerClip>();
            stackClip2.displayName = "CellB→CellC";
            stackClip2.start = stackPick.end;
            var stackAsset2 = (StackerClip)stackClip2.asset;
            ConfigureStackerClip(stackAsset2, default, cellC, hasStart: false);
            float sd2 = StackerSampler.EstimateDuration(stackerAnim, stackAsset2.Data, posB, posC);
            if (sd2 > 0f) stackClip2.duration = sd2;
            EditorUtility.SetDirty(stackAsset2);

            var stackPut = stackerTrack.CreateClip<StackerForkClip>();
            stackPut.displayName = "PutDown@C";
            stackPut.start = stackClip2.end;
            var stackPutAsset = (StackerForkClip)stackPut.asset;
            ConfigureStackerForkClip(stackPutAsset, ForkliftMode.PutDown);
            float su = StackerForkSampler.EstimateDuration(stackerAnim, stackPutAsset.Data);
            if (su > 0f) stackPut.duration = su;
            EditorUtility.SetDirty(stackPutAsset);

            var stackClip3 = stackerTrack.CreateClip<StackerClip>();
            stackClip3.displayName = "CellC→CellA";
            stackClip3.start = stackPut.end;
            var stackAsset3 = (StackerClip)stackClip3.asset;
            ConfigureStackerClip(stackAsset3, default, cellA, hasStart: false);
            float sd3 = StackerSampler.EstimateDuration(stackerAnim, stackAsset3.Data, posC, posA);
            if (sd3 > 0f) stackClip3.duration = sd3;
            EditorUtility.SetDirty(stackAsset3);

            // RobotArm 巡航：覆盖近/远、高/低、左右大转角、连续段无跳变
            // A(中前)→B(低右)→C(中左)→D(高近)→E(远前)→F(近收)→G(远左后)→H(低前)→A
            Transform[] armPath =
            {
                armTargetA, armTargetB, armTargetC, armTargetD,
                armTargetE, armTargetF, armTargetG, armTargetH, armTargetA
            };
            string[] armClipNames =
            {
                "Arm A→B mid→lowR",
                "Arm B→C lowR→midL",
                "Arm C→D midL→high",
                "Arm D→E high→far",
                "Arm E→F far→near",
                "Arm F→G near→farLB",
                "Arm G→H farLB→lowF",
                "Arm H→A lowF→mid"
            };
            AppendRobotArmPathClips(director, robotArmTrack, robotArm, armPath, armClipNames);

            // RobotArm5：点位闭环巡航（Timeline 驱动，不再用 RigDemoDriver）
            var arm5Path = new Transform[arm5Waypoints.Length + 1];
            for (int i = 0; i < arm5Waypoints.Length; i++)
                arm5Path[i] = arm5Waypoints[i];
            arm5Path[arm5Path.Length - 1] = arm5Waypoints[0];
            AppendRobotArm5PathClips(director, robotArm5Track, robotArm5, arm5Path);

            Vector3 scissorHomePos = scissorAnim.transform.position;
            Quaternion scissorHomeRot = scissorAnim.transform.rotation;

            var scissorPick = scissorTrack.CreateClip<ForkliftClip>();
            scissorPick.displayName = "Scissor PickUp";
            scissorPick.start = 0;
            var scissorPickAsset = (ForkliftClip)scissorPick.asset;
            scissorPickAsset.Data.Mode = ForkliftMode.PickUp;
            scissorPickAsset.Data.AutoSyncDuration = true;
            scissorPickAsset.Data.ForkStartTravelHeight = scissorRestHeight;
            scissorPickAsset.Data.ForkEndTravelHeight = scissorRestHeight;
            scissorPickAsset.Data.ForkPlaceHeight = scissorRestHeight;
            scissorPickAsset.Data.ForkLiftHeight = scissorLiftHeight;
            scissorPickAsset.Station = BindExposed(director, EnsurePoint(scissorStation));
            float scissorPickDur = ForkliftSampler.EstimateDuration(
                scissorAnim, scissorPickAsset.Data, scissorStation, scissorHomePos, scissorHomeRot,
                ForkliftRotateMode.Timed);
            if (scissorPickDur > 0f) scissorPick.duration = scissorPickDur;
            EditorUtility.SetDirty(scissorPickAsset);

            Vector3 scissorToStation = scissorStation.position - scissorHomePos;
            Quaternion scissorPickExitRot = scissorAnim.LookRotation(scissorToStation, scissorHomeRot);

            var scissorPut = scissorTrack.CreateClip<ForkliftClip>();
            scissorPut.displayName = "Scissor PutDown";
            scissorPut.start = scissorPick.end;
            var scissorPutAsset = (ForkliftClip)scissorPut.asset;
            scissorPutAsset.Data.Mode = ForkliftMode.PutDown;
            scissorPutAsset.Data.AutoSyncDuration = true;
            scissorPutAsset.Data.ForkStartTravelHeight = scissorRestHeight;
            scissorPutAsset.Data.ForkEndTravelHeight = scissorRestHeight;
            scissorPutAsset.Data.ForkPlaceHeight = scissorRestHeight;
            scissorPutAsset.Data.ForkLiftHeight = scissorLiftHeight;
            scissorPutAsset.Station = BindExposed(director, EnsurePoint(scissorStation));
            float scissorPutDur = ForkliftSampler.EstimateDuration(
                scissorAnim, scissorPutAsset.Data, scissorStation, scissorHomePos, scissorPickExitRot,
                ForkliftRotateMode.Skip);
            if (scissorPutDur > 0f) scissorPut.duration = scissorPutDur;
            EditorUtility.SetDirty(scissorPutAsset);

            var filmClip = filmWrapTrack.CreateClip<FilmWrapClip>();
            filmClip.displayName = "缠膜 0→1";
            filmClip.start = 0;
            filmClip.duration = 6.0;
            var filmAsset = (FilmWrapClip)filmClip.asset;
            filmAsset.Data.FromProgress = 0f;
            filmAsset.Data.ToProgress = 1f;
            filmAsset.Data.DurationSeconds = 6f;
            filmAsset.Data.Ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            EditorUtility.SetDirty(filmAsset);

            // OpenBox：压扁 → 成型侧壁 → 合底 → 合顶
            double openBoxT = 0;
            var openWall = openBoxTrack.CreateClip<OpenBoxClip>();
            openWall.displayName = "侧壁成型";
            openWall.start = openBoxT;
            openWall.duration = 2.0;
            var openWallAsset = (OpenBoxClip)openWall.asset;
            openWallAsset.Data.FromCurrent = false;
            openWallAsset.Data.AnimateWall = true;
            openWallAsset.Data.FromWall = 0f;
            openWallAsset.Data.Wall = 1f;
            openWallAsset.Data.AnimateBottomShort = false;
            openWallAsset.Data.AnimateBottomLong = false;
            openWallAsset.Data.AnimateTopShort = false;
            openWallAsset.Data.AnimateTopLong = false;
            openWallAsset.Data.DurationSeconds = 2f;
            EditorUtility.SetDirty(openWallAsset);
            openBoxT = openWall.end;

            var openBottom = openBoxTrack.CreateClip<OpenBoxClip>();
            openBottom.displayName = "合底盖";
            openBottom.start = openBoxT;
            openBottom.duration = 2.0;
            var openBottomAsset = (OpenBoxClip)openBottom.asset;
            openBottomAsset.Data.FromCurrent = true;
            openBottomAsset.Data.AnimateWall = false;
            openBottomAsset.Data.AnimateBottomShort = true;
            openBottomAsset.Data.BottomShort = 1f;
            openBottomAsset.Data.AnimateBottomLong = true;
            openBottomAsset.Data.BottomLong = 1f;
            openBottomAsset.Data.AnimateTopShort = false;
            openBottomAsset.Data.AnimateTopLong = false;
            openBottomAsset.Data.DurationSeconds = 2f;
            EditorUtility.SetDirty(openBottomAsset);
            openBoxT = openBottom.end;

            var openTop = openBoxTrack.CreateClip<OpenBoxClip>();
            openTop.displayName = "合顶盖";
            openTop.start = openBoxT;
            openTop.duration = 2.0;
            var openTopAsset = (OpenBoxClip)openTop.asset;
            openTopAsset.Data.FromCurrent = true;
            openTopAsset.Data.AnimateWall = false;
            openTopAsset.Data.AnimateBottomShort = false;
            openTopAsset.Data.AnimateBottomLong = false;
            openTopAsset.Data.AnimateTopShort = true;
            openTopAsset.Data.TopShort = 1f;
            openTopAsset.Data.AnimateTopLong = true;
            openTopAsset.Data.TopLong = 1f;
            openTopAsset.Data.DurationSeconds = 2f;
            EditorUtility.SetDirty(openTopAsset);

            // 潜伏车：原地旋转 → 直线移动 → 路径移动 → 贝塞尔拐弯 → 三点转向
            // → 倒车掉头 → 平台升降 → 瞬移回 N1
            double latentT = 0;

            var latentRotate = latentTrack.CreateClip<RotateClip>();
            latentRotate.displayName = "Rotate 90°";
            latentRotate.start = latentT;
            var latentRotateAsset = (RotateClip)latentRotate.asset;
            latentRotateAsset.Data.StartYawDegrees = 90f;
            latentRotateAsset.Data.AngleDegrees = 90f;
            latentRotateAsset.Data.Direction = RotateDirection.Clockwise;
            latentRotateAsset.Data.AutoSyncDuration = true;
            float latentRotateDur = RotateSampler.EstimateDuration(latentRotateAsset.Data, latentAgv);
            if (latentRotateDur > 0f) latentRotate.duration = latentRotateDur;
            EditorUtility.SetDirty(latentRotateAsset);
            latentT = latentRotate.end;

            var latentDirect = latentTrack.CreateClip<DirectMoveClip>();
            latentDirect.displayName = "DirectMove A→B";
            latentDirect.start = latentT;
            var latentDirectAsset = (DirectMoveClip)latentDirect.asset;
            latentDirectAsset.Data.AutoSyncDuration = true;
            latentDirectAsset.StartNode = BindExposed(director, EnsurePoint(directPointA.transform));
            latentDirectAsset.EndNode = BindExposed(director, EnsurePoint(directPointB.transform));
            float latentDirectDur = DirectMoveSampler.EstimateDuration(
                latentDirectAsset.Data, latentAgv,
                directPointA.transform.position, directPointB.transform.position);
            if (latentDirectDur > 0f) latentDirect.duration = latentDirectDur;
            EditorUtility.SetDirty(latentDirectAsset);
            latentT = latentDirect.end;

            var latentPath1 = latentTrack.CreateClip<PathMoveClip>();
            latentPath1.displayName = "Move N1→N2";
            latentPath1.start = latentT;
            var latentPathAsset1 = (PathMoveClip)latentPath1.asset;
            BindPathClip(director, latentPathAsset1, network, n1, n2);
            float latentD1 = EstimatePath(network, n1, n2, latentPathAsset1.Data, latentAgv);
            if (latentD1 > 0f) latentPath1.duration = latentD1;
            EditorUtility.SetDirty(latentPathAsset1);
            latentT = latentPath1.end;

            var latentBezier = latentTrack.CreateClip<BezierCornerClip>();
            latentBezier.displayName = "Bezier N1→N2→N3";
            latentBezier.start = latentT;
            var latentBezierAsset = (BezierCornerClip)latentBezier.asset;
            latentBezierAsset.Data.AutoSyncDuration = true;
            latentBezierAsset.PrevNode = BindExposed(director, n1);
            latentBezierAsset.CornerNode = BindExposed(director, n2);
            latentBezierAsset.NextNode = BindExposed(director, n3);
            ResolvePathEnd(network, n1, n2, latentPathAsset1.Data,
                out Vector3 bezierHomePos, out Quaternion bezierHomeRot, latentAgv);
            float latentBezierDur = BezierCornerSampler.EstimateDuration(
                latentBezierAsset.Data, latentAgv, n2, n1, n3, bezierHomePos, bezierHomeRot);
            if (latentBezierDur > 0f) latentBezier.duration = latentBezierDur;
            else latentBezier.duration = 2.0;
            EditorUtility.SetDirty(latentBezierAsset);
            latentT = latentBezier.end;

            var latent3pt = latentTrack.CreateClip<ThreePointTurnClip>();
            latent3pt.displayName = "ThreePointTurn Right";
            latent3pt.start = latentT;
            var latent3ptAsset = (ThreePointTurnClip)latent3pt.asset;
            latent3ptAsset.Data.Direction = ThreePointTurnDirection.Right;
            latent3ptAsset.Data.AutoSyncDuration = true;
            float latent3ptDur = ThreePointTurnSampler.EstimateDuration(latent3ptAsset.Data, latentAgv);
            if (latent3ptDur > 0f) latent3pt.duration = latent3ptDur;
            else latent3pt.duration = 3.0;
            EditorUtility.SetDirty(latent3ptAsset);
            latentT = latent3pt.end;

            var latentUTurn = latentTrack.CreateClip<ReverseUTurnClip>();
            latentUTurn.displayName = "ReverseUTurn N3→N2→N1";
            latentUTurn.start = latentT;
            var latentUTurnAsset = (ReverseUTurnClip)latentUTurn.asset;
            latentUTurnAsset.Data.AutoSyncDuration = true;
            latentUTurnAsset.PrevNode = BindExposed(director, n3);
            latentUTurnAsset.CornerNode = BindExposed(director, n2);
            latentUTurnAsset.NextNode = BindExposed(director, n1);
            latentUTurn.duration = 3.5;
            EditorUtility.SetDirty(latentUTurnAsset);
            latentT = latentUTurn.end;

            var latentPick = latentTrack.CreateClip<LatentAgvClip>();
            latentPick.displayName = "Platform PickUp";
            latentPick.start = latentT;
            var latentPickAsset = (LatentAgvClip)latentPick.asset;
            latentAgv.ApplyClipDefaults(latentPickAsset.Data);
            latentPickAsset.Data.Mode = ForkliftMode.PickUp;
            latentPickAsset.Data.AutoSyncDuration = true;
            float latentPickDur = LatentAgvSampler.EstimateDuration(latentAgv, latentPickAsset.Data);
            if (latentPickDur > 0f) latentPick.duration = latentPickDur;
            EditorUtility.SetDirty(latentPickAsset);
            latentT = latentPick.end;

            var latentPut = latentTrack.CreateClip<LatentAgvClip>();
            latentPut.displayName = "Platform PutDown";
            latentPut.start = latentT;
            var latentPutAsset = (LatentAgvClip)latentPut.asset;
            latentAgv.ApplyClipDefaults(latentPutAsset.Data);
            latentPutAsset.Data.Mode = ForkliftMode.PutDown;
            latentPutAsset.Data.AutoSyncDuration = true;
            float latentPutDur = LatentAgvSampler.EstimateDuration(latentAgv, latentPutAsset.Data);
            if (latentPutDur > 0f) latentPut.duration = latentPutDur;
            EditorUtility.SetDirty(latentPutAsset);
            latentT = latentPut.end;

            var latentTeleport = latentTrack.CreateClip<TeleportClip>();
            latentTeleport.displayName = "Teleport→N1";
            latentTeleport.start = latentT;
            var latentTeleportAsset = (TeleportClip)latentTeleport.asset;
            BindTeleportClip(director, latentTeleportAsset, n1, TeleportFacingMode.CustomYaw, 90f);
            latentTeleport.duration = TeleportSampler.EstimateDuration(timeline);
            EditorUtility.SetDirty(latentTeleportAsset);

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0.5f, 8.5f, -14.5f);
                cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath);

            var selectGo = GameObject.Find("ScriptAnimationDirector")
                           ?? GameObject.Find("DemoRobot_5Axis")
                           ?? GameObject.Find("DemoFilmWrap/Film");
            if (selectGo != null)
                Selection.activeGameObject = selectGo;

            Debug.Log(
                $"[ScriptAnimation] 统一 Demo 场景已生成：\n" +
                $"  Scene: {ScenePath}\n" +
                $"  Timeline: {TimelinePath}\n" +
                "Play：Forklift 路网取放货；Stacker 货位+伸叉；RobotArm/RobotArm5 Timeline 巡航；" +
                "ScissorLift 剪叉；FilmWrap 缠膜；OpenBox 开箱；LatentAgv 机动+平台升降。");
        }

        /// <summary>
        /// 用 CartonRigConfig 生成占位纸箱，挂 FoldableCarton 供 OpenBoxClip 驱动。
        /// </summary>
        static OpenBoxAnim CreateDemoOpenBox(Vector3 worldPos)
        {
            var root = new GameObject("DemoOpenBox");
            root.transform.position = worldPos;

            var carton = root.AddComponent<FoldableCarton>();
            carton.SetMasterFold(0f);

            var rig = root.AddComponent<CartonRigConfig>();
            SetField(rig, "m_carton", (OpenBoxAnim)carton);
            SetField(rig, "m_width", 0.7f);
            SetField(rig, "m_depth", 0.5f);
            SetField(rig, "m_height", 0.55f);
            SetField(rig, "m_thickness", 0.018f);
            Material cartonMat = LoadDefaultCartonMaterial();
            if (cartonMat != null)
                SetField(rig, "m_panelMaterial", cartonMat);

            rig.GenerateCartonTree();
            carton.CaptureDefaultPoseFromCurrent();
            return carton;
        }

        /// <summary>
        /// 五轴 Cube Rig：返回 <see cref="RobotArm5Anim"/> 与路径点（由 Timeline 驱动）。
        /// </summary>
        private static RobotArm5Anim CreateDemoRobotArm5(
            string name,
            Vector3 worldPos,
            float[] lengths,
            Vector3[] rotAxes,
            Color palette,
            Vector3[] waypoints,
            out Transform[] wpTransforms)
        {
            const int axisCount = 5;
            var root = new GameObject(name);
            root.transform.position = worldPos;

            var basePlate = CreateBox("Base", Vector3.zero, new Vector3(0.55f, 0.08f, 0.55f), palette * 0.55f);
            basePlate.transform.SetParent(root.transform, false);
            basePlate.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            Object.DestroyImmediate(basePlate.GetComponent<Collider>());

            var axisPoints = new Transform[axisCount];
            Transform parentAxis = root.transform;
            for (int i = 0; i < axisCount; i++)
            {
                var axisGo = new GameObject($"J{i + 1}_AxisPoint");
                axisGo.transform.SetParent(parentAxis, false);
                axisGo.transform.localPosition = i == 0
                    ? Vector3.zero
                    : new Vector3(0f, lengths[i - 1], 0f);
                axisPoints[i] = axisGo.transform;

                float len = lengths[i];
                float thickness = i < 3 ? 0.16f : 0.12f;
                Color linkColor = Color.Lerp(palette, Color.white, i * 0.06f);

                var link = CreateBox($"Link{i + 1}", Vector3.zero,
                    new Vector3(thickness, Mathf.Max(0.06f, len), thickness), linkColor);
                link.transform.SetParent(axisGo.transform, false);
                link.transform.localPosition = new Vector3(0f, len * 0.5f, 0f);
                Object.DestroyImmediate(link.GetComponent<Collider>());

                var pivotBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pivotBall.name = "PivotVisual";
                pivotBall.transform.SetParent(axisGo.transform, false);
                pivotBall.transform.localScale = Vector3.one * (thickness + 0.05f);
                Object.DestroyImmediate(pivotBall.GetComponent<Collider>());
                pivotBall.GetComponent<Renderer>().sharedMaterial =
                    CreateColorMaterial(palette * 0.75f, $"SA_Demo_{name}_Pivot{i + 1}");

                parentAxis = axisGo.transform;
            }

            var tipGo = new GameObject("Tip_AxisPoint");
            tipGo.transform.SetParent(axisPoints[axisCount - 1], false);
            tipGo.transform.localPosition = new Vector3(0f, lengths[axisCount - 1], 0f);

            var tipMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipMarker.name = "TipVisual";
            tipMarker.transform.SetParent(tipGo.transform, false);
            tipMarker.transform.localScale = Vector3.one * 0.09f;
            Object.DestroyImmediate(tipMarker.GetComponent<Collider>());
            tipMarker.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(Color.yellow, $"SA_Demo_{name}_Tip");

            var ikTargetGo = new GameObject("IkTarget");
            ikTargetGo.transform.SetParent(root.transform, false);
            ikTargetGo.transform.position = worldPos + waypoints[0];
            ikTargetGo.AddComponent<ScriptAnimPoint>();
            var ikMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ikMarker.name = "TargetVisual";
            ikMarker.transform.SetParent(ikTargetGo.transform, false);
            ikMarker.transform.localScale = Vector3.one * 0.11f;
            Object.DestroyImmediate(ikMarker.GetComponent<Collider>());
            ikMarker.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(new Color(1f, 0.4f, 0.15f), $"SA_Demo_{name}_IkTarget");

            var waypointsRoot = new GameObject("Waypoints");
            waypointsRoot.transform.SetParent(root.transform, false);
            wpTransforms = new Transform[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
            {
                var wp = new GameObject($"WP{i}");
                wp.transform.SetParent(waypointsRoot.transform, false);
                wp.transform.position = worldPos + waypoints[i];
                wp.AddComponent<ScriptAnimPoint>();
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Marker";
                marker.transform.SetParent(wp.transform, false);
                marker.transform.localScale = Vector3.one * 0.07f;
                Object.DestroyImmediate(marker.GetComponent<Collider>());
                Color c = Color.Lerp(palette, new Color(1f, 0.85f, 0.3f), i * 0.12f);
                marker.GetComponent<Renderer>().sharedMaterial =
                    CreateColorMaterial(c, $"SA_Demo_{name}_WP{i}");
                wpTransforms[i] = wp.transform;
            }

            var anim = root.AddComponent<RobotArm5Anim>();
            var so = new SerializedObject(anim);
            so.FindProperty("m_toolTip").objectReferenceValue = tipGo.transform;
            so.FindProperty("m_toolOffsetLocal").vector3Value = Vector3.zero;
            so.FindProperty("m_ikTarget").objectReferenceValue = ikTargetGo.transform;
            so.FindProperty("m_followIkTargetInPlayMode").boolValue = false;
            so.FindProperty("m_keepTipDown").boolValue = true;
            so.FindProperty("m_downWorldAxis").vector3Value = Vector3.down;
            so.FindProperty("m_autoMeasureLinks").boolValue = true;
            var jointsProp = so.FindProperty("m_joints");
            jointsProp.arraySize = axisPoints.Length;
            for (int i = 0; i < axisPoints.Length; i++)
            {
                var elem = jointsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("AxisPoint").objectReferenceValue = axisPoints[i];
                elem.FindPropertyRelative("DriveJoint").objectReferenceValue = null;
                elem.FindPropertyRelative("AxisLocal").vector3Value =
                    i < rotAxes.Length ? rotAxes[i] : Vector3.up;
                elem.FindPropertyRelative("MinAngle").floatValue = -175f;
                elem.FindPropertyRelative("MaxAngle").floatValue = 175f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            anim.MeasureLinkLengthsFromHierarchy();
            anim.CaptureRestPoseFromCurrent();
            anim.CaptureHomeFromCurrent();

            EditorUtility.SetDirty(root);
            return anim;
        }

        /// <summary>潜伏车简易体：车体 + 举升平台。</summary>
        private static LatentAgvAnim CreateDemoLatentAgv(Vector3 worldPos, Quaternion worldRot)
        {
            var root = CreateBox("DemoLatentAgv", worldPos, new Vector3(1.1f, 0.35f, 1.4f),
                new Color(0.55f, 0.75f, 0.45f));
            root.transform.rotation = worldRot;
            // CreateBox 把中心放在 worldPos；沉到地面
            root.transform.position = new Vector3(worldPos.x, 0.18f, worldPos.z);

            var platform = CreateBox("Platform", Vector3.zero, new Vector3(0.95f, 0.06f, 1.15f),
                new Color(0.7f, 0.85f, 0.55f));
            platform.transform.SetParent(root.transform, false);
            platform.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            Object.DestroyImmediate(platform.GetComponent<Collider>());

            var anim = root.AddComponent<LatentAgvAnim>();
            SetField(anim, "m_platform", platform.transform);
            SetField(anim, "m_liftAxisLocal", Vector3.up);
            SetField(anim, "m_platformSpeed", 0.7f);
            SetField(anim, "m_moveSpeed", 2.2f);
            SetField(anim, "m_rotateSpeed", 100f);
            SetField(anim, "m_moveMode", PathMoveMode.RotateThenMove);
            SetField(anim, "m_defaultPlatformStartTravelHeight", 0.0f);
            SetField(anim, "m_defaultPlatformEndTravelHeight", 0.0f);
            SetField(anim, "m_defaultPlatformPlaceHeight", 0.0f);
            SetField(anim, "m_defaultPlatformLiftHeight", 0.45f);
            anim.CaptureHomeFromCurrent();
            return anim;
        }

        /// <summary>
        /// 货物立方体 + 圆柱壳膜模型，挂 FilmWrapAnim 做螺旋上升显露。
        /// </summary>
        static FilmWrapAnim CreateDemoFilmWrap(Vector3 worldPos)
        {
            const float cargoSizeXz = 0.78f;
            const float cargoHeight = 1.12f;
            const float filmRadius = 0.52f;
            const float filmHeight = 1.18f;

            var root = new GameObject("DemoFilmWrap");
            root.transform.position = worldPos;

            var cargo = CreateBox("Cargo", Vector3.zero, new Vector3(cargoSizeXz, cargoHeight, cargoSizeXz),
                new Color(0.72f, 0.52f, 0.28f));
            cargo.transform.SetParent(root.transform, false);
            cargo.transform.localPosition = new Vector3(0f, cargoHeight * 0.5f, 0f);
            Object.DestroyImmediate(cargo.GetComponent<Collider>());

            Mesh filmMesh = CreateCylinderShellMesh(filmRadius, filmHeight, 48, 16);
            string meshPath = $"{MaterialsDir}/SA_Demo_FilmShell.asset";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh != null)
                AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(filmMesh, meshPath);

            var filmGo = new GameObject("Film", typeof(MeshFilter), typeof(MeshRenderer));
            filmGo.transform.SetParent(root.transform, false);
            filmGo.transform.localPosition = new Vector3(0f, cargoHeight * 0.5f, 0f);
            filmGo.GetComponent<MeshFilter>().sharedMesh =
                AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            filmGo.GetComponent<MeshRenderer>().sharedMaterial =
                CreateColorMaterial(new Color(0.82f, 0.94f, 1f, 0.72f), "SA_Demo_Film");

            var anim = filmGo.AddComponent<FilmWrapAnim>();
            SetField(anim, "m_includeSelf", true);
            SetField(anim, "m_includeInactiveChildren", false);
            SetField(anim, "m_clockwise", true);
            SetField(anim, "m_riseUp", true);
            SetField(anim, "m_turns", 8f);
            SetField(anim, "m_pitch", 0.15f);
            SetField(anim, "m_feather", 0.06f);
            SetField(anim, "m_autoBounds", true);
            SetField(anim, "m_currentProgress", 0f);
            SetField(anim, "m_previewInEditMode", true);

            Shader wrapShader = Shader.Find(FilmWrapAnim.WrapShaderName);
            if (wrapShader != null)
                SetField(anim, "m_shaderOverride", wrapShader);

            anim.RebuildTargets();
            anim.SetProgress(0f);
            return anim;
        }

        static Mesh CreateCylinderShellMesh(float radius, float height, int segments, int rings)
        {
            var mesh = new Mesh { name = "FilmShell" };
            int vertsX = segments + 1;
            int vertsY = rings + 1;
            var verts = new Vector3[vertsX * vertsY];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];

            for (int y = 0; y <= rings; y++)
            {
                float v = y / (float)rings;
                float py = (v - 0.5f) * height;
                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments;
                    float ang = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    int i = y * vertsX + x;
                    verts[i] = n * radius + Vector3.up * py;
                    norms[i] = n;
                    uvs[i] = new Vector2(u * 4f, v * 8f);
                }
            }

            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = y * vertsX + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsX;
                    int i3 = i2 + 1;
                    tris[t++] = i0;
                    tris[t++] = i2;
                    tris[t++] = i1;
                    tris[t++] = i1;
                    tris[t++] = i2;
                    tris[t++] = i3;
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 用 Cube 拼一辆两级剪叉举升小车：平台走 ForkliftAnim，剪叉走 ScissorLiftSync。
    /// </summary>
        private static ForkliftAnim CreateDemoScissorLift(
            Vector3 worldPos,
            out Transform station,
            out float restHeight,
            out float liftHeight)
        {
            const int stageCount = 2;
            const float armLength = 0.72f;
            const float restAngleDeg = 18f;
            const float liftAngleDeg = 58f;
            const float thickness = 0.05f;
            const float track = 0.36f;
            const float hingeY = 0.22f;

            float hRest = armLength * Mathf.Sin(restAngleDeg * Mathf.Deg2Rad);
            float hLift = armLength * Mathf.Sin(liftAngleDeg * Mathf.Deg2Rad);
            float widthRest = armLength * Mathf.Cos(restAngleDeg * Mathf.Deg2Rad);
            float halfSpan = widthRest * 0.5f;
            restHeight = hingeY + stageCount * hRest;
            liftHeight = hingeY + stageCount * hLift;

            var root = new GameObject("DemoScissorLift");
            root.transform.position = worldPos;

            var chassis = CreateBox("Chassis", Vector3.zero, new Vector3(1.15f, 0.18f, 0.85f),
                new Color(0.28f, 0.3f, 0.33f));
            chassis.transform.SetParent(root.transform, false);
            chassis.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            Object.DestroyImmediate(chassis.GetComponent<Collider>());

            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? 0.32f : -0.32f;
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = i == 0 ? "WheelF" : "WheelB";
                wheel.transform.SetParent(root.transform, false);
                wheel.transform.localPosition = new Vector3(0f, 0.08f, z);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                wheel.transform.localScale = new Vector3(0.16f, 0.52f, 0.16f);
                Object.DestroyImmediate(wheel.GetComponent<Collider>());
                wheel.GetComponent<Renderer>().sharedMaterial =
                    CreateColorMaterial(new Color(0.12f, 0.12f, 0.13f), "SA_Demo_ScissorWheel");
            }

            var bottomHinge = new GameObject("BottomHinge");
            bottomHinge.transform.SetParent(root.transform, false);
            bottomHinge.transform.localPosition = new Vector3(0f, hingeY, 0f);

            var platform = CreateBox("Platform", Vector3.zero, new Vector3(1.05f, 0.07f, 0.78f),
                new Color(0.78f, 0.8f, 0.84f));
            platform.transform.SetParent(root.transform, false);
            platform.transform.localPosition = new Vector3(0f, restHeight, 0f);

            var topHinge = new GameObject("TopHinge");
            topHinge.transform.SetParent(platform.transform, false);
            topHinge.transform.localPosition = Vector3.zero;

            var scissorRoot = new GameObject("Scissor");
            scissorRoot.transform.SetParent(root.transform, false);

            var leftPerStage = new Transform[stageCount][];
            var rightPerStage = new Transform[stageCount][];
            Color leftColor = new Color(0.95f, 0.45f, 0.22f);
            Color rightColor = new Color(0.2f, 0.72f, 0.82f);

            Vector3 lift = Vector3.up;
            Vector3 fold = Vector3.forward;
            Vector3 lateralAxis = Vector3.right;
            Vector3 b = bottomHinge.transform.position;

            for (int stage = 0; stage < stageCount; stage++)
            {
                var stageGo = new GameObject($"Stage{stage}");
                stageGo.transform.SetParent(scissorRoot.transform, false);

                Vector3 centerB = b + lift * (stage * hRest);
                Vector3 centerT = b + lift * ((stage + 1) * hRest);
                Vector3 bl = centerB - fold * halfSpan;
                Vector3 br = centerB + fold * halfSpan;
                Vector3 tl = centerT - fold * halfSpan;
                Vector3 tr = centerT + fold * halfSpan;

                leftPerStage[stage] = new Transform[2];
                rightPerStage[stage] = new Transform[2];
                for (int side = 0; side < 2; side++)
                {
                    float lateral = (side == 0 ? -1f : 1f) * track;
                    string sideName = side == 0 ? "Near" : "Far";

                    Transform left = CreateScissorArm(
                        stageGo.transform, $"Left_{sideName}", armLength, thickness, leftColor,
                        "SA_Demo_ScissorLeft");
                    AimScissorArm(left, bl + lateralAxis * (lateral - 0.03f), tr + lateralAxis * (lateral - 0.03f));
                    leftPerStage[stage][side] = left;

                    Transform right = CreateScissorArm(
                        stageGo.transform, $"Right_{sideName}", armLength, thickness, rightColor,
                        "SA_Demo_ScissorRight");
                    AimScissorArm(right, br + lateralAxis * (lateral + 0.03f), tl + lateralAxis * (lateral + 0.03f));
                    rightPerStage[stage][side] = right;
                }
            }

            var anim = root.AddComponent<ForkliftAnim>();
            SetField(anim, "m_fork", platform.transform);
            SetField(anim, "m_liftAxisLocal", Vector3.up);
            SetField(anim, "m_moveSpeed", 1.6f);
            SetField(anim, "m_forkSpeed", 0.45f);
            SetField(anim, "m_rotateSpeed", 90f);
            SetField(anim, "m_approachDistance", 1.05f);
            SetField(anim, "m_moveMode", PathMoveMode.RotateThenMove);

            var sync = root.AddComponent<ScissorLiftSync>();
            BindScissorSync(
                sync,
                bottomHinge.transform,
                topHinge.transform,
                platform.transform,
                stageCount,
                leftPerStage,
                rightPerStage,
                armLength);

            var stationGo = new GameObject("ScissorStation");
            stationGo.transform.position = worldPos + new Vector3(0f, 0f, 2.15f);
            stationGo.AddComponent<ScriptAnimPoint>();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "StationMarker";
            marker.transform.SetParent(stationGo.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            marker.transform.localScale = new Vector3(0.45f, 0.05f, 0.45f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(new Color(0.95f, 0.55f, 0.25f), "SA_Demo_ScissorStation");
            station = stationGo.transform;
            anim.CaptureHomeFromCurrent();
            return anim;
        }

        private static Transform CreateScissorArm(
            Transform parent, string name, float length, float thickness, Color color, string matName)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar";
            bar.transform.SetParent(pivot.transform, false);
            bar.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
            bar.transform.localScale = new Vector3(thickness, thickness, length);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.GetComponent<Renderer>().sharedMaterial = CreateColorMaterial(color, matName);

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Hinge";
            ball.transform.SetParent(pivot.transform, false);
            ball.transform.localScale = Vector3.one * (thickness + 0.02f);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(color * 0.75f, matName + "_Hinge");
            return pivot.transform;
        }

        private static void AimScissorArm(Transform arm, Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-10f)
                return;
            arm.position = from;
            arm.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static void BindScissorSync(
            ScissorLiftSync sync,
            Transform bottom,
            Transform top,
            Transform platform,
            int stageCount,
            Transform[][] leftPerStage,
            Transform[][] rightPerStage,
            float armLength)
        {
            var so = new SerializedObject(sync);
            so.FindProperty("m_bottomHinge").objectReferenceValue = bottom;
            so.FindProperty("m_topHinge").objectReferenceValue = top;
            so.FindProperty("m_platform").objectReferenceValue = platform;
            so.FindProperty("m_stageCount").intValue = stageCount;
            so.FindProperty("m_armLength").floatValue = armLength;
            so.FindProperty("m_foldSpan").floatValue = 0f;
            so.FindProperty("m_shrinkSpanWithLift").boolValue = false;
            so.FindProperty("m_liftAxisLocal").vector3Value = Vector3.up;
            so.FindProperty("m_foldAxisLocal").vector3Value = Vector3.forward;
            so.FindProperty("m_rotationAxisLocal").vector3Value = Vector3.right;
            so.FindProperty("m_pivotAlongArm").floatValue = 0f;
            so.FindProperty("m_driveInEditMode").boolValue = true;

            var stages = so.FindProperty("m_stages");
            stages.arraySize = stageCount;
            for (int i = 0; i < stageCount; i++)
            {
                SerializedProperty stage = stages.GetArrayElementAtIndex(i);
                Transform[] left = leftPerStage[i];
                Transform[] right = rightPerStage[i];
                stage.FindPropertyRelative("LeftNear").objectReferenceValue = left[0];
                stage.FindPropertyRelative("LeftFar").objectReferenceValue = left[1];
                stage.FindPropertyRelative("RightNear").objectReferenceValue = right[0];
                stage.FindPropertyRelative("RightFar").objectReferenceValue = right[1];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            sync.CaptureRestPose();
        }

        private static Transform CreateArmTarget(Transform parent, string name, Vector3 worldPos, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.AddComponent<ScriptAnimPoint>();
            CreateTargetMarker(go.transform, color);
            return go.transform;
        }

        /// <summary>
        /// 按点位序列生成单个 RobotArmClip（点位链表依次移动）。
    /// </summary>
        private static void AppendRobotArmPathClips(
            PlayableDirector director,
            ScriptDedicatedTrack track,
            RobotArmAnim robotArm,
            Transform[] path,
            string[] clipNames)
        {
            if (path == null || path.Length < 2)
                return;

            string displayName = clipNames != null && clipNames.Length > 0
                ? $"Arm path ({path.Length} pts)"
                : $"Arm path ({path.Length} pts)";

            var timelineClip = track.CreateClip<RobotArmClip>();
            timelineClip.displayName = displayName;
            timelineClip.start = 0f;
            var asset = (RobotArmClip)timelineClip.asset;
            ConfigureRobotArmPathClip(director, asset, path);

            float duration = EstimateRobotArmPath(robotArm, asset, path);
            if (duration > 0f)
                timelineClip.duration = duration;
            EditorUtility.SetDirty(asset);
        }

        private static void AppendRobotArm5PathClips(
            PlayableDirector director,
            ScriptDedicatedTrack track,
            RobotArm5Anim robotArm,
            Transform[] path)
        {
            if (path == null || path.Length < 2 || robotArm == null)
                return;

            var timelineClip = track.CreateClip<RobotArm5Clip>();
            timelineClip.displayName = $"Arm5 path ({path.Length} pts)";
            timelineClip.start = 0f;
            var asset = (RobotArm5Clip)timelineClip.asset;
            ConfigureRobotArm5PathClip(director, asset, path);

            float duration = EstimateRobotArm5Path(robotArm, asset, path);
            if (duration > 0f)
                timelineClip.duration = duration;
            EditorUtility.SetDirty(asset);
        }

        private static void ConfigureRobotArm5PathClip(
            PlayableDirector director,
            RobotArm5Clip clip,
            Transform[] path)
        {
            clip.Data.AutoSyncDuration = true;
            clip.Data.HasStartPoint = true;
            clip.Data.Waypoints = new System.Collections.Generic.List<RobotArm5Waypoint>(path.Length);
            clip.WaypointTargets = new System.Collections.Generic.List<RobotArm5WaypointTarget>(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                clip.Data.Waypoints.Add(new RobotArm5Waypoint());
                clip.WaypointTargets.Add(new RobotArm5WaypointTarget
                {
                    Target = BindExposed(director, EnsurePoint(path[i]))
                });
            }
        }

        private static float EstimateRobotArm5Path(
            RobotArm5Anim anim,
            RobotArm5Clip clip,
            Transform[] path)
        {
            Transform[] targets = new Transform[path.Length];
            for (int i = 0; i < path.Length; i++)
                targets[i] = path[i];

            return RobotArm5Sampler.EstimateDuration(
                anim, clip.Data, targets, RobotArm5Sampler.TimelineStartFallback.None);
        }

        private static Transform[] CreateDemoRobotArmHierarchy(Transform root)
        {
            // 串联六轴：每节一端为关节枢轴，沿 +Y 伸出连杆
            float[] lengths = { 0.35f, 0.55f, 0.45f, 0.12f, 0.12f, 0.08f };
            Color[] colors =
            {
                new Color(0.2f, 0.75f, 0.85f),
                new Color(0.25f, 0.7f, 0.9f),
                new Color(0.3f, 0.65f, 0.95f),
                new Color(0.35f, 0.8f, 0.85f),
                new Color(0.4f, 0.75f, 0.9f),
                new Color(0.45f, 0.7f, 0.95f)
            };

            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            basePlate.transform.SetParent(root, false);
            basePlate.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            basePlate.transform.localScale = new Vector3(0.5f, 0.08f, 0.5f);
            Object.DestroyImmediate(basePlate.GetComponent<Collider>());
            basePlate.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(new Color(0.18f, 0.4f, 0.48f), "SA_Demo_ArmBase");

            int jointCount = RobotArmAnim.DefaultJointCount;
            var joints = new Transform[jointCount];
            Transform parent = root;
            for (int i = 0; i < jointCount; i++)
            {
                var pivot = new GameObject($"J{i + 1}_AxisPoint");
                pivot.transform.SetParent(parent, false);
                pivot.transform.localPosition = i == 0
                    ? Vector3.zero
                    : new Vector3(0f, lengths[i - 1], 0f);
                joints[i] = pivot.transform;

                float len = lengths[i];
                float thickness = i < 3 ? 0.18f : 0.14f;
                var link = GameObject.CreatePrimitive(PrimitiveType.Cube);
                link.name = $"Link{i + 1}";
                link.transform.SetParent(pivot.transform, false);
                link.transform.localPosition = new Vector3(0f, len * 0.5f, 0f);
                link.transform.localScale = new Vector3(thickness, Mathf.Max(0.08f, len), thickness);
                Object.DestroyImmediate(link.GetComponent<Collider>());
                link.GetComponent<Renderer>().sharedMaterial =
                    CreateColorMaterial(colors[i], $"SA_Demo_ArmLink{i + 1}");

                // 枢轴小球便于辨认
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Pivot";
                ball.transform.SetParent(pivot.transform, false);
                ball.transform.localScale = Vector3.one * (thickness + 0.06f);
                Object.DestroyImmediate(ball.GetComponent<Collider>());
                ball.GetComponent<Renderer>().sharedMaterial =
                    CreateColorMaterial(colors[i] * 0.85f, $"SA_Demo_ArmPivot{i + 1}");

                parent = pivot.transform;
            }

            // 末端 tip 标记
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(joints[jointCount - 1], false);
            tip.transform.localPosition = new Vector3(0f, lengths[jointCount - 1], 0f);
            tip.transform.localScale = Vector3.one * 0.08f;
            Object.DestroyImmediate(tip.GetComponent<Collider>());
            tip.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(Color.yellow, "SA_Demo_ArmTip");

            return joints;
        }

        private static void ConfigureDemoRobotArm(RobotArmAnim arm, Transform[] joints)
        {
            float[] lengths = { 0.35f, 0.55f, 0.45f, 0.12f, 0.12f, 0.08f };
            Vector3[] axes =
            {
                Vector3.up, Vector3.forward, Vector3.forward,
                Vector3.up, Vector3.forward, Vector3.up
            };

            int jointCount = joints.Length;
            var so = new SerializedObject(arm);
            var jointsProp = so.FindProperty("m_joints");
            jointsProp.arraySize = jointCount;
            for (int i = 0; i < jointCount; i++)
            {
                var elem = jointsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("AxisPoint").objectReferenceValue = joints[i];
                elem.FindPropertyRelative("DriveJoint").objectReferenceValue = null;
                elem.FindPropertyRelative("AxisLocal").vector3Value = axes[i];
                elem.FindPropertyRelative("LinkLength").floatValue = lengths[i];
                elem.FindPropertyRelative("LinkDirectionLocal").vector3Value = Vector3.up;
                elem.FindPropertyRelative("MinAngle").floatValue = -175f;
                elem.FindPropertyRelative("MaxAngle").floatValue = 175f;
            }

            Transform tip = joints[jointCount - 1].Find("Tip");
            so.FindProperty("m_toolTip").objectReferenceValue = tip;
            so.FindProperty("m_toolOffsetLocal").vector3Value = Vector3.zero;
            so.FindProperty("m_keepTipDown").boolValue = true;
            so.FindProperty("m_jointSpeed").floatValue = 75f;
            so.FindProperty("m_jointAcceleration").floatValue = 120f;
            so.ApplyModifiedPropertiesWithoutUndo();

            arm.CaptureRestPoseFromCurrent();
            arm.CaptureHomeFromCurrent();
        }

        private static void CreateTargetMarker(Transform parent, Color color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(parent, false);
            marker.transform.localScale = Vector3.one * 0.12f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial =
                CreateColorMaterial(color, "SA_Demo_" + parent.name);
        }

        private static void ConfigureRobotArmPathClip(
            PlayableDirector director,
            RobotArmClip clip,
            Transform[] path)
        {
            clip.Data.AutoSyncDuration = true;
            clip.Data.HasStartPoint = true;
            clip.Data.Waypoints = new System.Collections.Generic.List<RobotArmWaypoint>(path.Length);
            clip.WaypointTargets = new System.Collections.Generic.List<RobotArmWaypointTarget>(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                clip.Data.Waypoints.Add(new RobotArmWaypoint());
                clip.WaypointTargets.Add(new RobotArmWaypointTarget
                {
                    Target = BindExposed(director, EnsurePoint(path[i]))
                });
            }
        }

        private static float EstimateRobotArmPath(
            RobotArmAnim anim,
            RobotArmClip clip,
            Transform[] path)
        {
            Transform[] targets = new Transform[path.Length];
            for (int i = 0; i < path.Length; i++)
                targets[i] = path[i];

            return RobotArmSampler.EstimateDuration(
                anim, clip.Data, targets, RobotArmSampler.TimelineStartFallback.None);
        }

        private static void ConfigureStackerClip(
            StackerClip clip,
            Int4 startCell,
            Int4 endCell,
            bool hasStart)
        {
            clip.Data.AutoSyncDuration = true;
            clip.Data.HasStartCell = hasStart;
            if (hasStart)
                clip.Data.StartCell = startCell;
            clip.Data.EndCell = endCell;
        }

        private static void ConfigureStackerForkClip(StackerForkClip clip, ForkliftMode mode)
        {
            clip.Data.Mode = mode;
            clip.Data.AutoSyncDuration = true;
            // 沿 PrimaryForkAxisLocal(+Z) 伸叉：Travel 收回，Place/Lift 伸出
            clip.Data.ForkTravelOffset = 0f;
            clip.Data.ForkPlaceOffset = 0.65f;
            clip.Data.ForkLiftOffset = 0.9f;
            clip.Data.SecondaryForkTravelOffset = 0f;
            clip.Data.SecondaryForkPlaceOffset = 0.65f;
            clip.Data.SecondaryForkLiftOffset = 0.9f;
        }

        private static GameObject CreateSlotMarker(Transform parent, string name, Vector3 pos, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localScale = Vector3.one * 0.35f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = CreateColorMaterial(color, "SA_Demo_" + name);
            return go;
        }

        private static void BindPathClip(
            PlayableDirector director,
            PathMoveClip clip,
            PathNetwork network,
            PathNode start,
            PathNode end)
        {
            clip.Data.AutoSyncDuration = true;
            clip.Network = BindExposed(director, network);
            clip.StartNode = BindExposed(director, start);
            clip.EndNode = BindExposed(director, end);
        }

        private static void BindTeleportClip(
            PlayableDirector director,
            TeleportClip clip,
            PathNode target,
            TeleportFacingMode facingMode,
            float customYawDegrees = 0f)
        {
            // 默认关闭 AutoSync，Demo 仅写入默认占位时长，之后可自由拖拽
            clip.Data.AutoSyncDuration = false;
            clip.Data.FacingMode = facingMode;
            clip.Data.CustomYawDegrees = customYawDegrees;
            clip.TargetNode = BindExposed(director, target);
        }

        private static ExposedReference<T> BindExposed<T>(PlayableDirector director, T value)
            where T : Object
        {
            var exposed = new ExposedReference<T>
            {
                exposedName = System.Guid.NewGuid().ToString("N"),
                // defaultValue：Inspector 无 Director 上下文时也能显示绑定，避免一片 None
                defaultValue = value
            };
            director.SetReferenceValue(exposed.exposedName, value);
            return exposed;
        }

        private static ScriptAnimPoint EnsurePoint(Transform transform)
        {
            if (transform == null)
                return null;
            var point = transform.GetComponent<ScriptAnimPoint>();
            if (point == null)
                point = transform.gameObject.AddComponent<ScriptAnimPoint>();
            return point;
        }

        private static float EstimatePath(
            PathNetwork network,
            PathNode start,
            PathNode end,
            PathMoveClipData data,
            PathMoveActor actor,
            Quaternion? incomingRotation = null)
        {
            var points = new List<Vector3>(16);
            Vector3 pathOffset = actor != null ? actor.PathOffset : default;
            if (!PathMoveSampler.TryResolveWorldPoints(network, start, end, data, points, pathOffset))
                return -1f;
            Quaternion incoming = incomingRotation
                ?? PathMoveSampler.GetPathStartRotation(
                    points, actor, data != null && data.ReverseFacing);
            return PathMoveSampler.EstimateDuration(data, actor, points, incoming);
        }

        private static void ResolvePathEnd(
            PathNetwork network,
            PathNode start,
            PathNode end,
            PathMoveClipData data,
            out Vector3 pos,
            out Quaternion rot,
            PathMoveActor actor = null)
        {
            pos = end != null ? end.transform.position : Vector3.zero;
            rot = Quaternion.identity;
            var points = new List<Vector3>(16);
            Vector3 pathOffset = actor != null ? actor.PathOffset : default;
            if (PathMoveSampler.TryResolveWorldPoints(network, start, end, data, points, pathOffset))
                PathMoveSampler.TryGetPathEndPose(
                    points, out pos, out rot, actor,
                    data != null && data.ReverseFacing, data);
        }

        private static PathNode CreateNode(Transform parent, string id, Vector3 pos)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go.AddComponent<PathNode>();
        }

        private static GameObject CreateBox(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = CreateColorMaterial(color, "SA_Demo_" + name);
            return go;
        }

        private static Material CreateColorMaterial(Color color, string assetName)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);

            string matPath = $"{MaterialsDir}/{assetName}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (existing != null)
            {
                existing.shader = shader;
                if (existing.HasProperty("_BaseColor"))
                    existing.SetColor("_BaseColor", color);
                if (existing.HasProperty("_Color"))
                    existing.SetColor("_Color", color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mat, matPath);
            return AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        static void OpenSampleScene(string relativeUnderSample)
        {
            string path = FindSampleAsset(relativeUnderSample);
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
            {
                Debug.LogError(
                    $"[ScriptAnimation] 找不到场景「{relativeUnderSample}」。" +
                    "请先在 Package Manager → NonsensicalKit.Simulation → Samples 中导入 ScriptAnimation。");
                return;
            }

            EditorSceneManager.OpenScene(path);
        }

        /// <summary>
        /// 解析 Sample 资源：优先包内 Samples~，其次已导入的 Assets/Samples/.../ScriptAnimation/。
        /// </summary>
        static string FindSampleAsset(string relativeUnderSample)
        {
            relativeUnderSample = relativeUnderSample.Replace('\\', '/').TrimStart('/');
            string pkgPath = $"Packages/{PackageId}/Samples~/{SampleFolderName}/{relativeUnderSample}";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(pkgPath) != null)
                return pkgPath;

            string suffix = $"/{SampleFolderName}/{relativeUnderSample}";
            string fileName = Path.GetFileNameWithoutExtension(relativeUnderSample);
            string ext = Path.GetExtension(relativeUnderSample);
            string filter = ext.Equals(".unity", System.StringComparison.OrdinalIgnoreCase)
                ? $"{fileName} t:Scene"
                : fileName;

            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }

        internal static Material LoadDefaultCartonMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DefaultCartonMatPath);
            if (mat != null)
                return mat;

            foreach (string guid in AssetDatabase.FindAssets("Carton t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.EndsWith("/Carton.mat", System.StringComparison.OrdinalIgnoreCase) &&
                    path.Contains("ScriptAnimation", System.StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent ?? "Assets", name);
        }

        private static void SetField(Object obj, string field, float value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(Object obj, string field, bool value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(Object obj, string field, Object value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(Object obj, string field, Vector3 value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetField(Object obj, string field, PathMoveMode value)
        {
            var so = new SerializedObject(obj);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.intValue = (int)value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
