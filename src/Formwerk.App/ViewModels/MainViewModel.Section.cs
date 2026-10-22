using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Section view (E-06): a cutting plane through the model; what is in front of it is cut away and the cut surface is filled with a color,
/// so holes and countersinks show in the section. Only what is drawn changes (<see cref="ViewportItems"/>): nothing in the document or
/// the undo history, nothing saved, and exports and tools keep working on the real model.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The color of the cut surface (amber).</summary>
    public const uint SectionColor = 0xFFF2A23A;

    private Task _latestSection = Task.CompletedTask;
    private CancellationTokenSource? _sectionCts;
    private int _sectionVersion;
    private bool _sectionStarted; // the position was set once: it is kept when the view is switched off and on again
    private Vector3 _sectionFaceNormal = Vector3.UnitZ;
    private FieldViewModel? _sectionField;

    /// <summary>The section view is on.</summary>
    [ObservableProperty]
    public partial bool IsSectionActive { get; set; }

    /// <summary>The plane is across X (0), Y (1) or Z (2), or is the plane of a face (3).</summary>
    [ObservableProperty]
    public partial int SectionAxis { get; set; }

    /// <summary>Where the plane is, in mm along its normal (a world coordinate for X, Y and Z).</summary>
    [ObservableProperty]
    public partial double SectionPosition { get; set; }

    /// <summary>The extent of the scene along the normal of the plane (a little more), the range of the slider.</summary>
    [ObservableProperty]
    public partial double SectionMin { get; set; } = -100;

    [ObservableProperty]
    public partial double SectionMax { get; set; } = 100;

    /// <summary>Keep the other side (the part in front of the normal instead of behind it).</summary>
    [ObservableProperty]
    public partial bool SectionFlip { get; set; }

    /// <summary>Waiting for a click on a face that the plane is put on.</summary>
    [ObservableProperty]
    public partial bool IsPickingSectionFace { get; set; }

    public IReadOnlyList<AxisChoice> SectionAxes { get; } =
        [new(0, Texts.Get("Section.Axis.X")), new(1, Texts.Get("Section.Axis.Y")), new(2, Texts.Get("Section.Axis.Z")), new(3, Texts.Get("Section.Axis.Face"))];

    /// <summary>The position typed as a number or a formula (project variables work).</summary>
    public FieldViewModel SectionPositionField => _sectionField ??= CreateSectionField();

    private FieldViewModel CreateSectionField()
    {
        var field = new FieldViewModel(Texts.Get("Section.Position"), "mm", text =>
        {
            var result = NumericInput.Resolve(text, SectionPosition, _document.ResolveVariables(out _));
            if (!result.IsSuccess) return result.Error;
            if (!double.IsFinite(result.Value)) return Texts.Get("Section.Err.Position");
            SectionPosition = result.Value;
            return null;
        });
        field.SetFromModel(FormatPosition(SectionPosition));
        return field;
    }

    private static string FormatPosition(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    partial void OnIsSectionActiveChanged(bool value)
    {
        if (value)
        {
            UpdateSectionRange(recenter: !_sectionStarted);
            _sectionStarted = true;
            RequestSection();
            return;
        }

        _sectionCts?.Cancel();
        _sectionVersion++;
        IsPickingSectionFace = false;
        if (_lastResult is not null) BuildViewportItems(_lastResult); // the whole model again
    }

    partial void OnSectionAxisChanged(int value)
    {
        UpdateSectionRange(recenter: true);
        RequestSection();
    }

    partial void OnSectionPositionChanged(double value)
    {
        _sectionField?.SetFromModel(FormatPosition(value));
        RequestSection();
    }

    partial void OnSectionFlipChanged(bool value) => RequestSection();

    partial void OnIsPickingSectionFaceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        RefreshScaleHint();
    }

    private Vector3 SectionNormal() => SectionAxis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        2 => Vector3.UnitZ,
        _ => _sectionFaceNormal,
    };

    private SectionPlane CurrentSectionPlane()
    {
        var plane = new SectionPlane(SectionNormal(), (float)SectionPosition);
        return SectionFlip ? plane.Flipped : plane;
    }

    /// <summary>
    /// The slider range follows the extent of the scene along the normal of the plane (one mm more on each side). With
    /// <paramref name="recenter"/> the plane goes to the middle of the model; otherwise the position stays where it is.
    /// </summary>
    private void UpdateSectionRange(bool recenter)
    {
        var normal = SectionNormal();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var part in _lastResult?.Parts ?? [])
        {
            if (part.Mesh.IsEmpty) continue;
            var bounds = part.Mesh.ComputeBounds();
            foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
            foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
            foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
            {
                var d = Vector3.Dot(new Vector3(x, y, z), normal);
                min = Math.Min(min, d);
                max = Math.Max(max, d);
            }
        }

        if (min > max)
        {
            SectionMin = -100;
            SectionMax = 100;
        }
        else
        {
            SectionMin = Math.Floor(min - 1);
            SectionMax = Math.Ceiling(max + 1);
        }

        if (recenter) SectionPosition = Math.Round((SectionMin + SectionMax) / 2, 1);
    }

    private void RequestSection()
    {
        if (IsSectionActive) _latestSection = SectionAsync();
    }

    /// <summary>
    /// Cuts the parts of the last result at the plane and fills the cut surface, on a background thread. A newer request replaces an older
    /// one, so dragging the slider stays smooth; the result is only drawn when it is still the newest.
    /// </summary>
    private async Task SectionAsync()
    {
        var version = ++_sectionVersion;
        _sectionCts?.Cancel();
        _sectionCts = new CancellationTokenSource();
        var token = _sectionCts.Token;
        if (_lastResult is not { } result) return;

        var parts = result.Parts.Where(p => ShowHoles || p.Kind == PartKind.Solid).ToList();
        var plane = CurrentSectionPlane();
        var inside = OpenContainerContents();

        IReadOnlyList<SectionPart> built;
        try
        {
            // the boolean kernel is shared with the evaluator, which is not thread-safe: one at a time
            built = await Task.Run(() =>
            {
                lock (_evaluatorLock)
                {
                    token.ThrowIfCancellationRequested();
                    return SectionBuilder.Build(parts, plane, _kernel, token);
                }
            }, CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // a newer request supersedes this one
        }
        catch (Exception ex) when (version == _sectionVersion)
        {
            SetNotice(Texts.Format("Msg.SectionFailed", ex.Message));
            return;
        }

        if (version != _sectionVersion || !IsSectionActive) return;
        ViewportItems = [.. built.SelectMany(p => SectionItems(p, inside))];
    }

    private IEnumerable<ViewportItem> SectionItems(SectionPart part, HashSet<Guid>? inside)
    {
        var source = part.Source;
        var solid = source.Kind == PartKind.Solid;
        var dimmed = inside is not null && !inside.Contains(source.Source.Id);
        yield return new ViewportItem(
            source.Source.Id, part.Body, solid ? ViewportItemKind.Solid : ViewportItemKind.Ghost, source.Source.Color,
            Outside: solid && _outside.Contains(source.Source.Id), Dimmed: dimmed);
        if (part.Cap is { } cap) yield return new ViewportItem(source.Source.Id, cap, ViewportItemKind.Solid, SectionColor, Dimmed: dimmed);
    }

    private bool CanShowSection() => _document.Root.Children.Count > 0;

    /// <summary>Starts or ends the mode in which a click on a face puts the plane on that face (switching the section view on).</summary>
    [RelayCommand(CanExecute = nameof(CanShowSection))]
    private void SectionAtFace()
    {
        IsPickingSectionFace = !IsPickingSectionFace;
        if (IsPickingSectionFace)
        {
            IsPickingFace = false;
            IsPlacingHole = false;
            IsPlacingEdgeCut = false;
            IsMeasuring = false;
            IsPickingMirrorFace = false;
            EndFaceToFaceMode();
        }

        RefreshScaleHint();
    }

    /// <summary>Switches the section view off (the close button of its panel).</summary>
    [RelayCommand]
    private void CloseSection() => IsSectionActive = false;

    /// <summary>The click of the face mode: the plane goes onto the clicked face, with its normal, and is moved along it from there.</summary>
    private void SectionFacePicked(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is null || pick.Normal.LengthSquared() < 1e-12f) return;

        IsPickingSectionFace = false;
        _sectionFaceNormal = Vector3.Normalize(pick.Normal);
        SectionFlip = false;
        IsSectionActive = true;
        SectionAxis = 3;
        SectionPosition = Math.Round(Vector3.Dot(_sectionFaceNormal, pick.Point), 3);
        RefreshScaleHint();
    }
}
