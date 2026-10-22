using System.Windows;
using System.Windows.Threading;
using Formwerk.App.ViewModels;
using Formwerk.App.Views;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.App;

public partial class App : Application
{
    private DispatcherTimer? _autosaveTimer;

    /// <summary>Composition root: the only place that creates the concrete kernel, dialogs and autosave location.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var viewModel = new MainViewModel(new ManifoldKernel(), new DialogService(), new AutosaveStore(AutosaveStore.DefaultDirectory),
            userSettings: new UserSettingsStore(UserSettingsStore.DefaultPath), slicerLauncher: new SlicerLauncher(),
            templates: new Formwerk.IO.TemplateStore(Formwerk.IO.TemplateStore.DefaultFolder), templatePreview: new TemplatePreviewRenderer(),
            libraryThumbnailFolder: System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Formwerk", "library-thumbnails"));
        _ = viewModel.GenerateLibraryThumbnailsAsync(); // the pictures of the library, one at a time in the background
        ThemeManager.Apply(this, viewModel.LightTheme);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.LightTheme)) ThemeManager.Apply(this, viewModel.LightTheme);
        };
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        // A leftover autosave means the last session did not end normally.
        viewModel.OfferRecovery();

        // The interval is a setting (F-SET-02, default 2 minutes, E-05); a change in the dialog applies at once.
        _autosaveTimer = new DispatcherTimer(TimeSpan.FromMinutes(viewModel.AutosaveMinutes), DispatcherPriority.Background,
            async (_, _) => await viewModel.AutosaveAsync(), Dispatcher);
        _autosaveTimer.Start();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.AutosaveMinutes))
                _autosaveTimer.Interval = TimeSpan.FromMinutes(viewModel.AutosaveMinutes);
        };
    }
}
