namespace Forme.Core;

public sealed record PetBehavior(string FavoriteFurniture,string FavoriteToy,string FavoriteActivity,string Personality);

public static class PetBehaviors
{
    public static PetBehavior For(string model)=>model switch
    {
        "cat"=>new("fish","feather-mint","观察游鱼和追羽毛","奶糖爱观察鱼缸，也会先追羽毛、毛线和铃铛。"),
        "fox"=>new("book","paper-plane","追纸飞机和探索角落","栗子喜欢追纸飞机、探索新角落，找到东西会兴奋地转圈。"),
        "penguin"=>new("fish","frisbee-sky","池塘边玩飞盘","雪球喜欢池塘和飞盘，接到玩具会摇摇摆摆庆祝。"),
        "vesna"=>new("garden","paper-plane","巡林与追逐纸飞机","巡林游侠喜欢观察花园里的新鲜事，也会追逐纸飞机。"),
        "vodyanitsa"=>new("window","bell","安静巡游与聆听风铃","星辉牧师喜欢在窗边安静休息，聆听风铃的声音。"),
        "odette"=>new("cushion","ball-yellow","练习投掷与守护小屋","守誓战士喜欢活动筋骨，也乐意守护小屋和伙伴。"),
        _=>new("cushion","ball-yellow","抱着小球和坐垫休息","芽芽喜欢小球、柔软坐垫和安静的花园时光。")
    };

    public static bool LikesToy(string model,string action)=>model switch
    {
        "cat"=>action is "feather" or "yarn" or "bell",
        "fox"=>action is "plane" or "yarn",
        "penguin"=>action is "frisbee" or "bell",
        "vesna"=>action is "yarn" or "plane",
        "vodyanitsa"=>action is "ball" or "frisbee" or "bell",
        "odette"=>action is "plane" or "feather",
        _=>action is "ball" or "yarn"
    };

    public static PetAction ToyReaction(string model,string action)
    {
        bool favorite=LikesToy(model,action);
        return model switch
        {
            "cat"=>favorite?PetAction.DanceHop:PetAction.Look,
            "fox"=>favorite?PetAction.DanceSpin:PetAction.Cheer,
            "penguin"=>favorite?PetAction.DanceSway:PetAction.Look,
            "vesna"=>favorite?PetAction.DanceHop:PetAction.Cheer,
            "vodyanitsa"=>favorite?PetAction.Cheer:PetAction.Pat,
            "odette"=>favorite?PetAction.DanceSpin:PetAction.Look,
            _=>favorite?PetAction.Cheer:PetAction.Pat
        };
    }
}
