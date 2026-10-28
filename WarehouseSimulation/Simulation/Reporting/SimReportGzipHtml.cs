using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 报告大块数据：gzip + base64 内嵌，浏览器用 DecompressionStream 解压后渲染。
    /// </summary>
    internal static class SimReportGzipHtml
    {
        public const string EncodingGzipBase64 = "gzip-base64";

        /// <summary>浏览器端解压 base64 gzip 文本；无 DecompressionStream 时给出提示。</summary>
        public const string GunzipScript = @"
function nkGunzipB64(b64){
  if(typeof DecompressionStream==='undefined'){
    return Promise.reject(new Error('当前浏览器不支持 DecompressionStream，请用较新的 Chrome / Edge / Firefox / Safari 打开报告。'));
  }
  var bin=atob(b64);
  var bytes=new Uint8Array(bin.length);
  for(var i=0;i<bin.length;i++)bytes[i]=bin.charCodeAt(i);
  var stream=new Response(bytes).body.pipeThrough(new DecompressionStream('gzip'));
  return new Response(stream).text();
}
async function nkLoadEmbeddedJson(el){
  if(!el)throw new Error('缺少数据节点');
  var raw=(el.textContent||'').trim();
  if(!raw)return null;
  var enc=el.getAttribute('data-encoding')||'';
  var text=enc==='gzip-base64'?await nkGunzipB64(raw):raw;
  return JSON.parse(text);
}
";

        public static string ToGzipBase64(string utf8Text)
        {
            if (string.IsNullOrEmpty(utf8Text))
            {
                return Convert.ToBase64String(Gzip(Array.Empty<byte>()));
            }

            return Convert.ToBase64String(Gzip(Encoding.UTF8.GetBytes(utf8Text)));
        }

        public static void AppendGzipJsonScript(
            StringBuilder sb,
            string elementId,
            string jsonText,
            string scriptType = "application/json")
        {
            if (sb == null)
            {
                return;
            }

            var payload = ToGzipBase64(jsonText ?? "null");
            sb.Append("<script type=\"");
            sb.Append(scriptType);
            sb.Append("\" id=\"");
            sb.Append(elementId);
            sb.Append("\" data-encoding=\"");
            sb.Append(EncodingGzipBase64);
            sb.Append("\">");
            sb.Append(payload);
            sb.AppendLine("</script>");
        }

        public static void AppendGzipHtmlScript(
            StringBuilder sb,
            string elementId,
            string htmlFragment)
        {
            if (sb == null)
            {
                return;
            }

            var payload = ToGzipBase64(htmlFragment ?? string.Empty);
            sb.Append("<script type=\"text/plain\" id=\"");
            sb.Append(elementId);
            sb.Append("\" data-encoding=\"");
            sb.Append(EncodingGzipBase64);
            sb.Append("\">");
            sb.Append(payload);
            sb.AppendLine("</script>");
        }

        private static byte[] Gzip(byte[] raw)
        {
            raw ??= Array.Empty<byte>();
            using var output = new MemoryStream(Math.Max(64, raw.Length / 4));
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return output.ToArray();
        }
    }
}
