using Forme.Core;
using System.Text.Json;
using System.Windows;

namespace Forme.App;

internal sealed partial class MainWindow
{
    internal string ExecuteCreation(CompanionCommand command)
    {
        if(WindowState==WindowState.Minimized)throw new OperationFailureException("窗口已最小化，操作未执行");
        if(!CompanionCommands.Allowed(command,_c.Preferences))throw new OperationFailureException("权限已关闭");
        RevealWorld();
        if(command.Action is "layout" or "furniture")
        {
            if(_page!="furniture")Navigate("furniture");
            RevealWorld();
            if(command.Action=="layout"&&command.Value=="undo"){_room.UndoFurniture();return "已撤销上一项家具预览，尚未保存";}
            var next=_room.CurrentWorld.Copy();
            if(command.Action=="layout")
            {
                next=LivingWorld.Default() with{Discoveries=next.Discoveries};
                for(int i=0;i<next.Items.Count;i++)next.Items[i]=next.Items[i].Kind switch{"book"=>next.Items[i] with{X=.2,Z=0},"cushion"=>next.Items[i] with{X=-.8,Z=0},"lamp"=>next.Items[i] with{X=.9,Z=.5},_=>next.Items[i]};
            }
            else
            {
                int index=next.Items.FindIndex(i=>i.Id==command.Value);
                if(index<0)throw new OperationFailureException("家具编号已失效，请重新发送请求");
                next.Items[index]=next.Items[index] with{X=command.X!.Value,Z=command.Z!.Value,Rotation=command.Rotation??next.Items[index].Rotation};
            }
            // Validate the complete proposed layout before mutating the draft.
            if(next.PlacementProblem() is {} problem)throw new OperationFailureException(problem);
            _room.ChangeFurniture(next);
            return "已生成可撤销的家具预览；点击「保存家具布置」才会保存，离开此页取消草稿";
        }
        if(_room.EditingFurniture)throw new OperationFailureException("请先保存或取消家具预览，再启动玩法");
        if(command.Action.StartsWith("boat",StringComparison.Ordinal))
        {
            if(_page!="boat")Navigate("boat");
            RevealWorld();
            return _room.ExecuteBoatIntent(command);
        }
        if(command.Action=="hide")
        {
            if(_page!="hide")Navigate("hide");
            RevealWorld();
            _room.StartHide(command.Value=="pet-hides",_room.CurrentWorld.Items.FirstOrDefault()?.Id);
            return _room.HideStatus;
        }
        if(_page is "boat" or "hide")Navigate("living");
        RevealWorld();
        _room.SwitchScene(false);
        if(command.Action=="toy"){if(!_room.SelectToy(command.Value!)||!_room.PlaySelectedToy())throw new ActionFailureException(ActionResultCode.TargetUnavailable,"玩具尚未拥有或没有可达落点。");return "玩具已抛出，伙伴会去捡。";}
        if(command.Action=="throw"){if(!_room.ThrowToy(new(command.X!.Value,command.Z!.Value)))throw new OperationFailureException("落点被遮挡，抛球未执行");return "小球已抛出，伙伴会去捡";}
        var life=_room.AvailableLife().FirstOrDefault(e=>e.Id==command.Value)??throw new OperationFailureException("当前摆放或天气不满足该事件条件");
        if(!_room.UseFurniture(life.Target,life.Action))throw new OperationFailureException("事件物件无法到达");
        _room.RecordRequestedLife(life);
        return "已开始「"+life.Title+"」，到达后记录发现";
    }
    internal Task<bool> WaitCreation(CompanionCommand command,CancellationToken token)=>_room.WaitCreation(command,token);
}

