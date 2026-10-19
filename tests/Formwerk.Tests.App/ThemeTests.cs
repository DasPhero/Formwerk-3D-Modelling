using System.IO;
using System.Windows.Media;
using Formwerk.App.ViewModels;
using Formwerk.App.Views;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Dark theme by default, light theme optional (F-VIS-07).</summary>
public sealed class ThemeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-theme-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    // ---- palettes ------------------------------------------------------------------------------------

    [Fact]
    public void The_dark_palette_is_the_one_of_the_spec_greys_and_one_accent()
    {
        var dark = ThemePalette.Dark;

        Assert.Equal(Color.FromRgb(0x1E, 0x1F, 0x22), dark.Window);
        Assert.Equal(Color.FromRgb(0x4C, 0x9A, 0xFF), dark.Accent);
        Assert.True(Luminance(dark.Window) < Luminance(dark.Text));
    }

    [Fact]
    public void The_light_palette_is_light_with_dark_text()
    {
        var light = ThemePalette.Light;

        Assert.True(Luminance(light.Window) > 0.7);
        Assert.True(Luminance(light.Panel) > 0.7);
        Assert.True(Luminance(light.Text) < 0.1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_and_signal_colors_are_readable_on_the_window_and_the_panels(bool light)
    {
        var palette = light ? ThemePalette.Light : ThemePalette.Dark;

        foreach (var background in new[] { palette.Window, palette.Panel })
        {
            Assert.True(Contrast(palette.Text, background) >= 7, $"text {Contrast(palette.Text, background):0.0}");
            Assert.True(Contrast(palette.MutedText, background) >= 4.5, $"muted text {Contrast(palette.MutedText, background):0.0}");
            Assert.True(Contrast(palette.Error, background) >= 4.5, $"error {Contrast(palette.Error, background):0.0}");
            Assert.True(Contrast(palette.Accent, background) >= 3, $"accent {Contrast(palette.Accent, background):0.0}");
        }
    }

    [Fact]
    public void The_two_palettes_differ_in_every_color_of_the_surface()
    {
        Assert.NotEqual(ThemePalette.Dark.Window, ThemePalette.Light.Window);
        Assert.NotEqual(ThemePalette.Dark.Panel, ThemePalette.Light.Panel);
        Assert.NotEqual(ThemePalette.Dark.PanelBorder, ThemePalette.Light.PanelBorder);
        Assert.NotEqual(ThemePalette.Dark.Text, ThemePalette.Light.Text);
        Assert.NotEqual(ThemePalette.Dark.MutedText, ThemePalette.Light.MutedText);
    }

    [UiFact]
    public void Applying_a_palette_gives_every_brush_the_colors_of_the_theme_and_back()
    {
        var resources = new System.Windows.ResourceDictionary();

        ThemeManager.ApplyPalette(resources, light: true);
        Assert.Equal(ThemePalette.Light.Window, ((SolidColorBrush)resources["WindowBrush"]).Color);
        Assert.Equal(ThemePalette.Light.Panel, ((SolidColorBrush)resources["PanelBrush"]).Color);
        Assert.Equal(ThemePalette.Light.PanelBorder, ((SolidColorBrush)resources["PanelBorderBrush"]).Color);
        Assert.Equal(ThemePalette.Light.Text, ((SolidColorBrush)resources["TextBrush"]).Color);
        Assert.Equal(ThemePalette.Light.MutedText, ((SolidColorBrush)resources["MutedTextBrush"]).Color);
        Assert.Equal(ThemePalette.Light.Accent, ((SolidColorBrush)resources["AccentBrush"]).Color);
        Assert.Equal(ThemePalette.Light.Error, ((SolidColorBrush)resources["ErrorBrush"]).Color);

        ThemeManager.ApplyPalette(resources, light: false);
        Assert.Equal(ThemePalette.Dark.Window, ((SolidColorBrush)resources["WindowBrush"]).Color);
        Assert.Equal(ThemePalette.Dark.Text, ((SolidColorBrush)resources["TextBrush"]).Color);
    }

    [Fact]
    public void All_xaml_refers_to_the_palette_brushes_dynamically_so_that_a_switch_reaches_open_windows()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Formwerk.App"));
        var files = Directory.GetFiles(root, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"\{StaticResource (Window|Panel|PanelBorder|Text|MutedText|Accent|Error)Brush\}", text);
        }
    }

    // ---- the setting ---------------------------------------------------------------------------------

    private MainViewModel NewViewModel(UserSettingsStore? store = null) =>
        new(new ManifoldKernel(), new FakeDialogs(), userSettings: store);

    [UiFact]
    public void The_dark_theme_is_the_default()
    {
        Assert.False(NewViewModel().LightTheme);
        Assert.False(NewViewModel().CurrentUserSettings().LightTheme);
    }

    [UiFact]
    public void The_choice_is_remembered_between_sessions()
    {
        var store = new UserSettingsStore(Path.Combine(_folder, "settings.json"));
        var vm = NewViewModel(store);

        vm.LightTheme = true;

        Assert.True(store.Load().LightTheme);
        Assert.True(NewViewModel(new UserSettingsStore(store.Path)).LightTheme);
    }

    [UiFact]
    public void The_toggle_command_switches_back_and_forth()
    {
        var vm = NewViewModel();

        vm.ToggleThemeCommand.Execute(null);
        Assert.True(vm.LightTheme);

        vm.ToggleThemeCommand.Execute(null);
        Assert.False(vm.LightTheme);
    }

    [UiFact]
    public void The_settings_dialog_can_choose_the_theme()
    {
        var vm = NewViewModel();
        var dialog = new SettingsViewModel(vm.Document.Settings.Clone(), vm.CurrentUserSettings());
        Assert.False(dialog.LightTheme);

        dialog.LightTheme = true;
        vm.ApplySettings(dialog.Result()!);

        Assert.True(vm.LightTheme);
    }

    [UiFact]
    public void Switching_the_theme_is_not_a_document_change()
    {
        var vm = NewViewModel();

        vm.LightTheme = true;

        Assert.False(vm.IsDirty);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }
}
