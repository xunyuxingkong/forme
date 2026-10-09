using Forme.Core;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;
using System.Text.Json;

int checks=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS "+name);checks++;}
void Throws(Action action,string name){try{action();}catch(Exception){Check(true,name);return;}throw new Exception("FAIL: expected rejection: "+name);}
async Task ThrowsAsync(Func<Task> action,string name){try{await action();}catch(Exception){Check(true,name);return;}throw new Exception("FAIL: expected rejection: "+name);}
string testRoot=Path.GetFullPath(Path.Combine("artifacts","test-data",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(testRoot);
try
{
    var motion=new PetMotion();
    var breathing=motion.Sample(1);
    Check(breathing.ScaleY>1&&breathing.Expression=="idle","idle breathing is local normalized pose");
    Check(motion.Sample(6.7).Blink&&!motion.Sample(6.9).Blink,"blink ends without renderer callbacks");
    motion.Play(PetAction.Pat,10);var pat=motion.Sample(10.45);
    Check(pat.Expression=="happy"&&pat.Lift>0,"pat produces bounded happy jump");
    motion.Play(PetAction.Rub,10.5);
    Check(motion.Sample(10.7).Blink&&motion.Sample(10.7).ScaleY<1,"new action replaces previous action");
    motion.Drag(true,11);motion.Play(PetAction.Pat,11.1);
    Check(motion.Sample(11.2).Expression=="thinking"&&motion.Reacting,"drag has priority over pat");
    motion.Drag(false,12);Check(motion.Sample(12.2).ScaleY<1,"release produces landing");
    Check(motion.Sample(12.6).Expression=="idle"&&!motion.Reacting,"finite action returns to base state");
    motion.State="focus";Check(!motion.Sample(6.7).Blink&&motion.Sample(5.5).Yaw==0,"focus excludes playful idle actions");
    motion.State="quiet";Check(motion.Sample(40)==PetPose.Neutral("quiet"),"quiet idle stays still");
    motion.Play(PetAction.Pat,41);motion.Reset();Check(!motion.Reacting&&motion.Sample(41.2)==PetPose.Neutral("quiet"),"suspend cancels pending actions");
    Throws(()=>motion.Sample(double.NaN),"invalid time rejected");
    Throws(()=>motion.Play(PetAction.Pat,double.PositiveInfinity),"invalid action start rejected");
    Throws(()=>motion.Play((PetAction)999,0),"unknown action rejected");
    var sparse=new PetMotion();var dense=new PetMotion();sparse.Play(PetAction.Pat,0);dense.Play(PetAction.Pat,0);
    for(int i=0;i<20;i++)dense.Sample(i*.02);
    Check(sparse.Sample(.45)==dense.Sample(.45),"action timing independent of frame frequency");
    foreach(var action in Enum.GetValues<PetAction>())
    {
        motion.State="idle";motion.Play(action,0);
        for(int i=0;i<=120;i++)
        {
            var pose=motion.Sample(i*.01);
            if(pose.ScaleX is <.85 or >1.15||pose.ScaleY is <.85 or >1.15||pose.Lift is <0 or >.08||Math.Abs(pose.Yaw)>10||Math.Abs(pose.Lean)>8)throw new Exception("Motion exceeded normalized bounds");
        }
    }
    Check(true,"all action samples stay inside motion bounds");
    double time=100;var clock=new FocusClock(()=>time);clock.Start("focus",5,"test");time+=31;
    Check(clock.Elapsed==31,"monotonic elapsed");clock.Pause();time+=400;Check(clock.Elapsed==31,"pause excludes absence");clock.Resume();time+=20;Check(clock.Elapsed==51,"resume uses fresh anchor");
    var snapshot=clock.Snapshot()!;var restored=new FocusClock(()=>time);restored.Restore(snapshot);time+=800;Check(!restored.Running&&restored.Elapsed==51,"crash recovery stays paused");clock.Pause();clock.Resume();time+=400;Check(clock.Complete&&clock.Elapsed==300,"completion clamps elapsed");Check(clock.Finish().Result=="completed","completed status");Throws(()=>clock.Start("focus",0,""),"invalid duration rejected");
    using var store=new Store(testRoot);store.SavePreferences(new(){PetName="测试伙伴",Endpoint="https://api.deepseek.com"});store.SaveActivity(snapshot);
    Check(store.Get<ActivitySnapshot>("activity")!.ElapsedSeconds==51,"checkpoint persistence");
    var day=new DateOnly(2026,10,9);Check(store.Water(day),"first watering");Check(!store.Water(day),"same-day watering deduplicated");
    for(int i=1;i<5;i++)store.Water(day.AddDays(i));Check(store.PlantPoints==5&&store.Unlocked("flower"),"plant maturity unlock");Check(!store.Water(day),"clock rollback cannot duplicate day");
    var entry=new FocusEntry("focus-test","focus","private task",DateTimeOffset.Now,300,300,"completed");store.FinishFocus(entry);store.FinishFocus(entry);
    Check(store.Focus().Count==1&&store.Unlocked("star"),"focus and rewards deduplicated transactionally");Check(store.Get<ActivitySnapshot>("activity") is null,"completion removes checkpoint");
    store.CompleteRelaxation();store.CompleteRelaxation();Check(store.Growth().Count(x=>x.Id=="unlock:cloud")==1,"relaxation reward deduplication");
    var mood=new MoodEntry("mood-test","低落","private mood",DateTimeOffset.Now,DateTimeOffset.Now);store.SaveMood(mood);store.SaveMood(mood with{Note="edited private mood"});Check(store.Moods().Single().Note=="edited private mood","mood edit preserves identity");
    var session=store.NewSession("https://api.deepseek.com");store.SaveMessage(new("u",session.Id,"user","hello","complete",DateTimeOffset.Now));store.SaveMessage(new("a",session.Id,"assistant","reply","streaming",DateTimeOffset.Now.AddMilliseconds(1)));
    Check(store.Messages(session.Id).Select(x=>x.Id).SequenceEqual(new[]{"u","a"}),"message ordering");Check(store.Messages(session.Id,0,1).Single().Id=="a","history pagination");
    using(var reader=new Store(testRoot,true)){Check(reader.Export(true,false,false,false).Messages!.Last().Status=="streaming","read-only export does not mutate active stream");}
    var export=store.Export(true,true,true,true);string json=JsonSerializer.Serialize(export);Check(!json.Contains("Endpoint\":\"https")||!json.Contains("secret"),"export contains no credential field");Check(!json.Contains("APIKey")&&!json.Contains("ReplyStyle"),"export excludes AI configuration");
    var subset=new ExportDocument{Moods=[new("other","平静","imported",DateTimeOffset.Now,DateTimeOffset.Now)]};store.Import(subset);
    Check(store.Moods().Single().Id=="other"&&store.Focus().Count==1&&store.Messages(session.Id).Count==2,"partial import preserves unrelated categories");Check(File.Exists(store.BackupPath),"pre-import recovery backup");Check(store.LoadPreferences().PetName=="测试伙伴","partial import preserves room and preferences");
    Throws(()=>store.Import(new(){SchemaVersion=999,Moods=[]}),"unsupported schema rejected before mutation");Check(store.Moods().Single().Id=="other","failed import preserves data");
    Throws(()=>store.Import(new(){Moods=[mood,mood]}),"duplicate import IDs rejected");store.DeleteMood("other");Check(!File.Exists(store.BackupPath),"deletion removes recovery backup");
    store.Import(export);Check(store.Moods().Single().Id=="mood-test"&&store.Messages(session.Id).Count==2&&store.Focus().Count==1,"full import restores all categories transactionally");Check(store.LoadPreferences().Endpoint=="https://api.deepseek.com"&&store.LoadPreferences().PetName=="测试伙伴","room import preserves connection preferences");Check(store.Messages(session.Id).Last().Status=="stopped","imported interrupted stream is not marked complete");
    store.DeleteSession(session.Id);store.SaveMessage(new("late",session.Id,"assistant","late content","complete",DateTimeOffset.Now));Check(store.Messages(session.Id).Count==0,"late callback cannot recreate deleted session");
    var p=new Preferences();var history=new List<ChatMessage>();
    for(int i=0;i<30;i++){history.Add(new("u"+i,"s","user",new string('中',80),"complete",DateTimeOffset.Now));history.Add(new("a"+i,"s","assistant",new string('答',80),"complete",DateTimeOffset.Now));}
    var turns=AiClient.BuildContext(p,history,"你好",out bool trimmed);Check(trimmed&&turns.Sum(AiClient.Estimate)<=4096,"bounded context trims oldest exchanges");Check(turns[0].Role=="system"&&turns[^1].Content=="你好","current message retained");Check(!turns.Any(x=>x.Content.Contains("private mood")||x.Content.Contains("private task")),"mood and focus not implicitly shared");
    Throws(()=>AiClient.BuildContext(p,[],new string('x',2001),out _),"character limit enforced");Throws(()=>AiClient.BuildContext(p,[],new string('中',2000),out _),"oversized UTF8 budget blocked before request");
    var retryHistory=new List<ChatMessage>{new("u","s","user","question","complete",DateTimeOffset.Now),new("a","s","assistant","bad partial","error",DateTimeOffset.Now),new("b","s","assistant","good retry","complete",DateTimeOffset.Now)};
    turns=AiClient.BuildContext(p,retryHistory,"next",out _);Check(turns.Any(x=>x.Content=="good retry")&&!turns.Any(x=>x.Content=="bad partial"),"successful retry context excludes failed partial");
    Throws(()=>AiClient.ValidateEndpoint("http://example.com"),"remote plaintext blocked");Throws(()=>AiClient.ValidateEndpoint("https://key:secret@example.com"),"URL credentials blocked");Throws(()=>AiClient.ValidateEndpoint("https://example.com?key=abc"),"query credentials blocked");Check(AiClient.ValidateEndpoint("http://127.0.0.1:1234/v1").IsLoopback,"local HTTP allowed");
    string stream="data: {\"choices\":[{\"delta\":{\"content\":\"你好\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"呀\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n";
    var handler=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(stream,Encoding.UTF8,"text/event-stream")}));
    using(var ai=new AiClient(handler))
    {
        string progress="";var result=await ai.SendAsync(p,"fake-key",[new("user","hello")],s=>progress=s,CancellationToken.None);
        Check(result.Content=="你好呀"&&progress==result.Content&&result.LengthLimited,"SSE and output-limit feedback");Check(handler.Calls==1,"one action one network request");
        using var payload=JsonDocument.Parse(handler.Body!);Check(payload.RootElement.GetProperty("max_tokens").GetInt32()==512&&payload.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","output limit and non-thinking mode");Check(!handler.Body!.Contains("fake-key"),"key is not in request body");
    }
    var redirect=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)));
    using(var ai=new AiClient(redirect)){await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","hello")],_=>{},CancellationToken.None),"redirect rejected");Check(redirect.Calls==1,"no redirect replay");}
    var slow=new FakeHandler(async(_,token)=>{await Task.Delay(5000,token);return new(HttpStatusCode.OK);});
    using(var ai=new AiClient(slow,new(TimeSpan.FromMilliseconds(80),TimeSpan.FromMilliseconds(80),TimeSpan.FromMilliseconds(300)))){await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","hello")],_=>{},CancellationToken.None),"first content timeout");Check(slow.Calls==1,"timeout never retries");}
    var stallStream=new StallStream(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"));
    var stallHandler=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stallStream)}));
    using(var ai=new AiClient(stallHandler,new(TimeSpan.FromMilliseconds(200),TimeSpan.FromMilliseconds(80),TimeSpan.FromMilliseconds(400)))){string partial="";await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","hello")],s=>partial=s,CancellationToken.None),"streaming idle timeout");Check(partial=="partial"&&stallHandler.Calls==1,"partial content preserved without retries");}
    var invalid=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: not-json\n\n")}));
    using(var ai=new AiClient(invalid)){await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","hello")],_=>{},CancellationToken.None),"invalid SSE fails visibly");Check(invalid.Calls==1,"invalid protocol is not retried");}
    var unauthorized=new FakeHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
    using(var ai=new AiClient(unauthorized)){await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","hello")],_=>{},CancellationToken.None),"authentication failure");Check(unauthorized.Calls==1,"auth failure is not retried");}
    var concurrent=new FakeHandler(async(_,token)=>{await Task.Delay(5000,token);return new(HttpStatusCode.OK);});
    using(var ai=new AiClient(concurrent)){using var cancel=new CancellationTokenSource();var first=ai.SendAsync(p,"fake",[new("user","first")],_=>{},cancel.Token);await Task.Delay(20);await ThrowsAsync(()=>ai.SendAsync(p,"fake",[new("user","second")],_=>{},CancellationToken.None),"concurrent request blocked");cancel.Cancel();await ThrowsAsync(()=>first,"cancellation completes request");Check(concurrent.Calls==1,"shared gate avoids duplicate requests");}
    store.ResetAll();Check(store.Moods().Count==0&&store.Focus().Count==0&&store.Sessions().Count==0&&store.Growth().Count==0,"reset all categories");
    string future=Path.Combine(testRoot,"future");Directory.CreateDirectory(future);using(var db=new SqliteConnection("Data Source="+Path.Combine(future,"forme.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="PRAGMA user_version=99";c.ExecuteNonQuery();}Throws(()=>{using var rejected=new Store(future);},"newer database version refused safely");
    Console.WriteLine($"\n{checks} checks passed. Test data: {testRoot}");
    Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/core-tests.txt",$"{checks} checks passed at {DateTimeOffset.Now:O}\n");
}
catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}

sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
{
    public int Calls {get;private set;}public string? Body {get;private set;}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {Calls++;Body=await request.Content!.ReadAsStringAsync(cancellationToken);return await reply(request,cancellationToken);}
}
sealed class StallStream(byte[] initial):Stream
{
    private int _position;
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>initial.Length;public override long Position{get=>_position;set=>throw new NotSupportedException();}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
    {
        if(_position<initial.Length){int count=Math.Min(buffer.Length,initial.Length-_position);initial.AsMemory(_position,count).CopyTo(buffer);_position+=count;return count;}
        await Task.Delay(Timeout.Infinite,token);return 0;
    }
    public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
