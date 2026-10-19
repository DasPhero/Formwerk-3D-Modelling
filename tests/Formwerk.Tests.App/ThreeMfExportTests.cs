using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Xml.Linq;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Export to 3MF and "Open in Slicer" through the view model (E-03).</summary>
public sealed class ThreeMfExportTests : IDisposable
{
    private static readonly XNamespace Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-3mf-app-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSlicer _slicer = new();
    private readonly List<string> _tempFiles = [];

    public ThreeMfExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        foreach (var file in _tempFiles) File.Delete(file);
    }

    private UserSettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private MainViewModel NewViewModel(UserSettingsStore? store = null) =>
        new(new ManifoldKernel(), _dialogs, userSettings: store ?? Store(), slicerLauncher: _slicer);

    private string FakeProgram(string name = "slicer.exe")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "not a real program");
        return path;
    }

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static List<XElement> ObjectsOf(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("3D/3dmodel.model")!.Open();
        return XDocument.Load(entry).Root!.Element(Core + "resources")!.Elements(Core + "object").ToList();
    }

    private static double VolumeOf(XElement obj)
    {
        var mesh = obj.Element(Core + "mesh")!;
        var vertices = mesh.Element(Core + "vertices")!.Elements().Select(v => new Vector3(Num(v, "x"), Num(v, "y"), Num(v, "z"))).ToList();
        return mesh.Element(Core + "triangles")!.Elements().Sum(t =>
            Vector3.Dot(vertices[Idx(t, "v1")], Vector3.Cross(vertices[Idx(t, "v2")], vertices[Idx(t, "v3")])) / 6.0);
    }

    private static float Num(XElement e, string a) => float.Parse(e.Attribute(a)!.Value, System.Globalization.CultureInfo.InvariantCulture);

    private static int Idx(XElement e, string a) => int.Parse(e.Attribute(a)!.Value, System.Globalization.CultureInfo.InvariantCulture);

    // ---- export 3MF ------------------------------------------------------------------------------

    [UiFact]
    public async Task Exporting_writes_every_solid_as_an_object_with_name_and_color()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        vm.Document.Root.Children[0].Color = 0xFF112233;
        vm.Document.Root.Children[1].Color = 0xFFAA5500;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var path = Path.Combine(_folder, "out.3mf");
        _dialogs.SavePath = path;

        await vm.Export3mfCommand.ExecuteAsync(null);

        var bytes = File.ReadAllBytes(path);
        var objects = ObjectsOf(bytes);
        Assert.Equal(["Box", "Cylinder"], objects.Select(o => o.Attribute("name")!.Value).ToArray());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var model = new StreamReader(zip.GetEntry("3D/3dmodel.model")!.Open());
        var text = model.ReadToEnd();
        Assert.Contains("#112233FF", text);
        Assert.Contains("#AA5500FF", text);
        Assert.Contains("Exported 2 object(s)", vm.StatusText);
        Assert.Contains("out.3mf", vm.StatusText);
    }

    [UiFact]
    public async Task The_file_name_gets_the_3mf_extension_when_it_is_missing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        _dialogs.SavePath = Path.Combine(_folder, "no-extension");

        await vm.Export3mfCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(_folder, "no-extension.3mf")));
        Assert.False(File.Exists(Path.Combine(_folder, "no-extension")));
    }

    [UiFact]
    public async Task A_hole_in_a_group_is_subtracted_and_a_loose_hole_is_not_exported()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var plate = vm.Document.Root.Children[0];
        vm.Properties!.Parameters[0].Text = "40";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Parameters[2].Text = "10";
        await Add(vm, "cylinder");
        var hole = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "-5";
        vm.Properties.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "30";
        vm.Properties.IsHole = true;
        await vm.WaitForEvaluationAsync();
        var path = Path.Combine(_folder, "cut.3mf");
        _dialogs.SavePath = path;

        await vm.Export3mfCommand.ExecuteAsync(null); // the hole is loose: only a ghost
        var loose = ObjectsOf(File.ReadAllBytes(path));
        Assert.Single(loose);
        Assert.Equal(16000, VolumeOf(loose[0]), 1);

        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        await vm.Export3mfCommand.ExecuteAsync(null);

        var cut = Assert.Single(ObjectsOf(File.ReadAllBytes(path)));
        var circle = 0.5 * 64 * 25 * Math.Sin(2 * Math.PI / 64); // the 64-sided "circle" of the generator
        Assert.Equal(16000 - circle * 10, VolumeOf(cut), 1);
    }

    [UiFact]
    public async Task Hidden_objects_are_not_exported()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.Nodes[1].Visible = false;
        await vm.WaitForEvaluationAsync();
        var path = Path.Combine(_folder, "hidden.3mf");
        _dialogs.SavePath = path;

        await vm.Export3mfCommand.ExecuteAsync(null);

        Assert.Equal(["Box"], ObjectsOf(File.ReadAllBytes(path)).Select(o => o.Attribute("name")!.Value).ToArray());
    }

    [UiFact]
    public async Task Nothing_to_export_is_explained_and_no_file_dialog_opens()
    {
        var vm = NewViewModel();
        await vm.WaitForEvaluationAsync();

        Assert.False(vm.Export3mfCommand.CanExecute(null));
        Assert.False(vm.OpenInSlicerCommand.CanExecute(null));

        await Add(vm, "box");
        Assert.True(vm.Export3mfCommand.CanExecute(null));
        Assert.True(vm.OpenInSlicerCommand.CanExecute(null));
        Assert.Equal(0, _dialogs.SavePrompts);
    }

    [UiFact]
    public async Task Cancelling_the_file_dialog_writes_nothing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        _dialogs.SavePath = null;

        await vm.Export3mfCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.SavePrompts);
        Assert.Empty(Directory.GetFiles(_folder, "*.3mf"));
    }

    [UiFact]
    public async Task A_file_that_cannot_be_written_is_reported_in_the_status_bar()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, "I am a file, not a folder");
        _dialogs.SavePath = Path.Combine(blocker, "out.3mf"); // the "folder" is a file

        await vm.Export3mfCommand.ExecuteAsync(null);

        Assert.Contains("could not be saved", vm.StatusText);
        Assert.Empty(_dialogs.Errors); // a status bar message, like the STL export
    }

    [UiFact]
    public async Task The_suggested_name_follows_the_project()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var project = Path.Combine(_folder, "Bracket.fwp");
        _dialogs.SavePath = project;
        Assert.True(vm.Save());
        string? suggested = null;
        var recording = new RecordingDialogs(_dialogs, name => suggested = name);
        var other = new MainViewModel(new ManifoldKernel(), recording, userSettings: Store(), slicerLauncher: _slicer);
        Assert.True(other.OpenPath(project));
        await other.WaitForEvaluationAsync();

        await other.Export3mfCommand.ExecuteAsync(null);

        Assert.Equal("Bracket.3mf", suggested);
    }

    // ---- open in slicer --------------------------------------------------------------------------

    [UiFact]
    public async Task Without_a_slicer_the_user_is_asked_once_and_the_program_is_remembered()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var program = FakeProgram();
        _dialogs.OpenPath = program;

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.OpenPrompts);
        var launch = Assert.Single(_slicer.Launches);
        Assert.Equal(program, launch.Program);
        Assert.EndsWith(".3mf", launch.File);
        Assert.NotNull(launch.Content); // the file existed when the slicer was started
        Assert.Single(ObjectsOf(launch.Content!));
        Assert.Equal(program, Store().Load().SlicerPath);
        Assert.Equal(program, vm.SlicerPath);
        _tempFiles.Add(launch.File);

        await vm.OpenInSlicerCommand.ExecuteAsync(null); // second time: no question
        Assert.Equal(1, _dialogs.OpenPrompts);
        Assert.Equal(2, _slicer.Launches.Count);
    }

    [UiFact]
    public async Task A_slicer_from_the_settings_is_used_without_asking()
    {
        var program = FakeProgram();
        Store().Save(new UserSettings { SlicerPath = program });
        var vm = NewViewModel();
        await Add(vm, "box");

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        Assert.Equal(0, _dialogs.OpenPrompts);
        Assert.Equal(program, Assert.Single(_slicer.Launches).Program);
        _tempFiles.Add(_slicer.Launches[0].File);
        Assert.Contains("Opened in slicer", vm.StatusText);
    }

    [UiFact]
    public async Task A_stored_slicer_that_no_longer_exists_is_asked_for_again()
    {
        Store().Save(new UserSettings { SlicerPath = Path.Combine(_folder, "gone.exe") });
        var vm = NewViewModel();
        await Add(vm, "box");
        var program = FakeProgram("new-slicer.exe");
        _dialogs.OpenPath = program;

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.OpenPrompts);
        Assert.Equal(program, Assert.Single(_slicer.Launches).Program);
        _tempFiles.Add(_slicer.Launches[0].File);
        Assert.Equal(program, Store().Load().SlicerPath);
    }

    [UiFact]
    public async Task Cancelling_the_slicer_question_starts_nothing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        _dialogs.OpenPath = null;

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        Assert.Empty(_slicer.Launches);
        Assert.Null(vm.SlicerPath);
    }

    [UiFact]
    public async Task A_slicer_that_does_not_start_is_explained_and_not_remembered()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        _dialogs.OpenPath = FakeProgram("broken.exe");
        _slicer.Succeeds = false;
        _slicer.Error = "Access denied.";

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        var message = Assert.Single(_dialogs.Errors);
        Assert.Contains("broken.exe", message);
        Assert.Contains("Access denied.", message);
        Assert.Null(Store().Load().SlicerPath);
        _tempFiles.Add(_slicer.Launches[0].File);
    }

    [UiFact]
    public async Task The_temporary_file_is_named_after_the_project()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        _dialogs.SavePath = Path.Combine(_folder, "My Part.fwp");
        Assert.True(vm.Save());
        _dialogs.OpenPath = FakeProgram();

        await vm.OpenInSlicerCommand.ExecuteAsync(null);

        var file = _slicer.Launches.Single().File;
        _tempFiles.Add(file);
        Assert.Equal("My Part.3mf", Path.GetFileName(file));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Formwerk"), Path.GetDirectoryName(file));
    }

    /// <summary>Wraps the fake dialogs to see which file name the save dialog suggests.</summary>
    private sealed class RecordingDialogs(FakeDialogs inner, Action<string> suggested) : IDialogService
    {
        public string? PickSaveFile(string title, string filter, string defaultFileName)
        {
            suggested(defaultFileName);
            return inner.PickSaveFile(title, filter, defaultFileName);
        }

        public string? PickOpenFile(string title, string filter) => inner.PickOpenFile(title, filter);

        public bool ShowSaveTemplate(SaveTemplateViewModel viewModel) => inner.ShowSaveTemplate(viewModel);

        public UnsavedChoice ConfirmUnsavedChanges(string documentName) => inner.ConfirmUnsavedChanges(documentName);

        public bool ConfirmRecovery(DateTime savedLocalTime, string? documentName) => inner.ConfirmRecovery(savedLocalTime, documentName);

        public void ShowError(string message) => inner.ShowError(message);

        public IReadOnlyList<string> PickImportFiles(string title, string filter) => inner.PickImportFiles(title, filter);

        public ImportUnit? PickImportUnit(string modelName, double largestExtentInFile) => inner.PickImportUnit(modelName, largestExtentInFile);

        public SettingsResult? ShowSettings(DocumentSettings project, UserSettings user) => inner.ShowSettings(project, user);
    }
}
