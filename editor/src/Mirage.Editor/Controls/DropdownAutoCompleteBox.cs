using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mirage.Editor.Models;
using Mirage.Editor.ViewModels;
namespace Mirage.Editor.Controls;

/// <summary>
/// An <see cref="AutoCompleteBox"/> that behaves like a combo box: the list drops open on click or
/// Tab focus rather than only after typing, and stale text is cleared when the selection is reset.
/// <para>Keeps the base control's style key so it picks up the stock AutoCompleteBox theme.</para>
/// </summary>
public class DropdownAutoCompleteBox : AutoCompleteBox
{
    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    // Set when the user explicitly selects the id=0 "(none)" entry, so the binding-driven
    // null that immediately follows still clears the text box even while the control has focus.
    private bool _clearTextOnNextNull;

    public DropdownAutoCompleteBox()
    {
        // Only open on Tab navigation. Pointer-driven GotFocus events (including focus
        // returning from the popup after an item is selected) must not reopen the dropdown.
        GotFocus += (_, e) =>
        {
            if (e.NavigationMethod == NavigationMethod.Tab)
                Dispatcher.UIThread.Post(() => IsDropDownOpen = true);
        };

        // Capture IsDropDownOpen synchronously at press time, before AutoCompleteBox
        // runs its selection logic. If the dropdown was already open the press landed on
        // a list item — AutoCompleteBox will close it; do not reopen in the deferred post.
        AddHandler(PointerPressedEvent,
            (_, _) =>
            {
                bool wasOpen = IsDropDownOpen;
                Dispatcher.UIThread.Post(() => { if (!wasOpen) IsDropDownOpen = true; });
            },
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>Watches <c>SelectedItem</c> so the text box doesn't keep a stale caption after the
    /// selection is cleared — either by a binding reset or by the author picking "(none)".</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SelectedItemProperty) return;

        if (change.NewValue is null)
        {
            // Clear stale text when:
            //   (a) focus is elsewhere — binding-driven reset (e.g. switching selected map), or
            //   (b) the user just explicitly selected the id=0 "(none)" entry.
            if (!IsKeyboardFocusWithin || _clearTextOnNextNull)
                Text = string.Empty;
            _clearTextOnNextNull = false;
        }
        else if (change.NewValue is NamedEntry { Id: 0 })
        {
            // "(none)" was picked — the binding will immediately set SelectedItem back to
            // null (EntryFor returns null for id=0). Flag so that null-change clears the text.
            _clearTextOnNextNull = true;
        }
    }
}