internal sealed partial class Room3DView
{
    internal ActionResult? PreflightActions(ActionPlan plan)
    {
        if(_released||_c.AnimationSuspended)return ActionResult.Failure(ActionResultCode.SceneUnavailable,"场景已关闭或暂停，整组未执行。");
        if((_editing||_boatOpen)&&plan.Steps.Any(x=>x.Action is "go" or "stroll" or "interact"))return ActionResult.Failure(ActionResultCode.SceneUnavailable,"请先退出家具预览或纸船玩法，整组未执行。");
        var draft=CurrentWorld.Copy();
        foreach(var step in plan.Steps)
        {
            if(step.Action=="toy"&&!OwnedToys.Any(x=>x.Id==step.Value))return ActionResult.Failure(ActionResultCode.TargetUnavailable,"玩具尚未拥有，整组未执行。",step);
            if(step.Action=="hide"&&draft.Items.Count<2)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"至少摆两件家具才能藏物，整组未执行。",step);
            if(step.Action=="life"&&!AvailableLife().Any(x=>x.Id==step.Value))return ActionResult.Failure(ActionResultCode.TargetUnavailable,"当前环境不满足生活事件条件，整组未执行。",step);
            if(step.Action=="boat-name"&&LastBoat is null)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"请先完成纸船旅行，整组未执行。",step);
            if(step.Action=="boat"&&step.Value is "pause" or "resume"&&!BoatRunning)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"没有正在进行的纸船旅行，整组未执行。",step);
            if(step.Action=="furniture")
            {
                int index=draft.Items.FindIndex(x=>x.Id==step.Value);if(index<0)return ActionResult.Failure(ActionResultCode.TargetUnavailable,"家具编号不存在，整组未执行。",step);
                draft.Items[index]=draft.Items[index] with{X=step.X!.Value,Z=step.Z!.Value,Rotation=step.Rotation??draft.Items[index].Rotation};
                if(draft.PlacementProblem() is {} issue)return ActionResult.Failure(ActionResultCode.PathBlocked,issue+"，整组未执行。",step);
            }
        }
        return TargetNavigation.Preflight(plan,CurrentWorld,_outdoors?SceneKind.Outdoor:SceneKind.Indoor,_travel.Position);
    }
    internal string MoveNaturally(CompanionCommand command)
    {
        if(_editing||_boatOpen)throw new OperationFailureException("请先退出家具预览或纸船玩法，再让伙伴走动");
        GroundPoint? target;
        if(command.Action=="stroll")target=_travel.NearbyTarget(command.Value=="run"?3.5:1.1);
        else
        {
            var definition=TargetCatalog.Find(command.Value)??throw new ActionFailureException(ActionResultCode.TargetUnavailable,"目标不存在。");
            if(definition.FurnitureKind is {} kind&&!CurrentWorld.Items.Any(x=>x.Kind==kind))throw new ActionFailureException(ActionResultCode.TargetUnavailable,"目标家具已收纳。");
            target=TargetCatalog.Resolve(definition,CurrentWorld,_travel,_outdoors?SceneKind.Outdoor:SceneKind.Indoor);
        }
        if(target is null||!TryMove(target.Value))throw new ActionFailureException(ActionResultCode.PathBlocked,"目标附近没有可达位置，动作未执行。");
        _travel.SetGait(command.Action=="stroll"&&command.Value=="run"||command.Action=="go"&&command.Motion=="run");
        return command.Action=="stroll"?(command.Value=="run"?"伙伴开始短途奔跑，结束后停下":"伙伴开始走几步，结束后停下"):"伙伴正在去目标旁边";
    }
    internal IReadOnlyList<LifeEvent> AvailableLife()
    {
        var p=_c.Preferences;bool night=p.Theme=="night"||p.Theme=="auto"&&(DateTime.Now.Hour>=19||DateTime.Now.Hour<7);
        return LifeRules.Evaluate(CurrentWorld,EnvironmentContext.From(DateTime.Now,p.Weather,p.Theme,p.RoomLamp));
    }
    internal void RecordRequestedLife(LifeEvent life)
    {if(_pendingInteraction is not null)_pendingLife=life.Id;else{_c.Store.DiscoverLife(life.Id);_c.Store.RecordGameAction(DateOnly.FromDateTime(DateTime.Now),"life");_c.Refresh();}_hint.Text=life.Title+"："+life.Description;}
    internal string CreationContext()
    {
        var p=_c.Preferences;var parts=new List<string>{SceneDescription};
        if(p.AllowPlayControl)parts.Add(JsonSerializer.Serialize(new{events=AvailableLife().Select(e=>e.Id),hide=HideActive?HideStatus:"未开始",boat=_boatOpen?new{layout=BoatSettings,running=BoatRunning,paused=BoatPaused,title=LastBoat?.Title}:null}));
        if(p.AllowLayoutPreview)parts.Add(JsonSerializer.Serialize(new{draft=_editing,furniture=CurrentWorld.Items.Select(i=>new{i.Id,i.Kind,i.X,i.Z,i.Rotation})}));
        return string.Join("；",parts);
    }
    internal string ExecuteBoatIntent(CompanionCommand c)
    {
        switch(c.Action)
        {
            case "boat":
                switch(c.Value)
                {
                    case "open":return "纸船场景已打开，可以调整并放船";
                    case "start":StartBoat();return BoatStatus;
                    case "pause":if(!BoatRunning)throw new OperationFailureException("没有正在进行的纸船旅行");if(!BoatPaused)PauseBoat();return BoatStatus;
                    case "resume":if(!BoatRunning)throw new OperationFailureException("没有可继续的纸船旅行");if(BoatPaused)PauseBoat();return BoatStatus;
                    default:RetrieveBoat();return "纸船已捞回";
                }
            case "boat-name":if(LastBoat is null)throw new OperationFailureException("请先完成一次纸船旅行");NameLastBoat(c.Value!);BoatChanged?.Invoke();return "已为当前旅行命名；尚未收藏，点击收藏才保存";
        }
        var layout=BoatSettings;
        layout=c.Action switch
        {
            "boat-shape"=>layout with{Shape=c.Value!},
            "boat-color"=>layout with{Color=c.Value!},
            "boat-wind"=>layout with{Wind=c.Value=="left"?-1:c.Value=="right"?1:0},
            _=>layout
        };
        if(c.Action=="boat-leaf")layout.Leaves[int.Parse(c.Value!)-1]=new(c.X!.Value,c.Z!.Value,c.Rotation!.Value);
        ConfigureBoat(layout);return "纸船配置和路线预览已更新，可以撤销；尚未放船";
    }
    internal async Task<bool> WaitCreation(CompanionCommand command,CancellationToken token)
    {
        if(command.Action is not ("move" or "stroll" or "go" or "interact" or "life" or "throw" or "toy" or "recall" or "dance" or "pat" or "rub"))return true;
        int generation=_activityGeneration;
        string model=_modelId;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(_released||!IsVisible||!_motionVisible||_c.AnimationSuspended||generation!=_activityGeneration||model!=_modelId)return false;
            if(!CompanionCommands.Allowed(command,_c.Preferences)){StopWorld();return false;}
            if(!_travel.Moving&&_pendingInteraction is null&&!ToyRunning&&!_animator.Reacting)return true;
            await Task.Delay(100,token);
        }
    }
}
