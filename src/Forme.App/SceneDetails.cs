using Forme.Core;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Forme.App;

internal static partial class MeshArt
{
    private static void Interactive(Model3DGroup scene,Dictionary<Model3D,string> hits,string id,Action<Model3DGroup> build)
    {var item=new Model3DGroup();build(item);foreach(var shape in item.Children)hits[shape]="interact:"+id;scene.Children.Add(item);}
    private static void RoomDetails(Model3DGroup scene,Preferences p,Dictionary<Model3D,string> hits)
    {
        Interactive(scene,hits,"book",g=>
        {
            SoftBox(g,"#B39A7D",3.64,1.1,-3.0,.66,2.0,.42);
            foreach(double y in new[]{.30,.9,1.5,2.08})Box(g,"#E1CEAC",3.64,y,-2.78,.72,.08,.38);
            foreach(double y in new[]{.52,1.12,1.72})for(int i=0;i<4;i++)Box(g,new[]{"#91ACA0","#D5B191","#B5A4BE","#DBCEA2"}[i],3.37+i*.16,y,-2.73,.11,.32,.20,i==3?-8:0);
        });
        Interactive(scene,hits,"lamp",g=>
        {Ellipse(g,"#8E7961",3.48,.08,-.4,.28,.045,.28);Box(g,"#987F61",3.48,.9,-.4,.05,1.7,.05);Ellipse(g,p.RoomLamp?"#F4DF99":"#B4B4A3",3.48,1.75,-.4,.38,.24,.36);});
        if(p.RoomLamp)scene.Children.Add(new PointLight(Color.FromRgb(235,199,128),new Point3D(3.48,1.7,-.4)){Range=4,ConstantAttenuation=1,LinearAttenuation=.8});
        Interactive(scene,hits,"fireplace",g=>
        {
            SoftBox(g,"#AE998D",-3.65,.60,-.5,.65,1.2,1.2);Box(g,"#655952",-3.28,.54,-.5,.04,.73,.82);SoftBox(g,"#D4BC99",-3.6,1.23,-.5,.93,.14,1.39);
            foreach(double z in new[]{-.73,-.32})Box(g,"#90755A",-3.22,.30,z,.14,.14,.63,12);
            if(p.Fireplace){Ellipse(g,"#E2A76B",-3.16,.54,-.5,.10,.30,.26);Ellipse(g,"#F3D597",-3.04,.44,-.5,.09,.20,.14);}
        });
        if(p.Fireplace)scene.Children.Add(new PointLight(Color.FromRgb(244,169,94),new Point3D(-3.0,.65,-.5)){Range=3,ConstantAttenuation=1,LinearAttenuation=.8});
        Interactive(scene,hits,"feed",g=>
        {Ellipse(g,"#BCA1A0",-2.66,.13,1.60,.31,.13,.25);Ellipse(g,"#E4CD99",-2.66,.24,1.60,.24,.02,.18);for(int i=0;i<3;i++)Ellipse(g,"#AC825D",-2.78+i*.12,.27,1.60,.06,.025,.05);});
        Interactive(scene,hits,"sleep",g=>
        {Ellipse(g,"#B5A5BF",2.83,.10,1.46,.75,.10,.62);Ellipse(g,"#DED4E3",2.83,.18,1.46,.61,.045,.48);Ellipse(g,"#F1E7D2",2.85,.29,1.16,.32,.10,.21);});
        Interactive(scene,hits,"fish",g=>
        {
            SoftBox(g,"#BDA989",-.20,.22,-3.12,.9,.44,.5);Box(g,"#93BDCD",-.20,.77,-3.12,.86,.64,.44);Box(g,"#D8C8A9",-.20,.48,-2.88,.9,.09,.04);
            Ellipse(g,"#D7A86A",-.36,.83,-2.875,.13,.07,.015);Ellipse(g,"#E9CE84",-.04,.69,-2.875,.10,.055,.015);
            for(int i=0;i<3;i++)Ellipse(g,"#E4EDF0",.09,.85+i*.10,-2.88,.025,.025,.01);
        });
        WeatherDetails(scene,p,false);
    }
    private static void OutdoorDetails(Model3DGroup scene,Preferences p,Dictionary<Model3D,string> hits)
    {
        Interactive(scene,hits,"pond",g=>
        {
            Ellipse(g,"#A9AB91",-3.6,.028,-5.0,2.3,.018,1.55);Ellipse(g,p.Weather=="snow"?"#C7DDE3":"#8CBDBD",-3.6,.051,-5.0,2.07,.012,1.32);
            foreach(var (x,z) in new[]{(-5.4,-5.5),(-2.0,-4.5),(-3.8,-6.3)})Ellipse(g,"#CBC6AC",x,.12,z,.30,.13,.26);
            foreach(var (x,z) in new[]{(-4.4,-5.2),(-3.2,-5.6)}){Ellipse(g,"#90AB78",x,.07,z,.24,.012,.23);Ellipse(g,"#D6A4B5",x,.13,z,.08,.07,.08);}
            SoftBox(g,"#B79A73",-3.8,.22,-3.52,3.3,.13,.54);for(int i=0;i<8;i++)Box(g,"#8D755C",-5.18+i*.40,.295,-3.52,.012,.008,.55);
        });
        Interactive(scene,hits,"picnic",g=>
        {
            SoftBox(g,"#D0A6A3",3.8,.025,4.7,2.7,.025,2.1);
            for(int i=0;i<5;i++)Box(g,"#F1DFCB",2.7+i*.54,.043,4.7,.21,.005,2.09);
            SoftBox(g,"#B6986D",4.44,.30,4.23,.55,.56,.43);Ellipse(g,"#E7D7AE",3.32,.10,4.64,.27,.06,.23);Ellipse(g,"#C1AA81",3.3,.22,4.64,.12,.08,.10);
            Ellipse(g,"#C6977B",3.85,.10,5.17,.26,.06,.15);
        });
        Interactive(scene,hits,"rest",g=>
        {
            foreach(double x in new[]{4.2,6.7})foreach(double z in new[]{-6.2,-4.0})Box(g,"#BAA184",x,1.4,z,.12,2.8,.12);
            SoftBox(g,"#91AFA1",5.45,2.86,-5.1,3.05,.24,2.7);Box(g,"#D0BE9E",5.45,.5,-5.7,2,.16,.7);
            foreach(double x in new[]{4.75,6.15})Box(g,"#AA8F73",x,.25,-5.7,.12,.5,.58);
        });
        Interactive(scene,hits,"bell",g=>
        {Box(g,"#BAA184",.2,1.25,-7.7,.13,2.5,.13);Box(g,"#BAA184",.2,2.53,-7.7,1.3,.12,.12);Box(g,"#D1B791",.68,2.24,-7.7,.016,.47,.016);Ellipse(g,"#D7C498",.68,1.96,-7.7,.15,.20,.15);});
        foreach(var (x,z) in new[]{(-8.0,-1.0),(-7.8,3.0),(7.6,0.0),(2.3,-8.2)})
        {Ellipse(scene,"#92AD78",x,.18,z,.48,.25,.38);Ellipse(scene,"#DAB7A1",x,.45,z,.10,.10,.065);Ellipse(scene,"#E8D7A6",x+.2,.38,z-.1,.08,.08,.06);}
        if(p.Theme=="night")for(int i=0;i<5;i++){double x=-8+i*4;Box(scene,"#B59E7A",x,.5,8.2,.05,1,.05);Ellipse(scene,"#F2D996",x,1.07,8.2,.16,.19,.16);}
        WeatherDetails(scene,p,true);
    }
    // Weather is a frozen diorama layer, with no permanent particle loop.
    private static void WeatherDetails(Model3DGroup scene,Preferences p,bool outdoors)
    {
        if(p.Weather=="clear")return;
        int count=outdoors?22:8;
        for(int i=0;i<count;i++)
        {
            double x=outdoors?-8+(i*7%17): -2.3+(i%4)*.28,z=outdoors?-8+(i*11%17):-3.67,y=outdoors?1+(i%5)*.6:1.65+(i/4)*.34;
            if(p.Weather=="snow")Ellipse(scene,"#F4F5ED",x,y,z,.04,.04,.04);else Box(scene,"#B0C7D2",x,y,z,.018,.25,.018,-12);
        }
        if(outdoors&&p.Weather=="snow")foreach(double x in new[]{-7d,7d})Ellipse(scene,"#EAEDE2",x,.032,6,1.7,.01,1.1);
    }
}

