using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Formwerk.App.Views;

/// <summary>Enter applies the typed value, Escape discards it (spec section 7: Enter confirms, Esc cancels).</summary>
public static class TextBoxBehavior
{
    public static readonly DependencyProperty CommitOnEnterProperty = DependencyProperty.RegisterAttached(
        "CommitOnEnter", typeof(bool), typeof(TextBoxBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetCommitOnEnter(DependencyObject d) => (bool)d.GetValue(CommitOnEnterProperty);

    public static void SetCommitOnEnter(DependencyObject d, bool value) => d.SetValue(CommitOnEnterProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        box.PreviewKeyDown -= OnKeyDown;
        if ((bool)e.NewValue) box.PreviewKeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        var binding = BindingOperations.GetBindingExpression(box, TextBox.TextProperty);
        if (e.Key == Key.Enter)
        {
            binding?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            binding?.UpdateTarget();
            e.Handled = true;
        }
    }
}
