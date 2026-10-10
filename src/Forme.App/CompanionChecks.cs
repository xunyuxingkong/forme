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
    private sealed class OfflineReplyHandler:HttpMessageHandler
    {
        public bool Complete=true;
        public string Reply="我们来跳舞吧。\n```forme-actions\n[{\"action\":\"dance\",\"value\":\"sway\"}]\n```";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            string reply=Reply;
            string body="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=reply}}}})+"\n\n"+(Complete?"data: [DONE]\n\n":"");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"text/event-stream")});
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
        File.WriteAllText("artifacts/desktop-rules-smoke-result.txt","PASS: key-independent local send; actual desktop walk/run with floating chat, finite sequence, sleep, negative request rejection, local transcript exclusion, gameplay transfer to house. No external API requests.\n");
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
        host.ExecuteCompanionCommand(new("go","book"));if(room.PetPosition==naturalStart||!room.CurrentWorld.Travel().Walkable(room.PetPosition))throw new Exception("Natural furniture target unreachable");
        c.SavePreferences(c.Preferences with{ReducedMotion=false});
        c.SavePreferences(c.Preferences with{AllowLayoutPreview=false,AllowPlayControl=false});
        if(house.SceneDescription.Contains("furniture")||house.SceneDescription.Contains("events"))throw new Exception("Disabled creation grants leaked world data");
        c.SavePreferences(c.Preferences with{AllowLayoutPreview=true,AllowPlayControl=true});
        // Exercise the real reply controller without connecting to an external service.
        var handler=new OfflineReplyHandler{Reply="按顺序执行。```forme-actions\n[{\"action\":\"dance\",\"value\":\"sway\"},{\"action\":\"dance\",\"value\":\"hop\"}]```"};
        using(var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"creation-protocol"),new AiClient(handler)))
        {
            protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true});int steps=0;bool waitingStep=false;
            protocol.CommandHandler=_=>{if(waitingStep)throw new Exception("Commands overlapped");steps++;return "已发起";};
            protocol.CommandWaiter=async(_,token)=>{waitingStep=true;await Task.Delay(50,token);waitingStep=false;return true;};protocol.Draft="依次跳两支舞";await protocol.Send();if(steps!=2)throw new Exception("Sequential reply failed");
            protocol.CommandWaiter=async(_,token)=>{await Task.Delay(10000,token);return true;};protocol.Draft="再跳两支舞";var sending=protocol.Send();await Task.Delay(150);protocol.StopReply();await sending;if(steps!=3||protocol.Busy)throw new Exception("Cancelled sequence continued");
            protocol.CommandWaiter=(_,_)=>Task.FromResult(false);protocol.Draft="再试一次";await protocol.Send();if(steps!=4)throw new Exception("Interrupted sequence continued");
            protocol.CommandHandler=_=>throw new OperationFailureException("被家具遮挡");protocol.Draft="试试被挡的动作";await protocol.Send();if(!protocol.ChatStatus.Contains("被家具遮挡"))throw new Exception("Local failure missing");
            handler.Reply="走两步再拍拍。```forme-actions\n[{\"action\":\"stroll\",\"value\":\"walk\"},{\"action\":\"pat\"}]```";
            var beforeWalk=room.PetPosition;
            protocol.CommandHandler=cmd=>{if(cmd.Action=="pat"&&(room.PetMoving||room.PetPosition==beforeWalk))throw new Exception("Pat began before arrival");return host.ExecuteCompanionCommand(cmd);};
            protocol.CommandWaiter=house.WaitCreation;protocol.Draft="走两步再拍拍";await protocol.Send();if(!protocol.ChatStatus.Contains("伙伴动作"))throw new Exception("Actual sequential action failed: "+protocol.ChatStatus);
        }
        c.Store.SaveWorld(original);c.SavePreferences(before);house.Navigate("home");room.ConfigureBoat(originalBoat);
        File.WriteAllText("artifacts/ai-creation-smoke-result.txt","PASS: bounded context; AI layout preview, collision rejection without draft mutation, manual save/cancel; eligible life discovery; hide/stop; boat color/leaf/start/name; manual interrupt; permission-scoped context; natural stroll and furniture destination; offline streamed reply sequential execution, cancellation, interrupted sequence, local failure report; actual stroll-arrive-pat order. No external API calls.\n");
    }
    public static async Task Run(DesktopHost host,Controller c,Room3DView room)
    {
        var handler=new OfflineReplyHandler();
        using(var protocol=new Controller(Path.Combine(c.Store.DirectoryPath,"offline-protocol"),new AiClient(handler)))
        {
            protocol.Secrets.Save("offline-test-key");protocol.SavePreferences(protocol.Preferences with{AllowPetControl=true});int executed=0;protocol.CommandHandler=_=>{executed++;return "已发起伙伴动作";};protocol.Draft="请跳舞";await protocol.Send();
            if(executed!=1||protocol.Store.Messages(protocol.Session!.Id).Last().Content.Contains(CompanionCommands.Marker)||!protocol.Store.Messages(protocol.Session.Id).Last().Content.Contains(CompanionCommands.ReportMarker))throw new Exception("Offline reply did not persist clean text and execute exactly once");
            handler.Complete=false;protocol.Draft="再跳一次";await protocol.Send();if(executed!=1||protocol.Store.Messages(protocol.Session.Id).Last().Status!="error")throw new Exception("Incomplete reply executed an action");
        }
        var original=c.Preferences;
        foreach(var model in PetModels.Catalog)
        {
            using var rig=PetModels.For(model.Id).Create();var body=rig.Root.Children[0];
            for(int i=0;i<1000;i++)rig.Apply(new PetMotion{State=i%2==0?"idle":"sleep"}.Sample(i*.02));
            if(!ReferenceEquals(body,rig.Root.Children[0])||!body.IsFrozen||!Shapes(rig.Root).All(rig.Contains))throw new Exception("Model pose cache or hit ownership failed: "+model.Id);
            c.SavePreferences(c.Preferences with{PetModel=model.Id});host.House!.Navigate("home");await Task.Delay(100);Image(host.House,"companion-"+model.Id);
        }
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
        c.SavePreferences(c.Preferences with{AllowSceneControl=false});host.ExecuteCompanionCommand(new("light","on"));if(c.Preferences.RoomLamp)throw new Exception("Disabled scene permission executed a command");
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
        File.WriteAllText("artifacts/companion-smoke-result.txt","PASS: 4 interchangeable models and immutable meshes, indoor and outdoor interaction paths, lamp toggle, watering deduplication, 100 scene switches with cache <=2, authorized and disabled AI control, persona, memory and session note UI. Offline; no AI API calls.\n");
    }
}
