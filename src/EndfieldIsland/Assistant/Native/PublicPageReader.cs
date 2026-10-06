using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldChargePlus.Assistant.Native;

internal static class PublicPageReader
{
    internal static bool Public(IPAddress ip)
    {
        if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();var b=ip.GetAddressBytes();
        if(b.Length==4)
        {
            var a=b[0];var c=b[1];
            return a is not(0 or 10 or 127)&&a<224&&!(a==100&&c is >=64 and <=127)&&!(a==169&&c==254)&&!(a==172&&c is >=16 and <=31)&&!(a==192&&(c==168||c==0||c==88))&&!(a==198&&(c==18||c==19||c==51&&b[2]==100))&&!(a==203&&c==0&&b[2]==113);
        }
        return (b[0]&0xe0)==0x20&&!(b[0]==0x20&&b[1]==0x01&&((b[2]==0x0d&&b[3]==0xb8)||(b[2]==0&&b[3]==0)))&&!(b[0]==0x20&&b[1]==0x02);
    }
    private static bool Safe(Uri uri)=>uri.Scheme is "http" or "https"&&uri.UserInfo==""&&((uri.Scheme=="http"&&uri.Port==80)||(uri.Scheme=="https"&&uri.Port==443))&&uri.Host!="localhost"&&(!IPAddress.TryParse(uri.Host,out var ip)||Public(ip));
    internal static async Task<SearchRow?> ReadAsync(string url,CancellationToken token)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(4000);
        using var handler=new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false,UseCookies=false,ConnectCallback=async(context,cancellation)=>
        {
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,cancellation);
            if(addresses.Length==0||addresses.Any(ip=>!Public(ip)))throw new SearchFailure("unsafe_source");
            var socket=new Socket(addresses[0].AddressFamily,SocketType.Stream,ProtocolType.Tcp);
            try{await socket.ConnectAsync(new IPEndPoint(addresses[0],context.DnsEndPoint.Port),cancellation);return new NetworkStream(socket,true);}catch{socket.Dispose();throw;}
        }};
        using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri))return null;
        for(var redirects=0;redirects<=2;redirects++)
        {
            if(!Safe(uri))return null;
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.TryAddWithoutValidation("Accept","text/html, text/plain");request.Headers.TryAddWithoutValidation("Accept-Encoding","identity");request.Headers.UserAgent.ParseAdd("EndfieldIsland/0.28");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
            if((int)response.StatusCode is >=300 and <=399)
            {
                if(response.Headers.Location is null)return null;uri=new Uri(uri,response.Headers.Location);continue;
            }
            if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>1500000||response.Content.Headers.ContentEncoding.Count>0)return null;
            var mime=response.Content.Headers.ContentType?.MediaType;if(mime is not("text/html" or "text/plain"))return null;
            await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int read;
            while((read=await stream.ReadAsync(buffer,deadline.Token))>0){if(memory.Length+read>1500000)return null;memory.Write(buffer,0,read);}
            var html=Encoding.UTF8.GetString(memory.ToArray());var body=Text(html);
            if(body is null)return null;
            return new("",url,body,"page",null,DateTimeOffset.UtcNow.ToString("O"));
        }
        return null;
    }
    internal static string? Text(string html)
    {
        const RegexOptions flags=RegexOptions.IgnoreCase|RegexOptions.Singleline;
        var timeout=TimeSpan.FromMilliseconds(200);
        var title=Regex.Match(html,"<title[^>]*>(.*?)</title>",flags,timeout).Groups[1].Value;
        if(Regex.IsMatch(title,"just a moment|access denied|attention required|verify.*human|驗證",flags,timeout))return null;
        var body=Regex.Replace(html,@"<(script|style|nav|footer|header|form|noscript|svg)\b[^>]*>.*?</\1>"," ",flags,timeout);
        body=Regex.Replace(body,@"<[^>]+>"," ",flags,timeout);body=WebUtility.HtmlDecode(body);body=Regex.Replace(body,@"\s+"," ",RegexOptions.None,timeout).Trim();
        if(body.Length<120)return null;return body[..Math.Min(body.Length,14000)];
    }
}
