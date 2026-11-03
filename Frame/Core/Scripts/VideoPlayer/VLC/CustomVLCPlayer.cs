using UnityEngine;
using System;
using LibVLCSharp;
using UnityEngine.UI;
using System.Collections.Generic;
using Frame.Core.VideoPlayer;

/// <summary>
/// IVideoPlayer 的 VLC 对接示例，依赖外部插件 VLCUnity / LibVLCSharp。
/// 插件安装位置与说明见同目录 README.md。
/// </summary>
/// <remarks>
/// Outputs audio to speakers and video to a RenderTexture / Renderer / RawImage.
/// LibVLCSharp: https://code.videolan.org/videolan/LibVLCSharp/-/blob/master/docs/home.md
/// </remarks>
namespace Frame.Core.VLC
{
    public class CustomVLCPlayer : MonoBehaviour, IVideoPlayer
    {
        public static LibVLC libVLC; //The LibVLC class is mainly used for making MediaPlayer and Media objects. You should only have one LibVLC instance.
        private static int s_libVlcRefCount;
        public MediaPlayer mediaPlayer; //MediaPlayer is the main class we use to interact with VLC

        //Screens
        public Renderer screen; //Assign a mesh to render on a 3d object
        public RawImage canvasScreen; //Assign a Canvas RawImage to render on a GUI object

        Texture2D _vlcTexture = null; //This is the texture libVLC writes to directly. It's private.
        public RenderTexture texture = null; //We copy it into this texture which we actually use in unity.


        public string path = "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"; //Can be a web path or a local path

        // when copying native Texture2D textures to Unity RenderTextures, the orientation mapping is incorrect on Android, so we flip it over.
        public bool flipTextureX = true;
        public bool flipTextureY = true;
        public bool playOnAwake = true; //Open path and Play during Awake
        [SerializeField] private bool m_multAudio;
        public bool logToConsole = false; //Log function calls and LibVLC logs to Unity console

        [SerializeField] private int m_antiAliasing = 1;
        //Unity Awake, OnDestroy, and Update functions

        #region unity

        void Awake()
        {
            //Setup LibVLC
            if (libVLC == null)
                CreateLibVLC();

            //Setup Screen
            if (screen == null)
                screen = GetComponent<Renderer>();
            if (canvasScreen == null)
                canvasScreen = GetComponent<RawImage>();

            //Setup Media Player
            CreateMediaPlayer();

            //Play On Start
            if (playOnAwake)
                Open();
        }

        void OnDestroy()
        {
            //Dispose of mediaPlayer, or it will stay in nemory and keep playing audio
            DestroyMediaPlayer();
            ReleaseOutputTextures();
        }

        void Update()
        {
            if (mediaPlayer == null)
            {
                return;
            }

            //Get size every frame
            uint height = 0;
            uint width = 0;
            mediaPlayer.Size(0, ref width, ref height);

            //Automatically resize output textures if size changes
            if (_vlcTexture == null || _vlcTexture.width != width || _vlcTexture.height != height)
            {
                ResizeOutputTextures(width, height);
            }

            if (_vlcTexture != null)
            {
                //Update the vlc texture (tex)
                var texptr = mediaPlayer.GetTexture(width, height, out bool updated);
                if (updated)
                {
                    _vlcTexture.UpdateExternalTexture(texptr);

                    //Copy the vlc texture into the output texture, automatically flipped over
                    var flip = new Vector2(flipTextureX ? -1 : 1, flipTextureY ? -1 : 1);

                    Graphics.Blit(_vlcTexture, texture, flip, Vector2.zero); //If you wanted to do post processing outside of VLC you could use a shader here.
                }
            }
        }

        #endregion


        //Public functions that expose VLC MediaPlayer functions in a Unity-friendly way. You may want to add more of these.

        #region vlc

        public void Open(string path)
        {
            Log("VLCPlayerExample Open " + path);
            this.path = path;
            Open();
        }

        public void Open()
        {
            Log("VLCPlayerExample Open");
            if (mediaPlayer.Media != null)
                mediaPlayer.Media.Dispose();

            var trimmedPath = path.Trim(new char[] { '"' }); //Windows likes to copy paths with quotes but Uri does not like to open them
            mediaPlayer.Media = new Media(new Uri(trimmedPath));
            if (m_multAudio)
            {
                mediaPlayer.SetVolume(0);
            }

            Play();
        }

        public void Init(RawImage image)
        {
            canvasScreen = image;
        }

        public void Play()
        {
            Log("VLCPlayerExample Play");
            if (m_multAudio)
            {
                mediaPlayer?.SetVolume(0);
            }

            mediaPlayer?.Play();
        }

        public void Pause()
        {
            Log("VLCPlayerExample Pause");
            mediaPlayer?.Pause();
        }

        public void Stop()
        {
            Log("VLCPlayerExample Stop");
            mediaPlayer?.Stop();
            ReleaseOutputTextures();
        }

        public void Seek(long timeDelta)
        {
            Log("VLCPlayerExample Seek " + timeDelta);
            mediaPlayer.SetTime(mediaPlayer.Time + timeDelta);
        }

        public void SetTime(long time)
        {
            Log("VLCPlayerExample SetTime " + time);
            mediaPlayer.SetTime(time);
        }

        public void SetVolume(int volume = 100)
        {
            Log("VLCPlayerExample SetVolume " + volume);
            mediaPlayer.SetVolume(volume);
        }

        public int Volume
        {
            get
            {
                if (mediaPlayer == null)
                    return 0;
                return mediaPlayer.Volume;
            }
        }

