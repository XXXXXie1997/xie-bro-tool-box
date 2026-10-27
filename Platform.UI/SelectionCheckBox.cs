using System.Windows.Controls;

namespace XieBox.Platform.UI;

/// <summary>Aggregate selection: clicking a partial selection selects all children.</summary>
public class SelectionCheckBox : CheckBox
{
    protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
}
