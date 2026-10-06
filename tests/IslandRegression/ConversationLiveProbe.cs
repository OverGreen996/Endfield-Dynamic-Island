using System.Text.Json;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

internal static class ConversationLiveProbe
{
    // Explicit live check only. Never included in automated suites or installer tests.
    internal static async Task RunAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var service=NativeAssistantService.Shared;var history=new List<ChatMessage>();var passed=0;
        try{
            var before=J.N(service.Usage()["models"]!["gemini-3.5-flash-lite"],"used_requests_today");
            foreach(var question in new[]{"終於把那個破設定關掉了，瞬間安靜好多。","先別替我安排一堆事，我現在只是想吐槽這台電腦。","換個話題，我家黑王蛇叫烏龍，牠老愛躲在水碗底下，跟住套房一樣。"}){
                var reply=await service.AskAsync(question,history,"auto",timeout.Token);
                Console.WriteLine(JsonSerializer.Serialize(new{question,answer=reply.text,reply.model,reply.search_used,memories=reply.memory_suggestions?.Length??0}));
                if(reply.answer_kind!="model"||reply.search_used||string.IsNullOrWhiteSpace(reply.text)||reply.text.Contains("[S1]")||reply.text.Contains("本次没有充分")||reply.text.Contains("本次沒有充分"))throw new Exception("Live conversational reply did not meet requirements.");
                if(passed<2&&(reply.memory_suggestions?.Length??0)>0)throw new Exception("Temporary chat request incorrectly became persistent memory.");
                if(passed==2&&!(reply.memory_suggestions?.Any(m=>m.text?.Contains("黑王蛇")==true)??false))throw new Exception("Durable pet fact not understood.");
                history.Add(new("user",question));history.Add(new("model",reply.text));passed++;
            }
            var after=J.N(service.Usage()["models"]!["gemini-3.5-flash-lite"],"used_requests_today");
            Console.WriteLine($"{passed}/{passed} live conversation checks PASS; {after-before} model requests; no search calls, no user chat or memory files written.");
        }finally{NativeAssistantService.Shutdown();}
    }
}
