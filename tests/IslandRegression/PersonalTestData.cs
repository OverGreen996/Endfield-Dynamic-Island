using EndfieldChargePlus.Assistant;

internal static class PersonalTestData
{
    internal static PersonalMemory? Memory(PersonalAssistantStore store,string text,string category="個人資料")
        => store.AcceptModelSuggestion(text,new(category,text,text,"user","durable",""),DateTimeOffset.Now);
    internal static LocalAssistantResult? Remind(PersonalAssistantStore store,string text,int seconds)
        => store.ApplyModelAction(text,new("remind",text,text,DateTimeOffset.Now.AddSeconds(seconds).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd'T'HH:mm:sszzz"),""),DateTimeOffset.Now);
}