        public bool IsPlaying
        {
            get
            {
                if (mediaPlayer == null)
                    return false;
                return mediaPlayer.IsPlaying;
            }
        }

        public long Duration
        {
            get
            {
                if (mediaPlayer == null || mediaPlayer.Media == null)
                    return 0;
                return mediaPlayer.Media.Duration;
            }
        }

        public long Time
        {
            get
            {
                if (mediaPlayer == null)
                    return 0;
                return mediaPlayer.Time;
            }
        }

        public List<MediaTrack> Tracks(TrackType type)
        {
            Log("VLCPlayerExample Tracks " + type);
            return ConvertMediaTrackList(mediaPlayer?.Tracks(type));
        }

        public MediaTrack SelectedTrack(TrackType type)
        {
            Log("VLCPlayerExample SelectedTrack " + type);
            return mediaPlayer?.SelectedTrack(type);
        }

        public void Select(MediaTrack track)
        {
            Log("VLCPlayerExample Select " + track.Name);
            mediaPlayer?.Select(track);
        }

        public void Unselect(TrackType type)
        {
            Log("VLCPlayerExample Unselect " + type);
            mediaPlayer?.Unselect(type);
        }

        //This returns the video orientation for the currently playing video, if there is one
        public VideoOrientation? GetVideoOrientation()
        {
            var tracks = mediaPlayer?.Tracks(TrackType.Video);

            if (tracks == null || tracks.Count == 0)
                return null;

            var orientation = tracks[0]?.Data.Video.Orientation; //At the moment we're assuming the track we're playing is the first track

            return orientation;
        }

        #endregion

        //Private functions create and destroy VLC objects and textures

        #region internal

        //Create a new static LibVLC instance and dispose of the old one. You should only ever have one LibVLC instance.
        void CreateLibVLC()
        {
            Log("VLCPlayerExample CreateLibVLC");
            // 共享静态 LibVLC：仅首次创建，避免多实例互相 Dispose
            if (libVLC != null)
            {
                s_libVlcRefCount++;
                return;
            }

            global::LibVLCSharp.Core.Initialize(Application.dataPath); //Load VLC dlls
            libVLC = new LibVLC(enableDebugLogs: true); //You can customize LibVLC with advanced CLI options here https://wiki.videolan.org/VLC_command-line_help/
            s_libVlcRefCount = 1;

            //Setup Error Logging
            //Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            libVLC.Log += (s, e) =>
            {
                //Always use try/catch in LibVLC events.
                //LibVLC can freeze Unity if an exception goes unhandled inside an event handler.
                try
                {
                    if (logToConsole)
                    {
                        Log(e.FormattedLog);
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception caught in libVLC.Log: \n" + ex.ToString());
                }
            };
        }

        //Create a new MediaPlayer object and dispose of the old one.
        void CreateMediaPlayer()
        {
            Log("VLCPlayerExample CreateMediaPlayer");
            if (mediaPlayer != null)
            {
                DestroyMediaPlayer();
            }

            mediaPlayer = new MediaPlayer(libVLC);
        }

        //Dispose of the MediaPlayer object.
        void DestroyMediaPlayer()
        {
            Log("VLCPlayerExample DestroyMediaPlayer");
            mediaPlayer?.Stop();
            mediaPlayer?.Dispose();
            mediaPlayer = null;

            if (s_libVlcRefCount > 0)
            {
                s_libVlcRefCount--;
            }

            if (s_libVlcRefCount <= 0 && libVLC != null)
            {
                libVLC.Dispose();
                libVLC = null;
                s_libVlcRefCount = 0;
            }
        }

        //Resize the output textures to the size of the video
        void ResizeOutputTextures(uint px, uint py)
        {
            var texptr = mediaPlayer.GetTexture(px, py, out bool updated);
            if (px != 0 && py != 0 && updated && texptr != IntPtr.Zero)
            {
                //If the currently playing video uses the Bottom Right orientation, we have to do this to avoid stretching it.
                if (GetVideoOrientation() == VideoOrientation.BottomRight)
                {
                    (px, py) = (py, px);
                }

                ReleaseOutputTextures();
                _vlcTexture = Texture2D.CreateExternalTexture((int)px, (int)py, TextureFormat.RGBA32, false, true, texptr); //Make a texture of the proper size for the video to output to
                texture = new RenderTexture(Mathf.Max(1, (int)px), Mathf.Max(1, (int)py), 0, RenderTextureFormat.ARGB32);
                texture.antiAliasing = m_antiAliasing;

                if (screen != null)
                    screen.material.mainTexture = texture;
                if (canvasScreen != null)
                    canvasScreen.texture = texture;
            }
        }

        //Converts MediaTrackList objects to Unity-friendly generic lists. Might not be worth the trouble.
        List<MediaTrack> ConvertMediaTrackList(MediaTrackList tracklist)
        {
            if (tracklist == null)
                return new List<MediaTrack>(); //Return an empty list

            var tracks = new List<MediaTrack>((int)tracklist.Count);
            for (uint i = 0; i < tracklist.Count; i++)
            {
                tracks.Add(tracklist[i]);
            }

            return tracks;
        }

        void ReleaseOutputTextures()
        {
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }

            if (_vlcTexture != null)
            {
                UnityEngine.Object.Destroy(_vlcTexture);
                _vlcTexture = null;
            }
        }

        void Log(object message)
        {
            if (logToConsole)
                Debug.Log(message);
        }

        #endregion
    }
}
