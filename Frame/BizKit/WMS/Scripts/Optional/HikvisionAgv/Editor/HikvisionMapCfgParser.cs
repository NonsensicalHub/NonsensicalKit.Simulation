using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HikvisionAgv.Editor
{
    public sealed class HikvisionMapCfg
    {
        public string MapId = string.Empty;
        public string MapName = string.Empty;
        public readonly List<HikvisionMapPoint> Points = new List<HikvisionMapPoint>();
    }

    public sealed class HikvisionMapPoint
    {
        public int Id;
        public float Xpos;
        public float Ypos;
        public int AllDir;
        public float Width;
        public float Height;
        public int RoadProperty;
        public readonly List<HikvisionMapNeighbor> Neighbors = new List<HikvisionMapNeighbor>();
    }

    public sealed class HikvisionMapNeighbor
    {
        public int Id;
        public float Distance;
        public int Rever;
    }

    public static class HikvisionMapCfgParser
    {
        static readonly Regex PointBlock = new Regex(
            @"<PointInfo>([\s\S]*?)</PointInfo>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static HikvisionMapCfg Parse(string xml)
        {
            var cfg = new HikvisionMapCfg();
            if (string.IsNullOrEmpty(xml))
                return cfg;

            cfg.MapId = ReadTag(xml, "MapID");
            cfg.MapName = ReadTag(xml, "MapName");

            var blocks = PointBlock.Matches(xml);
            for (int i = 0; i < blocks.Count; i++)
            {
                var point = ParsePoint(blocks[i].Groups[1].Value);
                cfg.Points.Add(point);
            }

            return cfg;
        }

        static HikvisionMapPoint ParsePoint(string inner)
        {
            var point = new HikvisionMapPoint
            {
                Id = ReadInt(inner, "id"),
                Xpos = ReadFloat(inner, "xpos"),
                Ypos = ReadFloat(inner, "ypos"),
                AllDir = ReadInt(inner, "allDir"),
                Width = ReadFloat(inner, "width"),
                Height = ReadFloat(inner, "height"),
                RoadProperty = ReadInt(inner, "RoadProperty")
            };

            string neighb = ReadTag(inner, "NeighbInfo");
            if (string.IsNullOrEmpty(neighb))
                return point;

            var chunks = Regex.Split(neighb, @"(?=<id>)");
            for (int i = 0; i < chunks.Length; i++)
            {
                string chunk = chunks[i];
                if (string.IsNullOrWhiteSpace(chunk) || !chunk.Contains("<id>"))
                    continue;
                int id = ReadInt(chunk, "id");
                if (id == point.Id && !HasTag(chunk, "distance"))
                    continue;

                point.Neighbors.Add(new HikvisionMapNeighbor
                {
                    Id = id,
                    Distance = ReadFloat(chunk, "distance"),
                    Rever = ReadInt(chunk, "Rever")
                });
            }

            return point;
        }

        static bool HasTag(string xml, string tag)
        {
            return xml.IndexOf("<" + tag + ">", System.StringComparison.Ordinal) >= 0;
        }

        static string ReadTag(string xml, string tag)
        {
            var match = Regex.Match(
                xml,
                "<" + tag + @">([\s\S]*?)</" + tag + ">",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        static int ReadInt(string xml, string tag)
        {
            string text = ReadTag(xml, tag);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;
            return 0;
        }

        static float ReadFloat(string xml, string tag)
        {
            string text = ReadTag(xml, tag);
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;
            return 0f;
        }
    }
}
