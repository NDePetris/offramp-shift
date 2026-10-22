using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace OfframpShift.App;

// The adjacent context text supplies the accessible meaning of this glyph.
public sealed class ContextIcon : TextBlock
{
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
