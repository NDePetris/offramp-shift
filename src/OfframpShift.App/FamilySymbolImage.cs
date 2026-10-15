using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace OfframpShift.App;

// Decorative artwork; the adjacent text supplies the accessible app identity.
public sealed class FamilySymbolImage : Image
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
