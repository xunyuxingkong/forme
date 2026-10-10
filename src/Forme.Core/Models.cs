namespace Forme.Core;

public sealed record Preferences
{
    public string PetName { get; set; } = "团团";
    public string PetModel { get; set; } = "sprout";
    public string PerformanceMode { get; set; } = "balanced";
    public string Weather { get; set; } = "clear";
    public bool RoomLamp { get; set; } = true;
    public bool Fireplace { get; set; }
    public string UserName { get; set; } = "";
    public bool Onboarded { get; set; }
    public bool Quiet { get; set; }
    public bool ReducedMotion { get; set; }
    public bool Topmost { get; set; }
    public bool Sounds { get; set; }
    public bool TimerSounds { get; set; }
    public bool Notifications { get; set; } = true;
    public bool Greetings { get; set; }
    public int GreetingStart { get; set; } = 9;
    public int GreetingEnd { get; set; } = 21;
    public string GreetingDay { get; set; } = "";
    public int GreetingCount { get; set; }
    public DateTimeOffset? LastGreeting { get; set; }
    public int FocusMinutes { get; set; } = 25;
    public int RestMinutes { get; set; } = 5;
    public double PetScale { get; set; } = 1;
    public double PetX { get; set; } = -1;
    public double PetY { get; set; } = -1;
    public int PetPositionVersion {get;set;}
    public string DisplayMode { get; set; } = "pet";
    public string PetIdleMode { get; set; } = "idle";
    public string Theme { get; set; } = "auto";
    public string AppBackground { get; set; } = "#F7F5EE";
    public bool ShowHome3D { get; set; } = true;
    public string Rug { get; set; } = "cream";
    public string Ornament { get; set; } = "none";
    public string ReplyStyle { get; set; } = "简短、温和，略有幽默";
    public string CharacterPreset { get; set; } = "companion";
    public string RoleDescription { get; set; } = "";
    public bool MemoryEnabled { get; set; }
    public bool AllowPetControl { get; set; }
    public bool AllowSceneControl { get; set; }
    public bool AllowPlayControl { get; set; }
    public bool AllowLayoutPreview { get; set; }
    public bool LocalActionRules { get; set; } = true;
    public int ContextBudget { get; set; } = 4096;
    public int MaxReplyTokens { get; set; } = 512;
    public double Temperature { get; set; } = .7;
    public string Endpoint { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PetName)||UserName is null||ReplyStyle is null||RoleDescription is null||PetName.Length is < 1 or > 20 || UserName.Length > 30 || ReplyStyle.Length > 400 || RoleDescription.Length > 1200)
            throw new InvalidDataException("名字或回复偏好过长。");
        if(!new[]{"sprout","cat","fox","penguin"}.Contains(PetModel)||!new[]{"balanced","economy"}.Contains(PerformanceMode)||!new[]{"clear","rain","snow"}.Contains(Weather)||
            !new[]{"companion","focus","story","custom"}.Contains(CharacterPreset)||ContextBudget is not (2048 or 4096 or 8192)||MaxReplyTokens is <128 or >1024||!double.IsFinite(Temperature)||Temperature is <0 or >1)
            throw new InvalidDataException("模型、场景或 AI 个性参数无效。");
        if (FocusMinutes is < 1 or > 180 || RestMinutes is < 1 or > 180 ||
            PetScale is < 0.7 or > 1.6 || !double.IsFinite(PetX) || !double.IsFinite(PetY) ||
            PetPositionVersion is <0 or >1 ||
            GreetingStart is < 0 or > 23 || GreetingEnd is < 1 or > 24 || GreetingEnd <= GreetingStart)
            throw new InvalidDataException("时长、尺寸或允许时段无效。");
        if (!System.Text.RegularExpressions.Regex.IsMatch(AppBackground??"", "^#[0-9A-Fa-f]{6}$") ||
            !new[] { "idle", "sleep", "walk", "run" }.Contains(PetIdleMode) || !new[] { "pet", "tray", "edge" }.Contains(DisplayMode) || !new[] { "auto", "day", "night" }.Contains(Theme) ||
            !new[] { "cream", "sage", "rose" }.Contains(Rug) ||
            !new[] { "none", "star", "cloud", "flower" }.Contains(Ornament))
            throw new InvalidDataException("房间选项无效。");
        AiClient.ValidateEndpoint(Endpoint);
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 100)
            throw new InvalidDataException("模型名称无效。");
    }
}

public sealed record MoodEntry(string Id, string Mood, string Note, DateTimeOffset Created, DateTimeOffset Updated);
public sealed record ChatSession(string Id, string Title, DateTimeOffset Created, string Endpoint);
public sealed record ChatMessage(string Id, string SessionId, string Role, string Content, string Status, DateTimeOffset Created);
public sealed record ChatMemory(string Id,string Title,string Content,string Provider,bool Enabled,DateTimeOffset Created,DateTimeOffset Updated);
public sealed record ChatNote(string SessionId,string Content,string Provider);
public sealed record FocusEntry(string Id, string Kind, string Title, DateTimeOffset Started, int TargetSeconds, double ElapsedSeconds, string Result);
public sealed record ActivitySnapshot(string Id, string Kind, string Title, DateTimeOffset Started, int TargetSeconds, double ElapsedSeconds);
public sealed record GrowthEvent(string Id, string Type, string Day);
public sealed record GameProgress(int Experience,int Level,int Stars);
public sealed record GameTask(string Id,string Title,string Hint,int RewardXp,int RewardStars,bool Completed);
public sealed record GameTaskDefinition(string Id,string Title,string Hint,int RewardXp,int RewardStars);
public sealed record GameItem(string Id,string Name,string Kind,int Price,int MaxOwned);
public sealed record GameAchievementDefinition(string Id,string Name,string Description);
public sealed record GameInventoryItem(string ItemId,int Quantity);
public sealed record GameDiscovery(string ItemId,string Day);
public sealed record GameAchievement(string Id,string Day);
public sealed class ExportDocument
{
    public int SchemaVersion { get; set; } = 6;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatSession>? Sessions { get; set; }
    public List<ChatMessage>? Messages { get; set; }
    public List<ChatMemory>? Memories { get; set; }
    public List<ChatNote>? Notes { get; set; }
    public List<MoodEntry>? Moods { get; set; }
    public List<FocusEntry>? Focus { get; set; }
    public RoomExport? Room { get; set; }
}
public sealed record GameTaskRecord(string Day,string TaskId);
public sealed record GameDailyRecord(string Day,int Experience);
public sealed record GameSlot(string SlotId,string ItemId);
public sealed record GameExport(int Experience,int Stars,List<GameInventoryItem> Inventory,List<GameTaskRecord> Tasks,List<GameDailyRecord> Daily,List<GameDiscovery> Discoveries,List<GameSlot> Slots,List<GameAchievement> Achievements);
public sealed record RoomExport(string PetName, string Theme, string Rug, string Ornament, List<GrowthEvent> Events)
{
    public LivingWorld? World {get;init;}
    public BoatAlbum? Boats {get;init;}
    public GameExport? Game {get;init;}
    public string PetModel {get;init;}="sprout";
    public string Weather {get;init;}="clear";
    public bool RoomLamp {get;init;}=true;
    public bool Fireplace {get;init;}
}
