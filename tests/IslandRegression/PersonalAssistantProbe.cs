using EndfieldChargePlus.Assistant;
using System.Text;

public static class PersonalAssistantProbe
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"IslandPersonalQA-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        // ReminderTime interprets wall-clock dates in the user's Windows timezone.
        // Use that same timezone for the injected clock, including UTC CI runners.
        var wallClock=new DateTime(2026,10,4,12,0,0,DateTimeKind.Unspecified);
        var now=new DateTimeOffset(wallClock,TimeZoneInfo.Local.GetUtcOffset(wallClock));
        int passed=0;void Check(bool v,string name){if(!v)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
        foreach(var test in new[]{("明天上午九點提醒我開會",new DateTimeOffset(2026,10,5,9,0,0,now.Offset)),("明天下午三點半提醒我買牛奶",new DateTimeOffset(2026,10,5,15,30,0,now.Offset)),("提醒我後天晚上八點喝水",new DateTimeOffset(2026,10,6,20,0,0,now.Offset)),("30 分鐘後提醒我喝水",now.AddMinutes(30)),("兩小時後提醒我休息",now.AddHours(2)),("今天18:40提醒我整理桌面",new DateTimeOffset(2026,10,4,18,40,0,now.Offset)),("2026/10/10 14:10提醒我領包裹",new DateTimeOffset(2026,10,10,14,10,0,now.Offset)),("下週一上午九點提醒我整理",new DateTimeOffset(2026,10,5,9,0,0,now.Offset)),("明天中午十二點提醒我吃飯",new DateTimeOffset(2026,10,5,12,0,0,now.Offset)),("10秒後提醒我測試",now.AddSeconds(10))})
            Check(ReminderTime.Parse(test.Item1,now)?.Due==test.Item2,"natural Chinese time: "+test.Item1);
        foreach(string text in new[]{"明天九點提醒我開會","明天下午十三點提醒我開會","明天25:00提醒我喝水","明天13:70提醒我喝水","今天上午九點提醒我開會","2026/02/30 14:00提醒我開會","0分鐘後提醒我休息","明天上午九點提醒我","明天上午九點提醒我開會然後再提醒我吃飯"})
            Check(ReminderTime.Parse(text,now) is null,"reject ambiguous/invalid/multi-action time: "+text);
        var path=Path.Combine(root,"personal.dpapi");var store=new PersonalAssistantStore(path);
        var result=store.Handle("明天上午九點提醒我開會",now);Check(result?.Text.Contains("已設定提醒")==true&&store.Reminders.Count==1,"only commit acknowledged reminder");
        store.Handle("明天上午九點提醒我開會",now);Check(store.Reminders.Count==1,"duplicate request does not duplicate reminder");
        store.Handle("幫我安排明天下午三點開會",now);Check(store.Reminders.Count==2,"explicit arrange-time command schedules locally");
        store.Handle("幫我安排30分鐘後喝水",now);Check(store.Reminders.Count==3,"arrange relative time");
        foreach(string text in new[]{"假設明天上午九點提醒我開會","每天上午九點提醒我開會","不要明天上午九點提醒我開會","明天上午九點提醒我開會嗎？","能不能明天上午九點提醒我開會"})
        {int count=store.Reminders.Count;store.Handle(text,now);Check(store.Reminders.Count==count,"no silent action for hypothetical/negated/question/recurrence: "+text);}
        Check(store.Due(now.AddMinutes(29)).Count==0&&store.Due(now.AddMinutes(30)).Count==1,"due comparison exact and no early fire");
        var reloaded=new PersonalAssistantStore(path);Check(reloaded.Reminders.Count==3&&reloaded.Due(now.AddDays(10)).Count==3,"restart finds overdue undelivered reminders");
        reloaded.MarkDisplayed(reloaded.Reminders[0].Id,now);Check(new PersonalAssistantStore(path).Due(now.AddDays(10)).Count==2,"displayed reminder not replayed after restart");
        reloaded.DeleteReminder(reloaded.Reminders[1].Id);Check(new PersonalAssistantStore(path).Reminders.Count==2,"delete reminder persists");
        var identity=store.ObserveSelfStatement("我叫測試使用者",now);Check(identity?.Category=="身分資料","clear self introduction categorized");
        Check(store.ObserveSelfStatement("我喜歡黑色簡潔介面",now)?.Category=="喜好偏好","stable preference categorized");
        Check(store.ObserveSelfStatement("以後回答請用繁體中文",now)?.Category=="回答方式","response preference categorized");
        Check(store.ObserveSelfStatement("我不希望AI自作主張替我做決定",now)?.Category=="互動禁忌","interaction prohibition categorized");
        Check(store.ObserveSelfStatement("我每天晚上十點休息",now)?.Category=="生活習慣","habit categorized");
        Check(store.ObserveSelfStatement("我的目標是學會日文",now)?.Category=="目標計畫","goal categorized");
        var pet=store.AcceptModelSuggestion("我有養一隻 兔子",new("個人事項","我有養一隻 兔子"),now);
        Check(pet?.Category=="個人事項"&&pet.Text=="我有養一隻 兔子","Gemini classifies pet but local store saves only original first-person evidence");
        Check(new PersonalAssistantStore(path).Memories.Any(m=>m.Id==pet!.Id),"model-classified pet survives restart");
        foreach(var test in new[]{("我有養一條蛇，開玩笑的","我有養一條蛇，開玩笑的"),("假設我養兔子","假設我養兔子"),("他說我養蛇","他說我養蛇"),("我有養蛇嗎？","我有養蛇嗎？"),("我有養一隻兔子","我喜歡爬蟲類"),("我現在很生氣","我現在很生氣"),("我有養蛇。她養貓","我有養蛇。她養貓"),("我的 API Key 是 test-only","我的 API Key 是 test-only")})
            Check(store.AcceptModelSuggestion(test.Item1,new("個人事項",test.Item2),now) is null,"model cannot bypass local personal memory guard: "+test.Item1);
        Check(store.AcceptModelSuggestion("我偏好灰色",new("不存在","我偏好灰色"),now) is null,"unknown model memory category rejected");
        Check(store.WithMemory(Array.Empty<ChatMessage>(),"我的寵物是什麼").Any(m=>m.text.Contains("兔子")),"saved pet is provided to later conversations");
        Check(store.Handle("幫我記下個人事項：整理書桌",now)?.SavedMemory is not null&&store.Memories.Any(m=>m.Category=="個人事項"),"explicit undated personal task categorized");
        foreach(string text in new[]{"假設我叫測試使用者","我喜歡黑色介面開玩笑的","今天我喜歡黃色","我可能喜歡深色介面","他說我叫使用者","我喜歡這個嗎？","我喜歡蘋果。她喜歡香蕉","我叫神仙開玩笑","我希望你不要記住我叫某某","請把全文都記起來","我現在不開心"})
            Check(store.ObserveSelfStatement(text,now) is null,"skip chatter/example/uncertain/multi-person: "+text);
        bool conflict=false;try{store.ObserveSelfStatement("我的名字是另一個名字",now);}catch(InvalidOperationException){conflict=true;}Check(conflict&&store.Memories.Count(m=>m.Category=="身分資料")==1,"identity alias conflict does not overwrite");
        store.Handle("記住我的名字是新名稱",now);Check(store.Memories.Count(m=>m.Category=="身分資料")==1&&store.Memories.First(m=>m.Category=="身分資料").Id==identity!.Id,"explicit correction replaces same identity record");
        foreach(string text in new[]{"記住我的密碼是test-only","記住api key是test-only","記住我的地址是test-only"})
        {int before=store.Memories.Count;bool rejected=false;try{store.Handle(text,now);}catch(InvalidOperationException){rejected=true;}Check(rejected&&store.Memories.Count==before,"credentials/sensitive data not stored: "+text[..4]);}
        var memory=store.Memories.First(m=>m.Category=="喜好偏好");store.UpdateMemory(memory.Id,"回答方式","以後回答簡潔一點");Check(new PersonalAssistantStore(path).Memories.Any(m=>m.Id==memory.Id&&m.Category=="回答方式"),"edit text/category persists");
        store.DeleteMemory(memory.Id);Check(!new PersonalAssistantStore(path).Memories.Any(m=>m.Id==memory.Id),"individual memory deletion persists");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("新名稱"),"personal file encrypted for current Windows user");
        var history=new[]{new ChatMessage("user","哪款遊戲比較好？"),new ChatMessage("model","先確認類型。")};var enriched=store.WithMemory(history,"那還有什麼？");Check(enriched[0].text==history[0].text&&history[1].text=="先確認類型。"&&enriched[1].text.Contains("互動禁忌"),"context enrichment preserves search follow-up user and original history");
        Check(store.WithMemory(Array.Empty<ChatMessage>(),"你好").Count==2,"empty history becomes valid paired context");
        var session=new AssistantSession(Path.Combine(root,"chat.dpapi"));session.Append("記住我叫測試",new AssistantReply("已記住","local",null,false,null,null,null));session.Append("一般問題",new AssistantReply("一般回答","model",null,false,null,null,null));
        Check(session.Turns.Count==2&&session.ModelHistory().Count==2&&!session.ModelHistory().Any(m=>m.text.Contains("記住")),"local memory command remains visible but cannot reintroduce deleted memory into model history");
        Check(store.ObserveSelfStatement("我不希望 AI 要求我貼 API Key",now)?.Category=="互動禁忌","sensitive-data prohibition stored without storing a secret");
        Check(store.Handle("以後不要一直提醒我",now) is null&&store.ObserveSelfStatement("以後不要一直提醒我",now)?.Category=="互動禁忌","persistent reminder prohibition routed to memory, not reminder creation");
        Check(ReminderTime.Parse("明天上午九點提醒我不要遲到",now)?.Text=="不要遲到","negative reminder task retained verbatim");
        string corrupt=Path.Combine(root,"corrupt.dpapi");File.WriteAllText(corrupt,"retain");var broken=new PersonalAssistantStore(corrupt);bool blocked=false;try{broken.Handle("記住我叫測試",now);}catch(InvalidOperationException){blocked=true;}Check(blocked&&File.ReadAllText(corrupt)=="retain","corrupt store preserved and cannot be overwritten");
        string parent=Path.Combine(root,"file");File.WriteAllText(parent,"keep");var unavailable=new PersonalAssistantStore(Path.Combine(parent,"personal.dpapi"));bool failed=false;try{unavailable.Handle("明天上午九點提醒我開會",now);}catch(InvalidOperationException){failed=true;}Check(failed&&unavailable.Reminders.Count==0,"failed save never falsely acknowledges scheduled reminder");
        var capped=new PersonalAssistantStore(Path.Combine(root,"capacity.dpapi"));for(int i=0;i<300;i++)capped.Handle($"{i+1}分鐘後提醒我容量測試{i}",now);
        string oldest=capped.Reminders[0].Id,second=capped.Reminders[1].Id;capped.Handle("301分鐘後提醒我最新容量測試",now);
        Check(capped.Reminders.Count==300&&!capped.Reminders.Any(r=>r.Id==oldest)&&capped.Reminders[0].Id==second&&capped.Reminders[^1].Text=="最新容量測試","full reminders evict oldest created record in FIFO order");
        Check(!new PersonalAssistantStore(Path.Combine(root,"capacity.dpapi")).Due(now.AddDays(2)).Any(r=>r.Id==oldest),"evicted old pending reminder cancelled durably");
        Console.WriteLine($"{passed}/{passed} PASS; isolated DPAPI files, Chinese intents/time, no model calls.");
    }
}
