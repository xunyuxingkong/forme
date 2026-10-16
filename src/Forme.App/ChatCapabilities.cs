using Forme.Core;

namespace Forme.App;

internal sealed partial class Controller
{
    private bool _chatStateRequest;
    internal ChatCapabilitySnapshot CaptureChatCapabilities()=>ChatCapabilitySnapshot.Capture(Preferences,CurrentScene?.Invoke()??SceneKind.Desktop,
        ActivityWorld is {} world?[world.Observe(CompanionView.Room),world.Observe(CompanionView.Desktop)]:[])
        with{Games=GoalPlayWorld is {} games?[games.ObserveGoalPlay("hide"),games.ObserveGoalPlay("boat")]:[]};
    internal string ChatRoutingInstructions(ChatCapabilitySnapshot snapshot)=>AiIntentProtocol.RoutingInstructions(snapshot);
}
