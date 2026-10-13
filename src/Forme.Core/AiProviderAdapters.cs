namespace Forme.Core;

public enum AiCapabilitySupport { Supported, Unsupported, ModelDependent, Unknown }

public sealed record AiProviderCapabilities(
    AiCapabilitySupport Images,
    AiCapabilitySupport NativePdf,
    AiCapabilitySupport ToolCalling,
    AiCapabilitySupport Thinking,
    AiCapabilitySupport ReasoningEffort,
    AiCapabilitySupport ThinkingBudget,
    IReadOnlyList<string> ImageFormats);

internal interface IAiProviderAdapter
{
    string Id { get; }
    AiProviderCapabilities Capabilities { get; }
    bool IncludeImageDetail { get; }
    bool IsZhipu { get; }
    bool IsSiliconFlowGlm { get; }
    bool RequiresRenderedPdf { get; }
    void ValidateAttachments(IReadOnlyList<AiAttachment> attachments);
    void ApplyRequestOptions(IDictionary<string, object> payload, Preferences settings);
}

internal static class AiProviderAdapters
{
    private static readonly IAiProviderAdapter Custom = new OpenAiCompatibleAdapter();

    public static IAiProviderAdapter Resolve(string endpoint, string model)
    {
        var uri = AiClient.ValidateEndpoint(endpoint);
        if (AiClient.IsZhipuEndpoint(endpoint)) return new ZhipuOfficialAdapter(model);
        if (uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)) return new DeepSeekAdapter();
        if (uri.Host.Equals("api.siliconflow.cn", StringComparison.OrdinalIgnoreCase)) return new SiliconFlowAdapter(endpoint, model);
        return Custom;
    }

    private abstract class AdapterBase : IAiProviderAdapter
    {
        public abstract string Id { get; }
        public abstract AiProviderCapabilities Capabilities { get; }
        public virtual bool IncludeImageDetail => true;
        public virtual bool IsZhipu => false;
        public virtual bool IsSiliconFlowGlm => false;
        public virtual bool RequiresRenderedPdf => false;

        public virtual void ValidateAttachments(IReadOnlyList<AiAttachment> attachments)
        {
            if (RequiresRenderedPdf && attachments.Any(x => x.MediaType == "application/pdf" && !x.IsRenderedPdf))
                throw new InvalidDataException("DeepSeek需将PDF转为页面图片。请移除并重新添加PDF，本地转换后再发送；未联网。");
        }

        public virtual void ApplyRequestOptions(IDictionary<string, object> payload, Preferences settings) { }

        protected static AiProviderCapabilities UnknownCapabilities { get; } = new(
            AiCapabilitySupport.Unknown, AiCapabilitySupport.Unknown, AiCapabilitySupport.Unsupported,
            AiCapabilitySupport.Unknown, AiCapabilitySupport.Unknown, AiCapabilitySupport.Unknown, []);
    }

    private sealed class OpenAiCompatibleAdapter : AdapterBase
    {
        public override string Id => "openai-compatible";
        public override AiProviderCapabilities Capabilities => UnknownCapabilities;
    }

    private sealed class DeepSeekAdapter : AdapterBase
    {
        public override string Id => "deepseek";
        public override bool RequiresRenderedPdf => true;
        public override AiProviderCapabilities Capabilities => UnknownCapabilities with
        {
            NativePdf = AiCapabilitySupport.Unsupported,
            Thinking = AiCapabilitySupport.Supported
        };

        public override void ApplyRequestOptions(IDictionary<string, object> payload, Preferences settings) =>
            payload["thinking"] = new { type = "disabled" };
    }

    private sealed class SiliconFlowAdapter(string endpoint, string model) : AdapterBase
    {
        private bool IsGlm => AiClient.IsSiliconFlowGlm(endpoint, model);
        private bool IsDeepSeekV32 => model is "deepseek-ai/DeepSeek-V3.2" or "Pro/deepseek-ai/DeepSeek-V3.2";
        public override string Id => "siliconflow";
        public override bool IsSiliconFlowGlm => IsGlm;
        public override AiProviderCapabilities Capabilities => UnknownCapabilities with
        {
            Thinking = IsGlm || IsDeepSeekV32 ? AiCapabilitySupport.Supported : AiCapabilitySupport.Unknown,
            ThinkingBudget = IsGlm ? AiCapabilitySupport.Supported : AiCapabilitySupport.Unsupported,
            ReasoningEffort = model == "Pro/zai-org/GLM-5.2" ? AiCapabilitySupport.Supported : AiCapabilitySupport.Unsupported
        };

        public override void ApplyRequestOptions(IDictionary<string, object> payload, Preferences settings)
        {
            if (IsGlm)
            {
                payload["enable_thinking"] = true;
                payload["thinking_budget"] = AiClient.SiliconFlowThinkingBudget(settings.ZhipuReasoningEffort);
                // SiliconFlow documents reasoning_effort for this GLM model only.
                if (model == "Pro/zai-org/GLM-5.2")
                    payload["reasoning_effort"] = settings.ZhipuReasoningEffort == "max" ? "max" : "high";
            }
            else if (IsDeepSeekV32) payload["enable_thinking"] = false;
        }
    }

    private sealed class ZhipuOfficialAdapter(string model) : AdapterBase
    {
        private static readonly string[] ThinkingModels = ["glm-5.3", "glm-5.3-flash", "glm-5.3-flashx"];
        public override string Id => "zhipu";
        public override bool IsZhipu => true;
        public override bool IncludeImageDetail => false;
        public override AiProviderCapabilities Capabilities => UnknownCapabilities with
        {
            Images = AiCapabilitySupport.Supported,
            Thinking = ThinkingModels.Contains(model, StringComparer.Ordinal) ? AiCapabilitySupport.Supported : AiCapabilitySupport.Unknown,
            ReasoningEffort = ThinkingModels.Contains(model, StringComparer.Ordinal) ? AiCapabilitySupport.Supported : AiCapabilitySupport.Unknown,
            ImageFormats = ["image/png", "image/jpeg"]
        };

        public override void ValidateAttachments(IReadOnlyList<AiAttachment> attachments)
        {
            var allowedFormats = Capabilities.ImageFormats;
            if (attachments.SelectMany(x => x.IsRenderedPdf ? x.Pages : new[] { x })
                .Any(x => x.IsImage && !allowedFormats.Contains(x.MediaType, StringComparer.Ordinal)))
                throw new InvalidDataException("智谱官方图片输入请使用PNG或JPEG；WebP请先自行转换后重新添加，未联网。");
        }

        public override void ApplyRequestOptions(IDictionary<string, object> payload, Preferences settings)
        {
            if (!ThinkingModels.Contains(model, StringComparer.Ordinal)) return;
            payload["thinking"] = new { type = "enabled", clear_thinking = true };
            payload["reasoning_effort"] = settings.ZhipuReasoningEffort;
        }
    }
}