internal sealed partial class Room3DView
{
    public string SceneDescription=>$"{(_outdoors?"20m×20m户外":"小屋和花园")}；宠物坐标X={_travel.Position.X:0.0},Z={_travel.Position.Z:0.0}；天气={_c.Preferences.Weather}；灯={_c.Preferences.RoomLamp}；壁炉={_c.Preferences.Fireplace}";
    private static string InteractionName(string id)=>TargetCatalog.Find(id)?.Name??(id switch{"lamp"=>"落地灯","fireplace"=>"暖暖的壁炉",_=>"互动"});
    public void PlayAction(PetAction action)
    {
        _activityGeneration++;_animator.Play(action);
        if(action is PetAction.DanceSway or PetAction.DanceHop or PetAction.DanceSpin)Ui.Guard(()=>{if(_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"dance"))_c.Refresh();});
    }
    internal string Interact(string id)
    {
        _activityGeneration++;
        if(!IsLoaded||!IsVisible||!_motionVisible||_c.AnimationSuspended)throw new ActionFailureException(ActionResultCode.SceneUnavailable,"场景未显示，互动未执行。");
        if(id=="lamp"){_c.SavePreferences(_c.Preferences with{RoomLamp=!_c.Preferences.RoomLamp});if(_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"furniture"))_c.Refresh();return _hint.Text=_c.Preferences.RoomLamp?"灯亮了，小屋暖暖的。":"灯关好了。";}
        if(id=="fireplace"){_c.SavePreferences(_c.Preferences with{Fireplace=!_c.Preferences.Fireplace});if(_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"furniture"))_c.Refresh();return _hint.Text=_c.Preferences.Fireplace?"壁炉点亮了。":"壁炉熄灭了。";}
        if(id=="ball"){if(!PlayBall())throw new ActionFailureException(ActionResultCode.PathBlocked,"这里无法投球。");return "小球正在飞向落点，伙伴随后去捡。";}
        if(!_outdoors&&CurrentWorld.Items.FirstOrDefault(i=>LivingWorld.Kind(i.Kind).Action==id) is {} furniture){if(!UseFurniture(furniture.Id))throw new ActionFailureException(ActionResultCode.PathBlocked,"目标家具附近没有可达位置。");return _hint.Text;}
        if(_outdoors&&id is "feed" or "book" or "sleep" or "fish")return _hint.Text="回小屋后可以使用这个物件。";
        if(!_outdoors&&id is "pond" or "picnic" or "rest" or "bell")return _hint.Text="去户外后可以使用这个物件。";
        var definition=TargetCatalog.Find(id)??throw new ActionFailureException(ActionResultCode.TargetUnavailable,"目标不存在。");
        var target=TargetCatalog.Resolve(definition,CurrentWorld,_travel,_outdoors?SceneKind.Outdoor:SceneKind.Indoor)??throw new ActionFailureException(ActionResultCode.PathBlocked,"目标附近没有可达位置。");
        _pendingInteraction=null;if(!_animator.MoveTo(target))throw new ActionFailureException(ActionResultCode.PathBlocked,"伙伴走不到这里，请先点击附近的空地。");
        _pendingInteraction=id;if(!_travel.Moving){_pendingInteraction=null;CompleteInteraction(id);}
        return _hint.Text="伙伴正在走向"+InteractionName(id);
    }
    private void CompleteInteraction(string id)
    {
        _moveTarget=null;PaintWorld();if(id.StartsWith("search:",StringComparison.Ordinal)){FinishSearch(id[7..]);return;}if(_pendingLife is {} life){_pendingLife=null;_c.Store.DiscoverLife(life);_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"life");_c.Refresh();WorldChanged?.Invoke();}
        bool toy=id.StartsWith("toy:",StringComparison.Ordinal)||id=="ball";
        if(toy)
        {
            string toyId=id=="ball"?"ball-yellow":id[4..];var item=GameProgression.Catalog.FirstOrDefault(x=>x.Id==toyId&&x.Kind=="toy");
            if(item is not null){bool liked=PetBehaviors.LikesToy(_modelId,item.Action);_animator.Play(PetBehaviors.ToyReaction(_modelId,item.Action));_hint.Text=PetModels.Catalog.First(x=>x.Id==_modelId).Name.Split(' ')[0]+(liked?"最喜欢":"也很喜欢")+item.Name+"，开心地回应了你。";_ballVisual.Content=null;Ui.Guard(()=>{_c.Store.RecordToyPlay(DateOnly.FromDateTime(DateTime.Now),toyId);_c.Refresh();});}
        }
        else
        {
            _animator.Play(id switch{"bell"=>PetAction.DanceSway,"book"=>PetAction.Read,"sleep" or "rest"=>PetAction.Rest,"feed" or "picnic"=>PetAction.Eat,"fish" or "pond"=>PetAction.Look,_=>PetAction.Pat});
            _hint.Text=id switch{"book"=>"翻开一本小书，今天的故事慢慢读。","feed"=>"吃到一口零食啦，不吃也不会变饿。","sleep"=>"小窝软软的，可以眯一会儿。","fish"=>"小鱼游过来打了个招呼。","pond"=>"坐在木桥旁，看水面和小小睡莲。","picnic"=>"一起坐下来，把忙碌放在一旁。","rest"=>"凉亭里有一小片安静。","bell"=>"风铃轻轻响了一下。",_=>"伙伴回应了你的互动。"};
            if(id is "book" or "sleep" or "feed" or "fish" or "lamp" or "fireplace")if(_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"furniture"))_c.Refresh();
            if(_outdoors&&id is "pond" or "picnic" or "rest" or "bell")if(_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"garden"))_c.Refresh();
        }
        if(id=="water")Ui.Guard(()=>{var day=DateOnly.FromDateTime(DateTime.Now);bool watered=_c.Store.Water(day);_hint.Text=watered?"浇好水啦，谢谢你来看看它。":"今天已经浇过水了，陪它待一会儿就好。";_c.Refresh();});
        if(id=="bell"&&_c.Preferences.Sounds&&!_c.Preferences.Quiet)System.Media.SystemSounds.Asterisk.Play();
    }
}
