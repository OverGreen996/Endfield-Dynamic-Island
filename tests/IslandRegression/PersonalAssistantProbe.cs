using EndfieldChargePlus.Assistant;
using System.Text;

public static class PersonalAssistantProbe
{
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"IslandPersonalQA-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var now=DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(8));
        var passed=0;void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        var path=Path.Combine(root,"personal.dpapi");var store=new PersonalAssistantStore(path);
        MemorySuggestion Memory(string text,string category="回覆偏好")=>new(category,text,text,"user","durable","");
        const string preference="不要每次回答都叫我名字";
        var memory=store.AcceptModelSuggestion(preference,Memory(preference),now)!;
        Check(memory.Category=="回覆偏好"&&new PersonalAssistantStore(path).Memories.Single().Text==preference,"AI memory persists without local command parser");
        Check(store.AcceptModelSuggestion(preference,Memory(preference),now) is null&&store.Memories.Count==1,"duplicate AI suggestions do not repeat saved notice");
        const string prohibition="我不希望 AI 要求我貼 API Key";
        Check(store.AcceptModelSuggestion(prohibition,Memory(prohibition,"互動界線"),now) is not null,"interaction boundary can mention API keys without being mistaken for a secret");
        Check(store.AcceptModelSuggestion("我養兔子",new("寵物飼養","他養兔子","我養兔子","user","durable",""),now) is null,"missing source quote cannot create memory");
        Check(store.AcceptModelSuggestion("我叫測試",new("身分稱呼","我叫測試","我叫測試","other","durable",""),now) is null,"only AI-classified user facts accepted");
        Check(store.AcceptModelSuggestion("今天生氣",new("心情","今天生氣","今天生氣","user","temporary",""),now) is null,"temporary AI classification cannot persist");
        Check(store.AcceptModelSuggestion("我叫測試",Memory("我叫測試","EnglishOnly"),now) is null,"new category must be readable Chinese");
        var history=new[]{new ChatMessage("user","哪款遊戲比較好？"),new ChatMessage("model","先確認類型。")};
        var enriched=store.WithMemory(history,"那還有什麼？");
        Check(enriched[0].text==history[0].text&&history[1].text=="先確認類型。"&&enriched[1].text.Contains(memory.Id),"context enrichment retains complete pairs and memory IDs");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(preference),"personal facts encrypted for current Windows user");
        store.UpdateMemory(memory.Id,"對話習慣","回答簡短直白");
        Check(new PersonalAssistantStore(path).Memories.Any(m=>m.Id==memory.Id&&m.Category=="對話習慣"),"manual category and text editing survive reload");
        store.DeleteMemory(memory.Id);
        Check(!new PersonalAssistantStore(path).Memories.Any(m=>m.Id==memory.Id),"individual memory deletion is durable");
        PersonalAction Remind(string text,DateTimeOffset due)=>new("remind",text,text,due.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd'T'HH:mm:sszzz"),"");
        var due=now.AddMinutes(30);store.ApplyModelAction("喝水",Remind("喝水",due),now);
        Check(store.Reminders.Count==1&&store.Due(due.AddSeconds(-1)).Count==0&&store.Due(due).Count==1,"reminder doesn't fire early and uses exact saved instant");
        store.ApplyModelAction("喝水",Remind("喝水",due),now);
        Check(store.Reminders.Count==1,"identical reminder is not duplicated");
        foreach(var badDue in new[]{"2026-02-30T09:00:00+08:00",now.AddMinutes(-1).ToString("yyyy-MM-dd'T'HH:mm:sszzz"),now.AddDays(400).ToString("yyyy-MM-dd'T'HH:mm:sszzz"),now.AddHours(1).ToOffset(TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:sszzz")})
            Check(store.ApplyModelAction("測試",new("remind","測試","測試",badDue,""),now)?.Text.Contains(EndfieldChargePlus.LocalizationManager.Text("尚未建立提醒","Reminder not created"))==true&&store.Reminders.Count==1,"invalid/past/outside-year/non-Taipei reminder rejected");
        var reminder=store.Reminders[0];store.MarkDisplayed(reminder.Id,now);
        Check(new PersonalAssistantStore(path).Due(due).Count==0,"shown reminder isn't replayed after restart");
        store.UpdateReminder(reminder.Id,"補充喝水",DateTimeOffset.Now.AddHours(2));
        Check(store.Reminders[0].Displayed is null,"editing reschedules a shown reminder");
        store.ClearMemories();Check(store.Memories.Count==0&&store.Reminders.Count==1,"clearing palace preserves schedules");
        store.ApplyModelAction("包裹提醒不要了",new("delete_reminder","包裹提醒不要了","","",reminder.Id),now);
        Check(store.Reminders.Count==0,"AI-selected cancel removes only the existing reminder");
        var corrupt=Path.Combine(root,"corrupt.dpapi");File.WriteAllText(corrupt,"retain");var broken=new PersonalAssistantStore(corrupt);
        var blocked=false;try{broken.AcceptModelSuggestion(preference,Memory(preference),now);}catch(InvalidOperationException){blocked=true;}
        Check(blocked&&File.ReadAllText(corrupt)=="retain","corrupt personal store retained instead of overwritten");
        var parent=Path.Combine(root,"file");File.WriteAllText(parent,"keep");
        var unavailable=new PersonalAssistantStore(Path.Combine(parent,"personal.dpapi"));var failed=false;
        try{unavailable.ApplyModelAction("喝水",Remind("喝水",due),now);}catch(InvalidOperationException){failed=true;}
        Check(failed&&unavailable.Reminders.Count==0,"failed disk write does not acknowledge or schedule reminder");
        failed=false;try{unavailable.AcceptModelSuggestion(preference,Memory(preference),now);}catch(InvalidOperationException){failed=true;}
        Check(failed&&unavailable.Memories.Count==0,"failed memory write cannot acknowledge success");
        var capped=new PersonalAssistantStore(Path.Combine(root,"capacity.dpapi"));
        for(var i=0;i<301;i++)capped.ApplyModelAction("事項"+i,Remind("事項"+i,now.AddMinutes(i+1)),now);
        Check(capped.Reminders.Count==300&&capped.Reminders[0].Text=="事項1"&&capped.Reminders[^1].Text=="事項300","full reminders evict oldest created record in FIFO order");
        Check(!new PersonalAssistantStore(Path.Combine(root,"capacity.dpapi")).Reminders.Any(r=>r.Text=="事項0"),"old pending reminder eviction persists");
        Console.WriteLine($"{passed}/{passed} PASS; AI decisions, isolated DPAPI stores, no model calls.");
    }
}
