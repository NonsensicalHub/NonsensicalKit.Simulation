using System.Collections.Generic;
using CineShot.Core;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace CineShot.Samples
{
    public sealed class CineDemoHUD : MonoBehaviour
    {
        struct Cue
        {
            public double Start;
            public double End;
            public int Priority;
            public string Name;
            public ShotAsset Shot;
            public Image Background;
        }

        static readonly Color k_CueIdle = new Color(0.16f, 0.17f, 0.19f, 0.95f);
        static readonly Color k_CueOn = new Color(0.22f, 0.38f, 0.55f, 0.98f);

        [SerializeField] CineShotPlayer m_Player;
        [SerializeField] Text m_Title;
        [SerializeField] Text m_Detail;
        [SerializeField] Text m_Status;
        [SerializeField] Font m_UiFont;
        [SerializeField] float m_InterruptBlend = 0.35f;
        readonly List<Cue> m_Cues = new List<Cue>();
        int m_ActiveCue = -2;

        void Reset()
        {
            m_Player = FindObjectOfType<CineShotPlayer>();
        }

        void Start()
        {
            ApplyUiFont();
            LayoutChrome();
            BuildCues();
        }

        Font ResolveUiFont()
        {
            // WebGL 没有系统中文回退；必须用工程内带字库的字体，不能靠 Arial
            if (m_UiFont != null)
                return m_UiFont;
            if (m_Title != null && m_Title.font != null)
                return m_Title.font;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void ApplyUiFont()
        {
            Font font = ResolveUiFont();
            if (font == null)
                return;
            if (m_Title != null)
                m_Title.font = font;
            if (m_Detail != null)
                m_Detail.font = font;
            if (m_Status != null)
                m_Status.font = font;
        }

        void LayoutChrome()
        {
            // 顶栏只占左侧，右侧整列留给点播，避免说明条压住按钮
            if (m_Title == null)
                return;
            var panel = m_Title.transform.parent as RectTransform;
            if (panel == null)
                return;
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.offsetMin = new Vector2(0f, -168f);
            panel.offsetMax = new Vector2(-340f, 0f);
        }

        void Update()
        {
            bool play = false, interrupt = false, resume = false, stop = false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                play = kb.spaceKey.wasPressedThisFrame;
                interrupt = kb.iKey.wasPressedThisFrame;
                resume = kb.rKey.wasPressedThisFrame;
                stop = kb.sKey.wasPressedThisFrame;
            }
#else
            play = Input.GetKeyDown(KeyCode.Space);
            interrupt = Input.GetKeyDown(KeyCode.I);
            resume = Input.GetKeyDown(KeyCode.R);
            stop = Input.GetKeyDown(KeyCode.S);
#endif
            if (m_Player == null)
                return;
            if (play) m_Player.Play(0.0);
            if (interrupt) m_Player.Interrupt(m_InterruptBlend);
            if (resume) m_Player.Resume();
            if (stop) m_Player.Stop(true);

            var director = m_Player.Director;
            double time = director != null ? director.time : 0.0;
            var player = m_Player.Player;
            int cue = ActiveCue(time);
            string title = "-";
            string detail = "";
            if (cue >= 0)
            {
                title = m_Cues[cue].Name;
                detail = Describe(title, m_Cues[cue].Shot);
            }
            else if (player.CurrentShot != null)
            {
                title = player.CurrentShot.displayName;
                detail = Describe(title, player.CurrentShot);
            }
            if (cue != m_ActiveCue)
            {
                if (m_ActiveCue >= 0)
                    m_Cues[m_ActiveCue].Background.color = k_CueIdle;
                if (cue >= 0)
                    m_Cues[cue].Background.color = k_CueOn;
                m_ActiveCue = cue;
            }

            if (m_Title != null)
                m_Title.text = title;
            if (m_Detail != null)
                m_Detail.text = detail;
            if (m_Status != null)
            {
                string line = "状态：" + StateLabel(player.State) + "　时间：" + time.ToString("0.00") + "s"
                              + "　求解：" + CinePerfProbe.AverageMs.ToString("0.000") + "ms"
                              + "\n快捷键：空格=播放　I=打断　R=续播　S=停止";
                if (m_Title == null)
                    line = title + "\n" + detail + "\n" + line;
                m_Status.text = line;
            }
        }

        /// <summary>与混轨同规则：后开拍的是当前镜，同刻开拍比优先级。</summary>
        int ActiveCue(double time)
        {
            int best = -1;
            for (int i = 0; i < m_Cues.Count; i++)
            {
                var c = m_Cues[i];
                if (c.Shot == null || time < c.Start || time >= c.End)
                    continue;
                if (best < 0 || ShotBlend.CompareActive(c.Start, c.Priority, m_Cues[best].Start, m_Cues[best].Priority) > 0)
                    best = i;
            }
            return best;
        }

        static string Describe(string name, ShotAsset shot)
        {
            switch (name)
            {
                case "图库_静态": return "静态。机位停在注视点旁边，只用偏航和俯仰。";
                case "图库_轨道": return "轨道。沿起点到终点平移，侧向和高度是相对轨道的，不是世界坐标。";
                case "图库_环绕": return "环绕。绑了跟随就绕目标转；没绑则绕世界圆心。起始角 0 朝 +Z。";
                case "图库_推拉": return "推拉。距离由远到近，结束时可以联动视场角。";
                case "图库_摇臂": return "摇臂。从起点抬到终点，中间走一段弧。有跟随则整段跟着走。";
                case "图库_过肩": return "过肩。从肩侧看目标。正数是右肩，负数是左肩。";
                case "图库_手持": return "手持。基座不动，镜头自己晃；晃完仍然盯着注视点。";
                case "图库_样条": return "样条。沿曲线走，只瞄注视。前馈只在目标明显移动时才起作用。";
                case "图库_摇镜": return "摇镜。机位停住，注视点沿弧线扫过。";
                case "演示·景深浅": return "景深变浅。机位不动，散景光圈 f/1.2，背景先糊住。";
                case "演示·景深深": return "景深变深。与上一镜重叠，光圈收到 f/16，背景逐渐变实。";
                case "演示·焦点近": return "焦点在近处。浅景深，清晰面落在主角上。";
                case "演示·焦点远": return "焦点拉远。对焦距离从主角拉到远处的方块，机位不变。";
                case "演示·切换浅": return "镜头间景深：这一镜是近景、浅景深。";
                case "演示·切换深": return "镜头间景深：切到侧面全景，景深随叠化一起变深。";
                case "演示·高斯景深": return "高斯景深。不用散景，清晰带用起始和结束距离表示。";
                case "演示·景深关闭": return "景深关闭。焦点模式还在，但 Volume 里的景深效果被关掉。";
                case "演示·超焦距": return "超焦距。按焦距 35mm、光圈 f/8 算出对焦约 5 米，从一半距离到无穷远都清楚。";
                case "演示·手持层": return "手持层。主机位仍是静态，上面再叠一层轻微抖动。";
                case "演示·构图": return "构图偏移。沿镜头的右、上、前把机位挪开，再看回原来的注视点。";
                case "走动·静态": return "人物在走。静态机位绑在身上，相机保持在侧后方一起走。";
                case "走动·轨道": return "人物在走。轨道留在世界里，镜头平移并一直看着他。";
                case "走动·环绕": return "人物在走。环绕中心跟着他，相机绕着走动的人转。";
                case "走动·推拉": return "人物在走。推拉距离相对他变化，人往前时相机仍贴着。";
                case "走动·摇臂": return "人物在走。摇臂的起点终点跟着他抬高、拉远。";
                case "走动·过肩": return "人物在走。过肩机位贴在肩侧，朝向跟着转身。";
                case "走动·手持": return "人物在走。手持基座跟着他，晃动之后仍然盯住。";
                case "走动·样条": return "人物在走。相机沿样条移动，视线只跟注视点。";
                case "走动·摇镜": return "人物在走。机位相对他停住，注视点从一侧扫到另一侧。";
                case "演示·缓动甲": return "缓动对比的起点。下一镜用线性曲线叠进来。";
                case "演示·缓动乙": return "线性叠化。机位匀速挪到侧面，不走默认的平滑加减速。";
                case "演示·优先级低": return "同刻叠片里优先级较低的一条。正常播放时看不到它。";
                case "演示·优先级高": return "同刻开拍时优先级高的镜头胜出。这条是侧面机位。";
                case "演示·匹配前": return "匹配帧的上一镜。结束姿态会交给下一条的匹配点。";
                case "演示·匹配后": return "匹配帧。进入的头一帧贴住上一镜结束姿态，再落到这条机位。";
                case "演示·跟随": return "跟随。环绕中心绑在主角上，人动的时候圈子一起走。";
            }
            if (shot == null)
                return "";
            switch (shot.rigKind)
            {
                case RigKind.Static: return "静态机位。";
                case RigKind.DollyTrack: return "轨道机位。";
                case RigKind.Orbit: return "环绕机位。";
                case RigKind.PushPull: return "推拉机位。";
                case RigKind.Crane: return "摇臂机位。";
                case RigKind.Shoulder: return "过肩机位。";
                case RigKind.Handheld: return "手持机位。";
                case RigKind.SplineDolly: return "样条机位。";
                case RigKind.Pan: return "摇镜机位。";
                default: return shot.displayName;
            }
        }

        static string StateLabel(CineState state)
        {
            switch (state)
            {
                case CineState.Idle: return "空闲";
                case CineState.Playing: return "播放中";
                case CineState.PausedResumable: return "已打断可续播";
                case CineState.BlendingOut: return "交权混合中";
                default: return state.ToString();
            }
        }

        public void PlayAt(double startTime)
        {
            if (m_Player != null)
                m_Player.Play(startTime);
        }

        void BuildCues()
        {
            m_Cues.Clear();
            if (m_Player == null || m_Player.Director == null)
                return;
            var timeline = m_Player.Director.playableAsset as TimelineAsset;
            if (timeline == null)
                return;

            var old = transform.Find("点播");
            if (old != null)
                Destroy(old.gameObject);

            Font font = ResolveUiFont();

            var root = new GameObject("点播", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rootRt = (RectTransform)root.transform;
            rootRt.anchorMin = new Vector2(1f, 0f);
            rootRt.anchorMax = new Vector2(1f, 1f);
            rootRt.pivot = new Vector2(1f, 0.5f);
            rootRt.offsetMin = new Vector2(-328f, 12f);
            rootRt.offsetMax = new Vector2(-12f, -12f);
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.52f);
            bg.raycastTarget = true;

            var headGo = new GameObject("栏头", typeof(RectTransform));
            headGo.transform.SetParent(root.transform, false);
            var headRt = (RectTransform)headGo.transform;
            headRt.anchorMin = new Vector2(0f, 1f);
            headRt.anchorMax = new Vector2(1f, 1f);
            headRt.pivot = new Vector2(0.5f, 1f);
            headRt.offsetMin = new Vector2(10f, -36f);
            headRt.offsetMax = new Vector2(-10f, -8f);
            var head = headGo.AddComponent<Text>();
            head.font = font;
            head.fontSize = 18;
            head.fontStyle = FontStyle.Bold;
            head.alignment = TextAnchor.MiddleLeft;
            head.color = new Color(0.9f, 0.92f, 0.95f);
            head.text = "点播";
            head.raycastTarget = false;

            var scrollGo = new GameObject("滚动", typeof(RectTransform));
            scrollGo.transform.SetParent(root.transform, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -40f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var viewport = new GameObject("视口", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewRt = (RectTransform)viewport.transform;
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = Vector2.zero;
            viewRt.offsetMax = Vector2.zero;
            var viewImg = viewport.AddComponent<Image>();
            viewImg.color = new Color(1f, 1f, 1f, 0.02f);
            viewImg.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = viewRt;

            var content = new GameObject("列表", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(2, 2, 2, 2);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRt;

            var clips = new List<TimelineClip>();
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is CineShotTrack))
                    continue;
                foreach (var c in track.GetClips())
                    clips.Add(c);
            }
            clips.Sort(delegate (TimelineClip a, TimelineClip b)
            {
                int s = a.start.CompareTo(b.start);
                if (s != 0)
                    return s;
                return string.Compare(a.displayName, b.displayName, System.StringComparison.Ordinal);
            });

            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                double start = clip.start;
                var buttonGo = new GameObject(clip.displayName, typeof(RectTransform));
                buttonGo.transform.SetParent(content.transform, false);
                var img = buttonGo.AddComponent<Image>();
                img.color = k_CueIdle;
                var button = buttonGo.AddComponent<Button>();
                button.targetGraphic = img;
                var le = buttonGo.AddComponent<LayoutElement>();
                le.minHeight = 30f;
                le.preferredHeight = 30f;
                le.flexibleWidth = 1f;

                var textGo = new GameObject("字", typeof(RectTransform));
                textGo.transform.SetParent(buttonGo.transform, false);
                var tr = (RectTransform)textGo.transform;
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(8f, 1f);
                tr.offsetMax = new Vector2(-8f, -1f);
                var label = textGo.AddComponent<Text>();
                label.font = font;
                label.fontSize = 15;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = Color.white;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.raycastTarget = false;
                label.text = clip.displayName;

                button.onClick.AddListener(delegate { PlayAt(start); });
                var cine = clip.asset as CineShotClip;
                var shot = cine != null ? cine.shot : null;
                m_Cues.Add(new Cue
                {
                    Start = clip.start,
                    End = clip.end,
                    Priority = shot != null ? cine.overrides.ResolvePriority(shot) : 0,
                    Name = string.IsNullOrEmpty(clip.displayName) && shot != null ? shot.displayName : clip.displayName,
                    Shot = shot,
                    Background = img
                });
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        }

        public void UiPlay() { if (m_Player != null) m_Player.Play(0.0); }
        public void UiInterrupt() { if (m_Player != null) m_Player.Interrupt(m_InterruptBlend); }
        public void UiResume() { if (m_Player != null) m_Player.Resume(); }
        public void UiStop() { if (m_Player != null) m_Player.Stop(true); }
    }
}
