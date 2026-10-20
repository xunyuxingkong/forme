using Forme.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Forme.App;

internal static class CompanionChecks
{
    public static async Task ChatCleanup(DesktopHost host,Controller c)
    {
        c.NewSession();var now=DateTimeOffset.Now;
        var upload=new ChatMessage("presentation-user",c.Session!.Id,"user","这是什么？\n\n[本次附件：sample (2).jpg；原内容未保存，后续消息不自动重传]","attachment",now);
        var quoted=upload with{Status="complete"};var legacy=upload with{Content="这是什么？\n[本次附件：sample.jpg；原内容未保存]"};
        var error=new ChatMessage("presentation-error",c.Session.Id,"assistant","网络或 TLS 连接失败，请检查网络和服务地址。","error",now.AddTicks(1));
        if(ChatDisplay.RecordMessage(upload)!="这是什么？"||ChatDisplay.RecordMessage(legacy)!="这是什么？"||ChatDisplay.RecordMessage(quoted)!=quoted.Content||ChatDisplay.RecordMessage(error).Length!=0||ChatDisplay.RecordMessage(error with{Status="complete"})!=error.Content)throw new Exception("Chat cleanup removed real text or retained technical records");
        if(ChatDisplay.Status("回复完成；上下文已按预算裁剪。").Length!=0||ChatDisplay.Status(error.Content)!=error.Content||ChatDisplay.Message("正在理解动作请求…")!="正在识别宠物动作…")throw new Exception("Chat cleanup hid an actionable failure or exposed internal progress");
        if(ChatDisplay.RecordMessage(error with{Content="服务返回 HTTP 503，请检查连接或模型设置。"}).Length!=0||ChatDisplay.RecordMessage(error with{Status="complete",Content="服务返回 HTTP 503，请检查连接或模型设置。"})!="服务返回 HTTP 503，请检查连接或模型设置。"||ChatDisplay.RecordMessage(error with{Status="local",Content="本地规则匹配 · 未联网\n已走到书柜旁。"})!="已走到书柜旁。")throw new Exception("Legacy technical cleanup swallowed an actual reply or retained operation headers");
        c.Store.SaveMessage(upload);c.Store.SaveMessage(error);c.Store.SaveMessage(new("presentation-reply",c.Session.Id,"assistant","我在这里，听你说。","complete",now.AddTicks(2)));c.Refresh();
        if(ChatTranscript.Text(c.Store.Messages(c.Session.Id),c.Preferences.PetName,null).Contains("原内容未保存")||c.Store.Messages(c.Session.Id).First().Content!=upload.Content)throw new Exception("Transcript copy leaked metadata or mutated stored record");
        void Check(Window window)
        {
            window.UpdateLayout();var transcript=Descendants(window).OfType<ScrollViewer>().Single(x=>x.Name=="聊天记录滚动区");var texts=Descendants(transcript).OfType<TextBlock>().Select(x=>x.Text).ToArray();
            if(!texts.Contains("这是什么？")||!texts.Contains("我在这里，听你说。")||texts.Any(x=>x.Contains("原内容未保存")||x.Contains("网络或 TLS")||x=="未收到内容"||x.Contains("自由对话需在设置连接AI")))throw new Exception("Rendered chat contains technical records or omitted real messages");
        }
        Check(host.House!);Image(host.House!,"chat-cleanup");host.House!.Close();host.ShowFloatingChat();await Task.Delay(80);Check(host.FloatingChat!);Image(host.FloatingChat!,"floating-chat-cleanup");Click(host.FloatingChat!,"完整聊天");
        var handler=new OfflineReplyHandler{Status=HttpStatusCode.ServiceUnavailable};using var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"presentation-protocol"),new AiClient(handler));protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true});protocol.Draft="你去有水的地方走走";await protocol.Send();
        if(protocol.Store.Messages(protocol.Session!.Id).Last().Content.Length!=0||!protocol.ChatStatus.Contains("HTTP 503")||protocol.ActionResults.Count!=0)throw new Exception("Service diagnostic became a pet reply or was silently lost");
        using(var latency=new Controller(Path.Combine(c.Store.DirectoryPath,"stream-latency"),new AiClient(new LatencyReplyHandler(),new(TimeSpan.FromMilliseconds(200),TimeSpan.FromSeconds(3),TimeSpan.FromSeconds(5)))))
        {
            latency.Secrets.Save("offline-test-key");latency.Draft="你好";var sending=latency.Send();await Task.Delay(350);
            if(!latency.Busy||!latency.ChatStatus.Contains("正在思考")||latency.LiveReply.Length!=0||latency.Store.Messages(latency.Session!.Id).Any(message=>message.Content.Contains("PRIVATE_REASONING")))throw new Exception("Aligned reasoning stream did not update controller status privately before the final reply");
            await sending;if(latency.Store.Messages(latency.Session!.Id).Last().Content!="及时回复"||latency.ActionResults.Count!=0)throw new Exception("Delayed streaming reply was lost or executed an action");
        }
        File.WriteAllText("artifacts/chat-cleanup-smoke-result.txt","PASS: exact generated attachment suffix hidden in both chats and transcript copy; quoted user text and actual replies retained; stored metadata unchanged; empty/error/protocol progress rows cleaned; failures remain in operation status. Offline mock only.\n");
    }

    public static async Task DesktopVisibility(DesktopHost host,Controller c)
    {
        var house=host.House!;host.ShowPet();var pet=host.Pet!;var animation=Descendants(pet).OfType<Pet3DView>().Single();
        if(!house.IsVisible||!pet.IsVisible||c.CurrentScene?.Invoke()!=house.CompanionScene)throw new Exception("House and desktop pet do not coexist or action context changed");
        void Toggle(string title)
        {
            var current=host.House!;Click(current,"伙伴控制");var button=Descendants(current).OfType<Button>().Single(x=>Equals(x.Content,"伙伴控制"));var menu=button.ContextMenu!;
            var item=menu.Items.OfType<MenuItem>().Single(x=>Equals(x.Header,title));item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));menu.IsOpen=false;
        }
        Toggle("隐藏桌面伙伴");if(host.House!=house||!house.IsVisible||host.Pet is not null||c.Store.LoadPreferences().DisplayMode!="tray"||animation.AnimationRunning)throw new Exception("Hiding desktop pet closed the house, lost the preference, or kept rendering");
        host.ShowHouse("chat");if(host.Pet is not null)throw new Exception("Opening the house ignored hidden preference");
        house.Close();if(host.Pet is not null)throw new Exception("Closing the house resurrected hidden desktop pet");
        host.ShowHouse("chat");Toggle("显示桌面伙伴");var shown=host.Pet!;var reopened=host.House!;
        if(!shown.IsVisible||!reopened.IsVisible||c.Store.LoadPreferences().DisplayMode!="pet")throw new Exception("Showing desktop pet closed house or lost display preference");
        host.ShowPet();host.ShowHouse("chat");if(host.Pet!=shown||host.House!=reopened||Application.Current.Windows.OfType<PetWindow>().Count()!=1)throw new Exception("Repeated show duplicated desktop or house windows");
        reopened.Close();if(host.Pet!=shown||!shown.IsVisible||c.CurrentScene?.Invoke()!=SceneKind.Desktop)throw new Exception("Closing house replaced or hid visible desktop pet");
        host.ShowHouse("chat");await Task.Delay(80);if(host.Pet!=shown||!host.House!.IsVisible)throw new Exception("Reopening house replaced desktop pet");
        File.WriteAllText("artifacts/desktop-visibility-smoke-result.txt","PASS: house and desktop coexist; house action context preserved; hide keeps house open, releases desktop animation and persists tray preference; closing/reopening respects hide; show/close/reopen keeps one desktop instance. No network calls.\n");
    }

    public static async Task Attachments(DesktopHost host,Controller c)
    {
        var before=c.Preferences;
        byte[] image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1sAAAAASUVORK5CYII=");
        var picture=AiAttachment.FromBytes("sample.png",image);
        var pdf=AiAttachment.FromBytes("sample.pdf",Encoding.ASCII.GetBytes("%PDF-1.7\nmock"));
        var text=AiAttachment.FromBytes("note.txt",Encoding.UTF8.GetBytes("ATTACHMENT_PRIVATE_BODY"));
        try
        {
            host.HidePet();host.ShowPet();await Task.Delay(80);host.ShowFloatingChat();c.AddAttachments([picture]);host.FloatingChat!.UpdateLayout();
            if(!Descendants(host.FloatingChat!).OfType<Button>().Any(b=>Equals(b.Content,"sample.png")))throw new Exception("Floating attachment list missing");
            Click(host.FloatingChat!,"移除");if(c.Attachments.Count!=0)throw new Exception("Attachment remove failed");
            c.AddAttachments([picture,pdf,text]);Click(host.FloatingChat!,"完整聊天");await Task.Delay(80);c.SavePreferences(c.Preferences with{ShowHome3D=false});host.House!.Width=1080;host.House.Height=685;host.House.UpdateLayout();
            if(c.Attachments.Count!=3||!Descendants(host.House).OfType<Button>().Any(b=>Equals(b.Content,"note.txt")))throw new Exception("Attachment draft lost on chat transfer");
            var input=Descendants(host.House).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="聊天消息");var history=Descendants(host.House).OfType<ScrollViewer>().Single(x=>x.Name=="聊天记录滚动区");
            if(history.ActualHeight<60||!input.IsVisible)throw new Exception("Attachments hid chat composer or history");Image(host.House,"chat-attachments");
            c.NewSession();if(c.Attachments.Count!=0)throw new Exception("New session retained attachments");

            var handler=new OfflineReplyHandler{Reply="收到附件"};string directory=Path.Combine(c.Store.DirectoryPath,"attachments");
            var siliconHandler=new OfflineReplyHandler{Reply="连接成功"};using(var silicon=new Controller(Path.Combine(directory,"silicon-glm"),new AiClient(siliconHandler)))
            {
                var initial=silicon.Preferences;await silicon.TestConnection("https://api.siliconflow.cn/v1","zai-org/GLM-5.3-Flash","offline-test-key","high");
                using var request=JsonDocument.Parse(siliconHandler.LastRequest);var root=request.RootElement;
                if(root.GetProperty("max_tokens").GetInt32()!=initial.MaxReplyTokens||root.GetProperty("thinking_budget").GetInt32()!=1024||!root.GetProperty("enable_thinking").GetBoolean()||root.TryGetProperty("reasoning_effort",out _)||silicon.Preferences!=initial||silicon.Secrets.Exists)throw new Exception("SiliconFlow GLM connection test lost draft effort, used fixed 128 tokens, saved implicitly, or sent unsupported effort");
            }
            using var protocol=new Controller(directory,new AiClient(handler));protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{Endpoint="https://example.com/v1",AllowPetControl=true,AllowSceneControl=true});int actions=0;
            protocol.ActionHandler=cmd=>{actions++;return new(ActionResultCode.Completed,cmd.Action,cmd.Value,"动作",TimeSpan.Zero);};protocol.AddAttachments([picture,pdf,text]);protocol.Draft="去池塘边";
            if(await protocol.TrySendLocal())throw new Exception("Attachment entered local action route");await protocol.Send();
            using(var request=JsonDocument.Parse(handler.LastRequest))if(request.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetArrayLength()!=3)throw new Exception("Controller did not send multimodal body");
            var saved=protocol.Store.Messages(protocol.Session!.Id);
            if(actions!=0||protocol.Attachments.Count!=0||saved.Any(x=>x.Content.Contains("ATTACHMENT_PRIVATE_BODY")||x.Content.Contains("base64,")))throw new Exception("Attachment executed actions or leaked contents into database");
            handler.Status=HttpStatusCode.BadRequest;protocol.AddAttachments([picture]);protocol.Draft="看图片";await protocol.Send();
            string userId=protocol.Store.Messages(protocol.Session.Id).Last(x=>x.Role=="user").Id;
            handler.Status=HttpStatusCode.OK;await protocol.Send(userId);
            if(!handler.LastRequest.Contains("image_url")||handler.Calls!=3)throw new Exception("Manual retry omitted attachment or repeated automatically");
            protocol.Draft="下一条普通消息";await protocol.Send();if(handler.LastRequest.Contains("image_url")||handler.LastRequest.Contains("ATTACHMENT_PRIVATE_BODY"))throw new Exception("Attachments silently entered later context");
            protocol.AddAttachments([picture]);protocol.NewSession();if(protocol.Attachments.Count!=0)throw new Exception("New session retained file bytes");
            string uploadDir=Path.Combine(directory,"selected");Directory.CreateDirectory(uploadDir);string imagePath=Path.Combine(uploadDir,"selected.png"),badPath=Path.Combine(uploadDir,"bad.exe");File.WriteAllBytes(imagePath,image);File.WriteAllBytes(badPath,[1]);
            try{await protocol.AddAttachmentFiles([imagePath,badPath]);throw new Exception("Invalid batch accepted");}catch(InvalidDataException){}if(protocol.Attachments.Count!=0)throw new Exception("Invalid file batch partially changed draft");
            var reading=protocol.AddAttachmentFiles([imagePath]);protocol.Cancel();await reading;if(protocol.Busy||protocol.Attachments.Count!=0)throw new Exception("Attachment loading cancellation failed");
            handler.Status=HttpStatusCode.BadRequest;protocol.AddAttachments([picture]);protocol.Draft="再次看图片";await protocol.Send();userId=protocol.Store.Messages(protocol.Session!.Id).Last(x=>x.Role=="user").Id;
            using var restart=new Controller(directory,new AiClient(new OfflineReplyHandler()));
            try{await restart.Send(userId);throw new Exception("Restart silently retried without original file");}catch(OperationFailureException ex){if(!ex.Message.Contains("重新添加"))throw;}
            var deepseekHandler=new OfflineReplyHandler{Reply="收到PDF页面"};using var deepseek=new Controller(Path.Combine(directory,"deepseek"),new AiClient(deepseekHandler));deepseek.Secrets.Save("offline-test-key");deepseek.SavePreferences(deepseek.Preferences with{Model="deepseek-v4-flash"});
            string pdfPath=Path.Combine(uploadDir,"two-pages.pdf");File.WriteAllBytes(pdfPath,PdfFixture(2));File.WriteAllBytes("artifacts/pdf-worker-fixture.pdf",PdfFixture(2));
            var timer=System.Diagnostics.Stopwatch.StartNew();await deepseek.AddAttachmentFiles([pdfPath]);timer.Stop();
            var converted=deepseek.Attachments.Single();if(!converted.IsRenderedPdf||converted.Pages.Count!=2||converted.DataUrl is not null)throw new Exception("Native PDF conversion did not produce all pages");
            string pageData=converted.Pages[0].DataUrl!;File.WriteAllBytes("artifacts/screenshots/pdf-local-page.png",Convert.FromBase64String(pageData[(pageData.IndexOf(',')+1)..]));
            deepseek.Draft="读这个PDF";await deepseek.Send();
            using(var request=JsonDocument.Parse(deepseekHandler.LastRequest))
            {
                var root=request.RootElement;var parts=root.GetProperty("messages").EnumerateArray().Last().GetProperty("content");
                if(root.GetProperty("model").GetString()!="deepseek-v4-flash"||parts.GetArrayLength()!=3||parts.EnumerateArray().Skip(1).Any(x=>x.GetProperty("type").GetString()!="image_url")||deepseekHandler.LastRequest.Contains("application/pdf;base64"))throw new Exception("DeepSeek PDF wire format or model changed");
                for(int page=0;page<2;page++)if(parts[page+1].GetProperty("image_url").GetProperty("url").GetString()!=converted.Pages[page].DataUrl)throw new Exception("PDF page order changed");
            }
            File.WriteAllBytes(pdfPath,PdfFixture(5));try{await deepseek.AddAttachmentFiles([pdfPath]);throw new Exception("Long PDF silently truncated");}catch(InvalidDataException ex){if(!ex.Message.Contains("拆分"))throw;}if(deepseek.Attachments.Count!=0)throw new Exception("Rejected PDF added partial pages");
            File.WriteAllBytes(pdfPath,PdfFixture(1,8000,6000));await deepseek.AddAttachmentFiles([pdfPath]);
            var giant=deepseek.Attachments.Single();string giantData=giant.Pages.Single().DataUrl!;
            using(var pixels=new MemoryStream(Convert.FromBase64String(giantData[(giantData.IndexOf(',')+1)..])))
            {var frame=new PngBitmapDecoder(pixels,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];if(frame.PixelWidth>1536||frame.PixelHeight>1536)throw new Exception("PDF raster dimensions exceeded bound");}
            deepseek.RemoveAttachment(giant);
            File.WriteAllBytes(pdfPath,Encoding.ASCII.GetBytes("%PDF-1.7 malformed"));try{await deepseek.AddAttachmentFiles([pdfPath]);throw new Exception("Malformed PDF accepted");}catch(Exception ex) when(ex is InvalidDataException or TimeoutException){}if(deepseek.Attachments.Count!=0)throw new Exception("Malformed PDF added pages");
            var stallSource=AiAttachment.FromBytes("stall.pdf",PdfFixture(1));PdfAttachments.StallWorkerForSmoke=true;
            try
            {
                try{await PdfAttachments.Render(stallSource,CancellationToken.None,TimeSpan.FromMilliseconds(300));throw new Exception("Stalled PDF worker did not time out");}catch(TimeoutException){}
                if(!PdfAttachments.LastWorkerExitedForSmoke)throw new Exception("Timed-out PDF worker survived");
                using var stopped=new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                try{await PdfAttachments.Render(stallSource,stopped.Token);throw new Exception("Stalled PDF worker ignored cancellation");}catch(OperationCanceledException){}
                if(!PdfAttachments.LastWorkerExitedForSmoke)throw new Exception("Cancelled PDF worker survived");
            }
            finally{PdfAttachments.StallWorkerForSmoke=false;}
            File.WriteAllBytes(pdfPath,PdfFixture(1));var converting=deepseek.AddAttachmentFiles([pdfPath]);deepseek.Cancel();await converting;if(deepseek.Attachments.Count!=0||deepseek.Busy)throw new Exception("PDF cancellation failed");
            File.WriteAllText("artifacts/pdf-attachments-smoke-result.txt",$"PASS: isolated Windows PDF rendering; all 2 pages sent as ordered image_url parts to deepseek-v4-flash; raw PDF absent; 5-page PDF rejected without partial draft; large page raster <=1536 px; malformed PDF rejected; stalled worker timeout/cancellation terminates worker; atomicity. Two-page conversion {timer.Elapsed.TotalMilliseconds:0} ms on this machine; source {PdfFixture(2).Length} bytes, bounded payload {converted.ByteCount} bytes. Mock network only.\n");
            File.WriteAllText("artifacts/attachments-smoke-result.txt","PASS: both composers attach/remove/share draft; compact layout; multipart image/PDF plus extracted text; attachment input cannot trigger local/AI actions; no file body in database or later history; manual retry preserves in-memory attachment, restart requires reselection; invalid batch atomicity and cancellation; real local PDF rendering for DeepSeek with page-limit/atomicity/cancellation checks. Mock only, no external calls.\n");
        }
        finally{if(!c.Busy)c.NewSession();c.SavePreferences(before);host.ShowHouse("home");}
    }
    private static byte[] PdfFixture(int pages,int width=300,int height=200)
    {
        var objects=new List<string>{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Count "+pages+" /Kids ["+string.Join(' ',Enumerable.Range(0,pages).Select(i=>$"{4+i*2} 0 R"))+"] >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};
        for(int i=0;i<pages;i++)
        {
            string content=$"BT /F1 24 Tf 40 150 Td (FORME PDF PAGE {i+1}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /Font << /F1 3 0 R >> >> /Contents {5+i*2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }
        var pdf=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};
        for(int i=0;i<objects.Count;i++){offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));pdf.Append($"{i+1} 0 obj\n{objects[i]}\nendobj\n");}
        int xref=Encoding.ASCII.GetByteCount(pdf.ToString());pdf.Append($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n");
        foreach(int offset in offsets.Skip(1))pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count+1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");return Encoding.ASCII.GetBytes(pdf.ToString());
    }
    public static async Task IntentPipeline(DesktopHost host,Controller c)
    {
        var before=c.Preferences;var world=c.Store.World();
        try
        {
            c.SavePreferences(before with{LocalActionRules=true,AllowPetControl=true,AllowSceneControl=true,ReducedMotion=true,ShowHome3D=true});
            host.ShowHouse("home");await Task.Delay(100);host.House!.SwitchWorld(false);
            c.SavePreferences(c.Preferences with{AllowSceneControl=false});c.Draft="去水边";
            if(!await c.TrySendLocal()||c.ActionResults.Single().Code!=ActionResultCode.Unauthorized||host.House.CompanionScene!=SceneKind.Indoor)throw new Exception("Intent preflight half-executed a denied plan");
            c.SavePreferences(c.Preferences with{AllowSceneControl=true});c.Draft="团团你去水边溜达一下吧";
            if(!await c.TrySendLocal()||c.ActionResults.Last().Code!=ActionResultCode.Completed||host.House.CompanionScene!=SceneKind.Outdoor)throw new Exception("Pond intent did not switch and arrive: "+c.ChatStatus);
            var room=Descendants(host.House).OfType<Room3DView>().Single();var pond=TargetCatalog.Find("pond")!.ApproachPoints[0];
            if(room.PetPosition!=pond||c.ChatStatus.Contains(CompanionCommands.ReportMarker)||c.ChatStatus.Contains("JSON"))throw new Exception("Intent movement or visible result was wrong");
            host.House.Navigate("chat");Image(host.House,"intent-pond-result");
            c.Draft="走到书柜那里";if(!await c.TrySendLocal()||host.House.CompanionScene!=SceneKind.Indoor||c.ActionResults.Last().Code!=ActionResultCode.Completed)throw new Exception("Book intent did not arrive");
            foreach(string text in new[]{"别去池塘","如果让你散步会怎样","解释一下去池塘边"}){c.Draft=text;if(await c.TrySendLocal())throw new Exception("Non-command became an action");}
            var partial=world.Copy();partial.Items.RemoveAll(x=>x.Kind=="book");c.Store.SaveWorld(partial);c.Refresh();host.House.SwitchWorld(true);c.Draft="到看书的地方";
            if(!await c.TrySendLocal()||c.ActionResults.Single().Code!=ActionResultCode.TargetUnavailable||host.House.CompanionScene!=SceneKind.Outdoor)throw new Exception("Missing target changed scene before failing");
            c.Store.SaveWorld(world);c.Refresh();host.HidePet();host.ShowPet();await Task.Delay(50);host.ShowFloatingChat();c.Draft="到水边";
            if(!await c.TrySendLocal()||host.House?.CompanionScene!=SceneKind.Outdoor||c.ActionResults.Last().Code!=ActionResultCode.Completed)throw new Exception("Floating chat target plan failed");
            host.HidePet();c.Draft="去书柜旁";if(!await c.TrySendLocal()||host.House?.CompanionScene!=SceneKind.Indoor)throw new Exception("Tray target plan failed");
            var handler=new OfflineReplyHandler{Reply="我已经到了池塘边。"};
            using var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"intent-protocol"),new AiClient(handler));
            protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true,AllowSceneControl=true});int executed=0;
            protocol.ActionHandler=cmd=>{executed++;return new(ActionResultCode.Completed,cmd.Action,cmd.Value,"完成",TimeSpan.Zero);};protocol.Draft="去有水的地方待会吧";
            await protocol.Send();if(executed!=0||protocol.ActionResults.Count!=0||protocol.Store.Messages(protocol.Session!.Id).Last().Content.Contains("我已经到了"))throw new Exception("Unbacked model promise reached the transcript");
            handler.Reply="```forme-actions\n[{\"type\":\"GoTo\",\"target\":\"pond\"}]\n```";protocol.Draft="去有水的地方待会吧";await protocol.Send();if(executed!=2||handler.Calls!=2)throw new Exception("AI intent fallback was not one request and one bounded plan");
            protocol.SavePreferences(protocol.Preferences with{LocalActionRules=false});handler.Reply="```forme-actions\n[{\"type\":\"ChangeScene\",\"target\":\"indoor\"},{\"type\":\"Stroll\",\"mode\":\"walk\"},{\"type\":\"GoTo\",\"target\":\"book\"}]\n```";protocol.Draft="在小屋走一圈，然后去书架旁边";await protocol.Send();
            using var routedRequest=JsonDocument.Parse(handler.LastRequest);string routedText=string.Join("\n",routedRequest.RootElement.GetProperty("messages").EnumerateArray().Select(x=>x.GetProperty("content").GetString()));
            if(handler.Calls!=3||executed!=5||protocol.ActionResults.Count!=3||protocol.ActionResults.Any(x=>!x.Success)||!routedText.Contains("在小屋走一圈，然后去书架旁边",StringComparison.Ordinal)||!routedText.Contains("view=room/desktop",StringComparison.Ordinal))
            {
                string actionResults=string.Join(" | ",protocol.ActionResults.Select(x=>$"{x.Code}: {x.UserMessage}"));
                throw new Exception($"AI routing did not infer and execute the house walk plus book target: calls={handler.Calls}, executed={executed}, results={protocol.ActionResults.Count} [{actionResults}], requestInput={routedText.Contains("在小屋走一圈，然后去书架旁边",StringComparison.Ordinal)}, requestPrompt={routedText.Contains("view=room/desktop",StringComparison.Ordinal)}");
            }
            handler.Reply="```json\n{\"type\":\"forme-actions\",\"commands\":[{\"action\":\"Stroll\",\"mode\":\"run\",\"count\":2}]}\n```";
            protocol.ActionHandler=cmd=>{executed++;return new(ActionResultCode.Started,cmd.Action,cmd.Value,"动作已开始",TimeSpan.Zero);};protocol.CommandWaiter=(_,_)=>Task.FromResult(true);
            protocol.Draft="在屏幕上跑2圈看看";await protocol.Send();
            if(handler.Calls!=4||executed!=6||protocol.ActionResults.Single().Code!=ActionResultCode.Completed||protocol.Store.Messages(protocol.Session!.Id).Last().Content!="好呀～我在屏幕上绕了两圈，回来啦！")
                throw new Exception("Alternate provider action format did not execute or return an in-character completion reply");
            handler.Reply="```json\n{\"type\":\"forme-actions\",\"commands\":[{\"action\":\"Fly\"}]}\n```";
            protocol.Draft="飞到月球";await protocol.Send();
            if(handler.Calls!=5||executed!=6||!protocol.Store.Messages(protocol.Session!.Id).Last().Content.Contains("暂时做不了",StringComparison.Ordinal)||protocol.Store.Messages(protocol.Session.Id).Last().Content.Contains("forme-actions",StringComparison.Ordinal))
                throw new Exception("Unsupported action was not safely rejected with a character-voice response");
            handler.Reply="我们可以聊聊你今天的感受。";protocol.Store.Set("ai-consent-v3-productivity",protocol.Preferences.Endpoint);protocol.Draft="谢谢你";await ChatRequests.Send(protocol);
            if(handler.Calls!=6||executed!=6||!protocol.Store.Messages(protocol.Session!.Id).Last().Content.Contains("我们可以聊聊",StringComparison.Ordinal)||protocol.Store.Get<string>("ai-consent-v3-productivity")!=protocol.Preferences.Endpoint)throw new Exception("Productivity consent did not preserve ordinary chat");
            protocol.Draft="取消";await ChatRequests.Send(protocol);if(handler.Calls!=6||executed!=7||protocol.ActionResults.Single().Code!=ActionResultCode.Completed)throw new Exception("Local cancel fast path called the AI service");
            protocol.Draft="回来";await ChatRequests.Send(protocol);if(handler.Calls!=6||executed!=8||protocol.ActionResults.Single().Code!=ActionResultCode.Completed)throw new Exception("Local recall fast path called the AI service");
            protocol.SavePreferences(protocol.Preferences with{LocalActionRules=true});protocol.SaveRules(new ActionRules(1,[new(["专属快捷回到我身边"],[new("recall")])]).Serialize());protocol.Draft="专属快捷回到我身边";await ChatRequests.Send(protocol);
            if(handler.Calls!=6||executed!=9||protocol.ActionResults.Single().Code!=ActionResultCode.Completed)throw new Exception("Explicit custom shortcut was not handled locally");
            var productivityHandler=new OfflineReplyHandler{Reply="```forme-productivity\n[{\"intent\":\"CreateNote\",\"title\":\"本地隐私样例\",\"content\":\"只应保存在本机的正文\",\"tags\":[\"smoke\"]}]\n```"};
            using(var productivity=new Controller(Path.Combine(c.Store.DirectoryPath,"productivity-protocol"),new AiClient(productivityHandler)))
            {
                productivity.Secrets.Save("offline-test-key");productivity.SavePreferences(productivity.Preferences with{LocalActionRules=false});productivity.Draft="帮我记一下：只应保存在本机的正文";await productivity.Send();
                var session=productivity.Session!;var messages=productivity.Store.Messages(session.Id);
                if(productivity.Store.UserNotes(search:"本地隐私样例").Single().Content!="只应保存在本机的正文"||messages.Any(x=>x.Status!="local")||productivity.Store.ContextMessages(session.Id).Any(x=>x.Content.Contains("本地隐私样例")||x.Content.Contains("只应保存在本机的正文")))throw new Exception("Productivity creation did not isolate local content from future AI context");
                productivityHandler.Reply="好的。";productivity.Draft="你好";await productivity.Send();
                using var request=JsonDocument.Parse(productivityHandler.LastRequest);string requestContent=request.RootElement.GetProperty("messages").ToString();if(requestContent.Contains("只应保存在本机的正文")||requestContent.Contains("本地隐私样例"))throw new Exception("Private productivity data leaked into a later AI request");
            }
            File.WriteAllText("artifacts/intent-smoke-result.txt","PASS: AI routes normal text as ordinary chat or a bounded marked action plan; stop/cancel, recall and explicit custom shortcuts use the local fast path; compound house walk and book target execute from AI output; whole-plan permission/target preflight; actual arrival; negative/discussion suppression; floating chat and tray; malformed AI promise rejected; one offline mock request for each AI-routed message.\n");
        }
        finally{c.Store.SaveWorld(world);c.SavePreferences(before);c.Draft="";host.ShowHouse("home");host.House!.SwitchWorld(false);}
    }
    private sealed class LatencyReplyHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new LatencyReplyStream())});
    }
    private sealed class LatencyReplyStream:Stream
    {
        private int _stage;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(_stage>=2)return 0;
            const string thought="data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n\n";
            byte[] bytes;if(_stage++==0)bytes=Encoding.UTF8.GetBytes(thought+":"+new string(' ',1024-Encoding.UTF8.GetByteCount(thought)-3)+"\n\n");
            else{await Task.Delay(1500,token);bytes=Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"及时回复\"},\"finish_reason\":\"stop\"}]}\n\n");}
            bytes.CopyTo(buffer);return bytes.Length;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    private sealed class OfflineReplyHandler:HttpMessageHandler
    {
        public bool Complete=true;
        public int Calls;
        public string Reply="```forme-actions\n[{\"type\":\"Dance\",\"mode\":\"sway\"}]\n```";
        public HttpStatusCode Status=HttpStatusCode.OK;
        public string LastRequest="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest=await request.Content!.ReadAsStringAsync(cancellationToken);
            string reply=Reply;
            string body="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=reply}}}})+"\n\n"+(Complete?"data: [DONE]\n\n":"");
            return new HttpResponseMessage(Status){Content=new StringContent(body,Encoding.UTF8,"text/event-stream")};
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
    private static IEnumerable<Model3D> Shapes(Model3DGroup group)=>group.Children.SelectMany(child=>child is Model3DGroup nested?Shapes(nested):[child]);
    private static void Click(Window window,string label)=>Descendants(window).OfType<Button>().First(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static void Image(FrameworkElement view,string name)
    {
        view.UpdateLayout();var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create("artifacts/screenshots/"+name+".png");encoder.Save(file);
    }
    public static async Task DesktopRules(DesktopHost host,Controller c)
    {
        string hiddenReport="跟我走两步吧。\n\n"+CompanionCommands.ReportMarker+" 伙伴在桌面走几步，结束后停下；已发起伙伴动作";
        if(ChatDisplay.Message(hiddenReport)!="跟我走两步吧。"||ChatDisplay.Message("本地规则匹配 · 未联网\n"+CompanionCommands.ReportMarker+" 已发起伙伴动作").Length!=0||ChatDisplay.Status("回复完成。 "+CompanionCommands.ReportMarker+" 已发起伙伴动作")!="已处理伙伴动作请求。"||ChatDisplay.Status("正在执行本地规则，不发送AI请求。")!="正在陪你互动…")throw new Exception("Chat exposed internal action details");
        var before=c.Preferences;string rules=c.RulesText();c.SaveRules(ActionRules.Default().Serialize());
        c.SavePreferences(before with{LocalActionRules=true,AllowPetControl=true,AllowPlayControl=true,PetIdleMode="idle",Quiet=false,ReducedMotion=false,DisplayMode="pet"});
        host.HidePet();host.ShowPet();await Task.Delay(150);host.ShowFloatingChat();await Task.Delay(80);
        if(!Descendants(host.FloatingChat!).OfType<Button>().Single(b=>Equals(b.Content,"发送")).IsEnabled)throw new Exception("Local send disabled without key");
        var start=new Point(host.Pet!.Left,host.Pet.Top);c.Draft="你能走两步吗？";
        if(!await c.TrySendLocal()||c.Busy||new Point(host.Pet.Left,host.Pet.Top)==start||host.House is not null)throw new Exception("Desktop local walking failed");
        c.Draft="跑几步";if(!await c.TrySendLocal())throw new Exception("Desktop run unmatched");
        c.Draft="睡觉吧";await c.TrySendLocal();if(c.Preferences.PetIdleMode!="sleep")throw new Exception("Desktop sleep rule failed");
        c.Draft="随机走动";await c.TrySendLocal();var roamStart=new Point(host.Pet.Left,host.Pet.Top);await Task.Delay(2800);if(new Point(host.Pet.Left,host.Pet.Top)==roamStart)throw new Exception("AI mode did not roam with chat open");
        c.Draft="别动";await c.TrySendLocal();if(c.Preferences.PetIdleMode!="idle")throw new Exception("Stop did not end random mode");
        host.Pet.Greet("绕屏回归检查");await Task.Delay(100);
        // DragMove consumes mouse-up; the previous gesture marker must not count as an active drag.
        typeof(PetWindow).GetField("_dragged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(host.Pet,true);
        c.Draft="跑2圈在屏幕上";
        if(!await c.TrySendLocal())throw new Exception("Desktop two laps did not resolve locally");
        if(c.ActionResults.LastOrDefault()?.Code!=ActionResultCode.Completed)throw new Exception("Desktop two laps failed: "+c.ChatStatus);
        c.Draft="绕屏幕跑两圈";var laps=c.TrySendLocal();await Task.Delay(150);host.Pet.StopCommands();await laps;
        if(c.ActionResults.LastOrDefault()?.Code!=ActionResultCode.Interrupted)throw new Exception("Real desktop stop was not reported as interrupted");
        c.Draft="跑几步";if(!await c.TrySendLocal()||c.ActionResults.Last().Code!=ActionResultCode.Completed)throw new Exception("Desktop command did not recover after stop");
        c.Draft="不要走两步";if(await c.TrySendLocal())throw new Exception("Negated instruction executed");
        c.SavePreferences(c.Preferences with{PetIdleMode="idle"});c.Draft="走两步，然后跳个舞";await c.TrySendLocal();
        var messages=c.Store.Messages(c.Session!.Id);if(messages.Last().Status!="local")throw new Exception("Local transcript not marked local");
        if(AiClient.BuildContext(c.Preferences,messages,"你好",out _).Any(t=>t.Content.Contains("不要走两步")))throw new Exception("Local transcript leaked");
        Image(host.FloatingChat!,"desktop-local-actions");
        c.Draft="陪我玩藏物";await c.TrySendLocal();if(host.House?.CurrentPage!="hide"||host.FloatingChat is not null)throw new Exception("Desktop game did not open world");
        host.House.StopCompanion();host.ShowHouse("settings");host.House.UpdateLayout();
        Descendants(host.House).OfType<Expander>().Single(e=>Equals(e.Header,"自然动作规则 · 可编辑")).IsExpanded=true;host.House.UpdateLayout();
        var editor=Descendants(host.House).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="动作规则JSON");
        editor.Text=new ActionRules(1,[new(["来个舞蹈"],[new("dance","sway")])]).Serialize();Click(host.House,"保存动作规则");c.Draft="来个舞蹈";
        if(c.LocalRulePreview() is null)throw new Exception("Rule editor did not save and reload");
        c.SaveRules(rules);c.SavePreferences(before);c.Draft="";host.ShowHouse("home");
        File.WriteAllText("artifacts/desktop-rules-smoke-result.txt","PASS: key-independent local send; actual desktop walk/run with floating chat, finite sequence, sleep, negative request rejection, local transcript exclusion, gameplay transfer to house; two screen laps after a completed drag gesture with bubble auto-collapse; explicit stop interrupts and subsequent movement recovers. No external API requests.\n");
    }
    public static async Task Creation(DesktopHost host,Controller c,Room3DView room)
    {
        var before=c.Preferences;var original=c.Store.World();var originalBoat=room.BoatSettings;var house=host.House!;
        c.SavePreferences(before with{AllowPetControl=true,AllowSceneControl=true,AllowPlayControl=true,AllowLayoutPreview=true,ReducedMotion=true,Weather="rain",Theme="day"});
        // The full default state must fit the ordinary budget with all four grants enabled.
        AiClient.BuildContext(c.Preferences,[],"帮我布置阅读角",out _,scene:house.SceneDescription);
        host.ExecuteCompanionCommand(new("layout","reading"));
        if(house.CurrentPage!="furniture"||c.Store.World().Items.SequenceEqual(room.CurrentWorld.Items))throw new Exception("AI layout was not an unsaved preview");
        var preview=room.CurrentWorld.Copy();
        try{host.ExecuteCompanionCommand(new("furniture","book",-2.25,-2.29));throw new Exception("Overlapping furniture accepted");}catch(OperationFailureException){}
        if(!preview.Items.SequenceEqual(room.CurrentWorld.Items))throw new Exception("Rejected layout mutated draft");
        house.UpdateLayout();Click(house,"保存家具布置");if(!c.Store.World().Items.SequenceEqual(preview.Items))throw new Exception("Preview save did not apply");
        host.ExecuteCompanionCommand(new("layout","reading"));host.ExecuteCompanionCommand(new("furniture","cushion",-.8,.25,90));
        Image(house,"ai-furniture-preview");house.Navigate("chat");if(!c.Store.World().Items.SequenceEqual(preview.Items))throw new Exception("Cancelled preview persisted");
        host.ExecuteCompanionCommand(new("life","reading"));if(!c.Store.World().Discoveries.Contains("reading"))throw new Exception("AI life not recorded on arrival");
        host.ExecuteCompanionCommand(new("hide","pet-hides"));if(!room.HideActive)throw new Exception("AI hide not active");
        if(house.SceneDescription.Contains("_hideTarget"))throw new Exception("Hidden answer leaked");host.ExecuteCompanionCommand(new("stop"));if(room.HideActive)throw new Exception("AI stop failed");
        host.ExecuteCompanionCommand(new("boat-color","rose"));host.ExecuteCompanionCommand(new("boat-leaf","1",-1,-.4,30));host.ExecuteCompanionCommand(new("boat","start"));
        if(room.LastBoat is null)throw new Exception("Reduced AI boat did not complete");host.ExecuteCompanionCommand(new("boat-name","听雨的小旅行"));if(room.LastBoat.Title!="听雨的小旅行")throw new Exception("AI naming failed");
        Image(house,"ai-boat-creation");house.Navigate("chat");
        c.SavePreferences(c.Preferences with{ReducedMotion=false});host.ExecuteCompanionCommand(new("move",X:1,Z:1));
        var waiting=house.WaitCreation(new("move",X:1,Z:1),CancellationToken.None);room.StopWorld();if(await waiting)throw new Exception("Manual stop did not cancel waiter");
        room.SwitchScene(false);c.SavePreferences(c.Preferences with{ReducedMotion=true});var naturalStart=room.PetPosition;
        host.ExecuteCompanionCommand(new("stroll","walk"));if(room.PetPosition==naturalStart)throw new Exception("Natural walking did not move");
        try{host.ExecuteCompanionCommand(new("go","book"));}catch(ActionFailureException ex){var probe=room.CurrentWorld.Travel();probe.Reset(room.PetPosition);var book=room.CurrentWorld.Items.FirstOrDefault(x=>x.Kind=="book");throw new Exception($"Natural book target failed: position={room.PetPosition}; world={string.Join(";",room.CurrentWorld.Items.Select(x=>$"{x.Kind}@{x.X:0.0},{x.Z:0.0}"))}; bookApproach={(book is null?"missing":LivingWorld.Approach(book,probe)?.ToString()??"unreachable")}; {ex.Message}",ex);}
        if(room.PetPosition==naturalStart||!room.CurrentWorld.Travel().Walkable(room.PetPosition))throw new Exception("Natural furniture target unreachable");
        c.SavePreferences(c.Preferences with{ReducedMotion=false});
        c.SavePreferences(c.Preferences with{AllowLayoutPreview=false,AllowPlayControl=false});
        if(house.SceneDescription.Contains("furniture")||house.SceneDescription.Contains("events"))throw new Exception("Disabled creation grants leaked world data");
        c.SavePreferences(c.Preferences with{AllowLayoutPreview=true,AllowPlayControl=true});
        // Exercise the real reply controller without connecting to an external service.
        var handler=new OfflineReplyHandler{Reply="```forme-actions\n[{\"type\":\"Dance\",\"mode\":\"sway\"},{\"type\":\"Dance\",\"mode\":\"hop\"}]\n```"};
        using(var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"creation-protocol"),new AiClient(handler)))
        {
            protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true});int steps=0;bool waitingStep=false;
            protocol.CommandHandler=_=>{if(waitingStep)throw new Exception("Commands overlapped");steps++;return "已发起";};
            protocol.CommandWaiter=async(_,token)=>{waitingStep=true;await Task.Delay(50,token);waitingStep=false;return true;};protocol.Draft="依次跳两支舞";await protocol.Send();if(steps!=2)throw new Exception("Sequential reply failed");
            protocol.CommandWaiter=async(_,token)=>{await Task.Delay(10000,token);return true;};protocol.Draft="再跳两支舞";var sending=protocol.Send();await Task.Delay(150);protocol.StopReply();await sending;if(steps!=3||protocol.Busy)throw new Exception("Cancelled sequence continued");
            protocol.CommandWaiter=(_,_)=>Task.FromResult(false);protocol.Draft="再跳一次";await protocol.Send();if(steps!=4)throw new Exception("Interrupted sequence continued");
            protocol.CommandHandler=_=>throw new OperationFailureException("被家具遮挡");protocol.Draft="请跳舞试试被挡的动作";await protocol.Send();if(!protocol.ChatStatus.Contains("被家具遮挡"))throw new Exception("Local failure missing");
            handler.Reply="```forme-actions\n[{\"type\":\"Stroll\",\"mode\":\"walk\"},{\"type\":\"Dance\",\"mode\":\"sway\"}]\n```";
            var beforeWalk=room.PetPosition;
            protocol.CurrentScene=()=>house.CompanionScene;
            protocol.CommandHandler=cmd=>{if(cmd.Action=="dance"&&(room.PetMoving||room.PetPosition==beforeWalk))throw new Exception("Pat began before arrival");return host.ExecuteCompanionCommand(cmd);};
            protocol.CommandWaiter=house.WaitCreation;protocol.Draft="走两步再跳舞";await protocol.Send();var actionReply=protocol.Store.Messages(protocol.Session!.Id).Last().Content;if(!actionReply.Contains("我",StringComparison.Ordinal)||actionReply.Contains("伙伴动作已完成",StringComparison.Ordinal)||actionReply.Contains(CompanionCommands.Marker,StringComparison.Ordinal))throw new Exception("Actual sequential action did not return a truthful in-character result: "+protocol.ChatStatus);
        }
        c.Store.SaveWorld(original);c.SavePreferences(before);house.Navigate("home");room.ConfigureBoat(originalBoat);
        File.WriteAllText("artifacts/ai-creation-smoke-result.txt","PASS: bounded context; AI layout preview, collision rejection without draft mutation, manual save/cancel; eligible life discovery; hide/stop; boat color/leaf/start/name; manual interrupt; permission-scoped context; natural stroll and furniture destination; offline streamed reply sequential execution, cancellation, interrupted sequence, local failure report; actual stroll-arrive-pat order. No external API calls.\n");
    }
    public static async Task Run(DesktopHost host,Controller c,Room3DView room)
    {
        var handler=new OfflineReplyHandler();
        using(var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"offline-protocol"),new AiClient(handler)))
        {
            protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true});int executed=0;protocol.CommandHandler=_=>{executed++;return "已发起伙伴动作";};protocol.CommandWaiter=(_,_)=>Task.FromResult(true);protocol.Draft="请跳舞";await protocol.Send();
            if(executed!=1||protocol.Store.Messages(protocol.Session!.Id).Last().Content.Contains(CompanionCommands.Marker)||protocol.ActionResults.Last().Code!=ActionResultCode.Completed)throw new Exception("Offline reply did not persist clean text and execute exactly once");
            handler.Complete=false;protocol.Draft="再跳一次";await protocol.Send();if(executed!=1||protocol.Store.Messages(protocol.Session.Id).Last().Status!="error")throw new Exception("Incomplete reply executed an action");
        }
        var original=c.Preferences;
        foreach(var model in PetModels.Catalog)
        {
            using var rig=PetModels.For(model.Id).Create();var body=rig.Root.Children[0];
            if(model.Id=="space-marine")
            {
                var bounds=body.Bounds;int triangles=Shapes(rig.Root).OfType<GeometryModel3D>().Sum(x=>((MeshGeometry3D)x.Geometry).TriangleIndices.Count/3);
                if(bounds.SizeY<3.0||bounds.SizeY>3.3||triangles!=260314)throw new Exception($"Embedded Space Marine geometry, rifle or doubled size are invalid: {bounds}, {triangles}");
                await MarineAnimation((SpaceMarinePetModel)rig);
            }
            for(int i=0;i<1000;i++)rig.Apply(new PetMotion{State=i%2==0?"idle":"sleep"}.Sample(i*.02));
            if(!ReferenceEquals(body,rig.Root.Children[0])||(model.Id=="space-marine"?!Shapes(rig.Root).All(x=>x.IsFrozen):!body.IsFrozen)||!Shapes(rig.Root).All(rig.Contains))throw new Exception("Model pose cache or hit ownership failed: "+model.Id);
            c.SavePreferences(c.Preferences with{PetModel=model.Id});host.House!.Navigate("home");await Task.Delay(100);Image(host.House,"companion-"+model.Id);
        }
        await MarineSize(host,c,room);
        c.SavePreferences(c.Preferences with{ReducedMotion=true,AllowPetControl=true,AllowSceneControl=true});
        room.SwitchScene(false);
        foreach(string id in new[]{"feed","book","fish","sleep"})if(room.Interact(id).Contains("走不到"))throw new Exception("Indoor interaction unreachable: "+id);
        bool lamp=c.Preferences.RoomLamp;room.Interact("lamp");if(c.Preferences.RoomLamp==lamp)throw new Exception("Lamp interaction failed");
        int points=c.Store.PlantPoints;room.Interact("water");if(c.Store.PlantPoints!=points+1)throw new Exception("Water interaction failed");room.Interact("water");if(c.Store.PlantPoints!=points+1)throw new Exception("Water interaction was not deduplicated");
        room.SwitchScene(true);
        foreach(string id in new[]{"pond","picnic","rest","bell"})if(room.Interact(id).Contains("走不到"))throw new Exception("Outdoor interaction unreachable: "+id);
        for(int i=0;i<100;i++)room.SwitchScene(i%2==0);
        if(room.SceneCacheCount>2)throw new Exception("Scene cache exceeded bound");
        c.SavePreferences(c.Preferences with{Weather="snow",Fireplace=true});Image(host.House!,"companion-outdoor-snow");
        host.ExecuteCompanionCommand(new("scene","indoor"));host.ExecuteCompanionCommand(new("light","off"));
        if(c.Preferences.RoomLamp||room.Outdoors)throw new Exception("Authorized local AI control did not execute");
        c.SavePreferences(c.Preferences with{AllowSceneControl=false});try{host.ExecuteCompanionCommand(new("light","on"));throw new Exception("Permission failure was not typed");}catch(ActionFailureException){}if(c.Preferences.RoomLamp)throw new Exception("Disabled scene permission executed a command");
        host.House!.Navigate("settings");host.House.UpdateLayout();
        var preset=Descendants(host.House).OfType<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="AI 角色预设");preset.SelectedIndex=2;
        var role=Descendants(host.House).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="AI 角色设定");role.Text="住在花园里的故事伙伴。";Click(host.House,"保存角色与权限");
        if(c.Preferences.CharacterPreset!="story"||c.Preferences.RoleDescription!=role.Text)throw new Exception("Persona UI failed to save");
        role.BringIntoView();await Task.Delay(60);Image(host.House,"companion-persona");
        host.House.Navigate("memory");host.House.UpdateLayout();
        Descendants(host.House).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="记忆标题").Text="饮品偏好";
        Descendants(host.House).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="记忆内容").Text="我喜欢温热的红茶。";Click(host.House,"保存到当前服务商");
        if(!c.Store.Memories().Any(x=>x.Title=="饮品偏好"&&x.Provider==AiClient.ProviderIdentity(c.Preferences.Endpoint)))throw new Exception("Memory UI failed to save or bind provider");Image(host.House,"companion-memory");
        c.NewSession();host.House.Navigate("chat");host.House.UpdateLayout();
        var chatOptions=Descendants(host.House).OfType<Expander>().Single(x=>AutomationProperties.GetName(x)=="聊天记录管理");chatOptions.IsExpanded=true;host.House.UpdateLayout();
        var expander=Descendants(host.House).OfType<Expander>().Single(x=>x.Header?.ToString()?.StartsWith("会话标题与摘要")==true);expander.IsExpanded=true;host.House.UpdateLayout();
        Descendants(host.House).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="会话摘要").Text="我们约好去池塘看看。";Click(host.House,"保存会话摘要");
        if(c.Store.Note(c.Session!.Id)?.Content!="我们约好去池塘看看。")throw new Exception("Session note UI failed");
        c.SavePreferences(original);room.SwitchScene(false);host.House.Navigate("home");
        File.WriteAllText("artifacts/companion-smoke-result.txt","PASS: 5 interchangeable models; complete Space Marine with rifle, original Walk/Run clips, doubled desktop/house size, front-facing idle, independent joints and shared frozen meshes; indoor/outdoor interactions, lamp toggle, watering deduplication, 100 scene switches with cache <=2, authorized/disabled AI control, persona, memory and session note UI. Offline; no AI API calls.\n");
    }
    private static async Task MarineAnimation(SpaceMarinePetModel marine)
    {
        if(marine.Clips.Count!=2||Math.Abs(marine.Clips["Walk"].Duration-1.35)>.001||Math.Abs(marine.Clips["Run"].Duration-.8)>.001||marine.JointMatrices.Length!=8)throw new Exception("Original marine clips or joints were lost");
        var idle=marine.JointMatrices;var shapes=Shapes(marine.Root).ToArray();var forward=marine.Root.Transform.Transform(new Vector3D(0,0,1));if(Math.Abs(forward.X)>.001||forward.Z<.99)throw new Exception("Marine neutral pose is not front-facing");
        using var other=new SpaceMarinePetModel();if(!shapes.SequenceEqual(Shapes(other.Root))||ReferenceEquals(marine.Root.Children[0],other.Root.Children[0]))throw new Exception("Marine instances did not share frozen geometry or isolate joints");
        foreach(string gait in new[]{"walk","run"})
        {
            marine.Apply(PetPose.Neutral() with{Locomotion=gait,LocomotionSeconds=.17});var first=marine.JointMatrices;
            marine.Apply(PetPose.Neutral() with{Locomotion=gait,LocomotionSeconds=.37});var second=marine.JointMatrices;
            if(marine.ActiveClip!=gait||first.SequenceEqual(second)||idle.SequenceEqual(first)||!other.JointMatrices.SequenceEqual(idle)||!shapes.SequenceEqual(Shapes(marine.Root)))throw new Exception("Imported marine gait did not animate joints independently or rebuilt geometry: "+gait);
            marine.Apply(PetPose.Neutral() with{Locomotion=gait,LocomotionSeconds=.17+marine.Clips[gait].Duration});if(!first.SequenceEqual(marine.JointMatrices))throw new Exception("Imported gait did not loop: "+gait);
        }
        marine.Apply(PetPose.Neutral());if(marine.ActiveClip.Length!=0||!marine.JointMatrices.SequenceEqual(idle))throw new Exception("Stopped marine retained a gait pose");
        var viewport=new Viewport3D{Camera=new PerspectiveCamera(new(0,1.55,8),new(0,0,-8),new(0,1,0),31)};var visual=new ModelVisual3D{Content=marine.Root};viewport.Children.Add(visual);
        var window=new Window{Title="Forme 模型验收",Width=440,Height=470,ShowActivated=false,ShowInTaskbar=false,Content=viewport,Background=Ui.Brush("#F7F5EE")};
        try
        {
            window.Show();await Task.Delay(80);Image(window,"space-marine-front");
            foreach(string gait in new[]{"walk","run"})foreach(double seconds in new[]{.17,.37}){marine.Apply(PetPose.Neutral() with{Locomotion=gait,LocomotionSeconds=seconds});await Task.Delay(40);Image(window,$"space-marine-{gait}-{seconds:0.00}");}
        }
        finally{visual.Content=null;window.Close();marine.Apply(PetPose.Neutral());}
        using var animator=new PetAnimator(other);var travel=new PetTravel(-10,10,-10,10,[]);travel.Reset(new(0,0));animator.AttachTravel(travel,(_,_)=>{});animator.Configure("idle",true,false,false,false);
        if(!animator.MoveTo(new(2,0)))throw new Exception("Marine animator rejected room travel");await Task.Delay(150);
        if(other.ActiveClip!="walk")throw new Exception("Room travel did not select the imported Walk clip");travel.SetGait(true);await Task.Delay(150);
        if(other.ActiveClip!="run")throw new Exception("Room travel did not switch to the imported Run clip");animator.Configure("idle",true,true,false,false);
        if(animator.Running||other.ActiveClip.Length!=0||!other.JointMatrices.SequenceEqual(idle))throw new Exception("Reduced motion did not reset marine joints or stop travel animation");
        bool running=false;var wander=new DesktopWander(new Random(1));wander.Reset(new(100,100),0,2000,0,2000);wander.BeginStep(300);animator.ExternalActive=true;animator.ExternalMoving=()=>wander.Moving;animator.ExternalMotion=(seconds,_)=>wander.Advance(seconds,running);animator.Configure("idle",true,false,false,false);await Task.Delay(150);
        if(other.ActiveClip!="walk")throw new Exception("Desktop wander did not select the imported Walk clip");running=true;await Task.Delay(150);
        if(other.ActiveClip!="run")throw new Exception("Desktop wander did not select the imported Run clip");animator.Configure("idle",false,false,false,false);
        if(animator.Running||other.ActiveClip.Length!=0||!other.JointMatrices.SequenceEqual(idle))throw new Exception("Hidden marine continued animating");
    }
    private static async Task MarineSize(DesktopHost host,Controller c,Room3DView room)
    {
        var before=c.Preferences;
        try
        {
            c.SavePreferences(before with{PetModel="space-marine",PetScale=1,ReducedMotion=true,Quiet=true,PetIdleMode="idle"});host.House!.Navigate("home");host.HideDesktopPet();host.ShowPet();await Task.Delay(80);
            var pet=host.Pet!;pet.UpdateLayout();host.House.UpdateLayout();double height=room.DisplayedPetBounds.SizeY;var view=Descendants(pet).OfType<Pet3DView>().Single();var baseline=view.TransformToAncestor(pet).TransformBounds(new Rect(0,0,view.ActualWidth,view.ActualHeight));
            if(height<3.0||pet.Width<379||!view.CharacterFits())throw new Exception("Doubled house/desktop marine size is missing or clipped");Image(pet,"space-marine-desktop-standard");Image(host.House,"space-marine-house-standard");
            c.SavePreferences(c.Preferences with{PetScale=1.3});await Task.Delay(80);pet.UpdateLayout();host.House.UpdateLayout();var large=view.TransformToAncestor(pet).TransformBounds(new Rect(0,0,view.ActualWidth,view.ActualHeight));
            if(room.DisplayedPetBounds.SizeY/height<1.29||large.Height/baseline.Height<1.2||!view.CharacterFits())throw new Exception("Large size did not enlarge both marine displays or clipped the desktop model");Image(pet,"space-marine-desktop-large");Image(host.House,"space-marine-house-large");
            File.WriteAllText("artifacts/space-marine-smoke-result.txt",$"PASS: original Walk=1.35s / Run=0.8s clips; 8 independent animated joints, 260314 triangles including rifle, shared frozen geometry, loop/reset and +Z neutral facing; house height {height:0.000}m -> {room.DisplayedPetBounds.SizeY:0.000}m on large; desktop model area {baseline.Height:0.0} -> {large.Height:0.0} DIP; projection bounds fit; no network calls.\n");
        }
        finally{c.SavePreferences(before);}
    }
}
