using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>Asks in which unit an imported model was designed (F-IMP-02). View code only; the decision is made by the view model.</summary>
public partial class ImportUnitWindow : Window
{
    public ImportUnitWindow(string modelName, double largestExtentInFile)
    {
        InitializeComponent();
        Question.Text = Texts.Format("Dlg.UnitQuestion", modelName, Format(largestExtentInFile));
        Label(MillimetersButton, ImportUnit.Millimeters, largestExtentInFile);
        Label(CentimetersButton, ImportUnit.Centimeters, largestExtentInFile);
        Label(InchesButton, ImportUnit.Inches, largestExtentInFile);

        // The unit that turns the model into the most plausible size is preselected: Enter accepts it.
        var best = new[] { ImportUnit.Millimeters, ImportUnit.Centimeters, ImportUnit.Inches }
            .OrderBy(u => Math.Abs(Math.Log10(Math.Max(largestExtentInFile * u.ToMillimeters(), 1e-9) / 100)))
            .First();
        var bestButton = ButtonOf(best);
        bestButton.IsDefault = true;
        Loaded += (_, _) => bestButton.Focus();
    }

    /// <summary>The chosen unit, or null when the dialog was cancelled.</summary>
    public ImportUnit? Choice { get; private set; }

    private Button ButtonOf(ImportUnit unit) => unit switch
    {
        ImportUnit.Centimeters => CentimetersButton,
        ImportUnit.Inches => InchesButton,
        _ => MillimetersButton,
    };

    private static void Label(Button button, ImportUnit unit, double extent)
    {
        button.Tag = unit;
        var name = unit switch
        {
            ImportUnit.Centimeters => Texts.Get("Unit.Centimeters"),
            ImportUnit.Inches => Texts.Get("Unit.Inches"),
            _ => Texts.Get("Unit.Millimeters"),
        };
        button.Content = Texts.Format("Dlg.UnitOption", name, Format(extent * unit.ToMillimeters()));
    }

    private static string Format(double value) =>
        value >= 100 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.###", CultureInfo.InvariantCulture);

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        Choice = (ImportUnit)((Button)sender).Tag;
        DialogResult = true;
    }
}
