using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ProductivityIntentTests
{
    [TestMethod]
    public void ExplicitLocalProductivityIntentsAreStrictlyParsed()
    {
        var due=DateTimeOffset.Now.AddHours(3).ToString("O");
        var note=ProductivityIntentProtocol.Parse("[{\"intent\":\"CreateNote\",\"title\":\"升级检查\",\"content\":\"先备份\",\"tags\":[\"发布\"]}]","帮我记一下先备份");
        Assert.AreEqual(ProductivityIntentType.CreateNote,note.Single().Type);Assert.AreEqual("发布",note.Single().Tags!.Single());
        var task=ProductivityIntentProtocol.Parse("[{\"intent\":\"CreateTask\",\"title\":\"测试安装包\",\"description\":\"\",\"dueAt\":\""+due+"\",\"remindAt\":null,\"priority\":\"important\"}]","明天测试安装包");
        Assert.AreEqual("important",task.Single().Priority);Assert.IsNotNull(task.Single().DueAt);
        Assert.IsTrue(ProductivityIntentProtocol.TryExtract("```forme-productivity\n[]\n```",out var json));Assert.AreEqual("[]",json);
        Assert.IsTrue(AiIntentProtocol.RoutingInstructions().Contains(ProductivityIntentProtocol.Marker));
        Assert.ThrowsExactly<InvalidDataException>(()=>ProductivityIntentProtocol.Parse("[{\"intent\":\"DeleteNote\"}]","删掉记事"));
        Assert.ThrowsExactly<InvalidDataException>(()=>ProductivityIntentProtocol.Parse("[{\"intent\":\"CreateReminder\",\"title\":\"开会\",\"dueAt\":null}]","提醒我开会"));
        Assert.ThrowsExactly<InvalidDataException>(()=>ProductivityIntentProtocol.TryExtract("解释```forme-productivity\n[]\n```",out _));
    }
}
