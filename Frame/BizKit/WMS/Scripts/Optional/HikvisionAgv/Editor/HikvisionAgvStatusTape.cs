using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HikvisionAgv;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HikvisionAgv.Editor
{
    /// <summary>queryAgvStatus 录制带：JSONL，一行一帧，带 unix 毫秒时间戳。</summary>
    public static class HikvisionAgvStatusTape
    {
        public const string TypeSession = "session";
        public const string TypeFrame = "frame";

        public static readonly JsonSerializerSettings JsonSetting = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        [Serializable]
        public sealed class Session
        {
            public string type = TypeSession;
            public string startedAt;
            public float intervalSeconds;
            public string[] maps;
            public string baseUrl;
        }

        [Serializable]
        public sealed class Frame
        {
            public string type = TypeFrame;
            public long unixMs;
            public string time;
            public string map;
            public int code;
            public string message;
            public bool ok;
            public List<HikAgvStatusDto> data;
        }

        public static StreamWriter OpenWrite(string path, Session session)
        {
            var writer = new StreamWriter(path, false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            writer.WriteLine(JsonConvert.SerializeObject(session, JsonSetting));
            return writer;
        }

        public static void WriteFrame(StreamWriter writer, Frame frame)
        {
            if (writer == null || frame == null)
                return;
            frame.type = TypeFrame;
            writer.WriteLine(JsonConvert.SerializeObject(frame, JsonSetting));
        }

        public static Frame MakeFrame(string map, bool httpOk, string rawJson, string httpError)
        {
            var now = DateTimeOffset.Now;
            var frame = new Frame
            {
                unixMs = now.ToUnixTimeMilliseconds(),
                time = now.ToString("o", CultureInfo.InvariantCulture),
                map = map ?? string.Empty,
                ok = false,
                code = -1,
                message = httpError,
                data = new List<HikAgvStatusDto>()
            };

            if (!httpOk)
            {
                if (string.IsNullOrEmpty(frame.message))
                    frame.message = "HTTP 失败";
                return frame;
            }

            try
            {
                var envelope = JsonConvert.DeserializeObject<HikApiResponse<HikAgvStatusDto>>(rawJson, JsonSetting);
                if (envelope == null)
                {
                    frame.message = "响应无法解析";
                    return frame;
                }

                frame.code = envelope.code;
                frame.message = envelope.message;
                frame.data = envelope.data ?? new List<HikAgvStatusDto>();
                frame.ok = envelope.code == 0;
                if (string.IsNullOrEmpty(frame.map))
                {
                    for (int i = 0; i < frame.data.Count; i++)
                    {
                        if (frame.data[i] != null && !string.IsNullOrEmpty(frame.data[i].mapCode))
                        {
                            frame.map = frame.data[i].mapCode;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                frame.message = e.Message;
            }

            return frame;
        }

        public static List<Frame> Load(string path, out Session session, out string error)
        {
            session = null;
            error = null;
            var frames = new List<Frame>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "文件不存在";
                return frames;
            }

            string text = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "文件为空";
                return frames;
            }

            string trimmed = text.TrimStart();
            try
            {
                if (trimmed.StartsWith("["))
                {
                    var list = JsonConvert.DeserializeObject<List<Frame>>(text, JsonSetting);
                    if (list != null)
                        frames.AddRange(list);
                }
                else if (trimmed.StartsWith("{") && !LooksLikeJsonl(text))
                {
                    var jo = JObject.Parse(text);
                    if (jo["frames"] is JArray arr)
                    {
                        foreach (var token in arr)
                        {
                            var frame = token.ToObject<Frame>();
                            if (frame != null)
                                frames.Add(frame);
                        }
                    }
                    else
                    {
                        var one = jo.ToObject<Frame>();
                        if (one != null && one.data != null)
                            frames.Add(one);
                    }
                }
                else
                {
                    using var reader = new StringReader(text);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        var jo = JObject.Parse(line);
                        string type = jo.Value<string>("type") ?? jo.Value<string>("_type");
                        if (type == TypeSession)
                        {
                            session = jo.ToObject<Session>();
                            continue;
                        }

                        var frame = jo.ToObject<Frame>(JsonSerializer.Create(JsonSetting));
                        if (frame != null)
                            frames.Add(frame);
                    }
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return frames;
            }

            frames.Sort((a, b) => a.unixMs.CompareTo(b.unixMs));
            return frames;
        }

        static bool LooksLikeJsonl(string text)
        {
            int nl = text.IndexOf('\n');
            return nl >= 0 && nl < text.Length - 1;
        }
    }
}
