using Forme.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Forme.Tests;

[TestClass]
public sealed class AiProviderAdapterTests
{
    [TestMethod]
    public void ProviderCapabilitiesKeepKnownLimitsSeparateFromUnknownModelSupport()
    {
        var zhipu=AiClient.ProviderCapabilities(AiClient.ZhipuEndpoint,AiClient.ZhipuModel);
        Assert.AreEqual(AiCapabilitySupport.Supported,zhipu.Images);
        CollectionAssert.AreEqual(new[]{"image/png","image/jpeg"},zhipu.ImageFormats.ToArray());
        Assert.AreEqual(AiCapabilitySupport.Unsupported,zhipu.ToolCalling,"Forme does not dispatch generic tool calls yet.");

        var deepseek=AiClient.ProviderCapabilities("https://api.deepseek.com","deepseek-v4-flash");
        Assert.AreEqual(AiCapabilitySupport.Unsupported,deepseek.NativePdf);
        Assert.IsTrue(AiClient.RequiresPdfImages("https://api.deepseek.com"));

        var siliconGlm=AiClient.ProviderCapabilities("https://api.siliconflow.cn/v1","zai-org/GLM-5.3-Flash");
        Assert.AreEqual(AiCapabilitySupport.Supported,siliconGlm.ThinkingBudget);

        var custom=AiClient.ProviderCapabilities("https://example.com/v1","some-model");
        Assert.AreEqual(AiCapabilitySupport.Unknown,custom.Images);
        Assert.AreEqual(AiCapabilitySupport.Unknown,custom.NativePdf);
    }
}
