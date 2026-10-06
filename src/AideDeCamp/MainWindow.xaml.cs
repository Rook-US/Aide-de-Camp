using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AideDeCamp.Models;
using AideDeCamp.Services;
using Microsoft.Win32;

namespace AideDeCamp;

public partial class MainWindow : Window
{
    private readonly GrandTacticianDataService _data = new();
    private readonly NamingSchemeService _naming = new();
    private readonly GameInstallationService _installation = new();
    private readonly OobDisplayModelService _displayBuilder = new();
    private readonly CommandClassificationService _commandClassifier = new();
    private readonly EditValidationService _validation = new();
    private readonly EditSession _editSession = new();
    private readonly BatchEditPlanner _batchPlanner;
    private DisplayOobModel _displayModel = new();
    private readonly ObservableCollection<OobNode> _visibleCanvasNodes = new();
    private readonly List<OobNode> _roots = new();
    private readonly ObservableCollection<RosterRow> _rosterRows = new();
    private readonly HashSet<CombatUnitNode> _selectedUnits = new();
    private readonly ObservableCollection<ReadinessAlertItem> _alerts = new();

    private CombatUnitNode? _selectedUnit;
    private OobNode? _selectedNode;
    private int _nation;
    private string? _configDirectory;
    private bool _dirty;
    private bool _showRoster;
    private CommandCategory _category = CommandCategory.FieldCommand;
    private bool _showGarrisons => _category == CommandCategory.Garrison;
    private bool _rosterListing = true;
    private bool _syncingRosterSelection;
    private string _rosterSortKey = "OobOrder";
    private bool _rosterSortDescending;

    private Point _dragStart;
    private CombatUnitNode? _dragUnit;
    private bool _isPanning;
    private bool _panCandidate;
    private MouseButton _panButton;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;
    private double _zoom = 1.0;
    private double _panX = 20;
    private double _panY = 20;
    private double _worldWidth = 6000;
    private double _worldHeight = 4000;
    private readonly Dictionary<OobNode, double> _nodeMeasuredHeights = new();
    private readonly Dictionary<OobNode, string> _nodeMeasureSignatures = new();
    private readonly Dictionary<(OobNode Node, bool Details, string Signature), (double Height, double Inset)> _modeFootprints = new();

    private void InvalidateCardMeasurements()
    {
        _cardCacheGeneration++;
        _nodeMeasuredHeights.Clear(); _nodeSurfaceInsets.Clear(); _nodeMeasureSignatures.Clear();
        _modeFootprints.Clear();
        _cardWarmupGeneration++;
        _cardWarmupOperation?.Abort(); _cardWarmupOperation = null;
        _preparedDetailCards.Clear();
        _cardVisuals.Clear();
        NodeCanvas.Children.Clear();
    }
    private readonly List<ConnectorSegment> _connectorSegments = new();
    private bool _allowClose;
    private bool _saveInProgress;

    private readonly UiSettingsService _ui;
    private double Ui(string key) => _ui.Get(key);
    private double CardScale => Ui("oob.cards.scale");
    private double GroupCardWidth => CardFootprintWidth(true);
    private const double GroupCardFallbackHeight = 292;
    private double UnitCardWidth => CardFootprintWidth(false);
    private const double UnitCardFallbackHeight = 174;
    // Contours use world pixels and actual card edges, not a largest-card column grid.
    // Base gutters are additive, so a larger setting never masks another control.
    private double HorizontalGap(OobNode node) => Ui("oob.spacing.minX") + Ui(node is GroupNode ? "oob.spacing.commands" : "oob.spacing.attached");
    private double ArmyRankGap => Ui("oob.spacing.minY") + Ui("oob.spacing.army");
    private double CorpsRankGap => Ui("oob.spacing.minY") + Ui("oob.spacing.corps");
    private double DivisionRankGap => Ui("oob.spacing.minY") + Ui("oob.spacing.division");
    private double BrigadeRankGap => Ui("oob.spacing.minY") + Ui("oob.spacing.brigade");
    private double RegimentRankGap => Ui("oob.spacing.minY") + Ui("oob.spacing.regiment");
    private double DefaultRankGap => RegimentRankGap;
    private double UnitStackGap => Ui("oob.spacing.stack");
    private const double WorldPadding = 1800;
    private const double MinWorldWidth = 6000;
    private const double MinWorldHeight = 4000;

    public MainWindow() : this(false) { }

    public MainWindow(bool skipInstallationDetection)
    {
        _ui = new UiSettingsService(loadSaved: !skipInstallationDetection);
        _batchPlanner = new BatchEditPlanner(_validation);
        InitializeComponent();
        InitializeTypedRoster();
        ApplyUiSettings();
        RosterGrid.ItemsSource = _rosterRows;
        AlertList.ItemsSource = _alerts;
        UpdateNationButtons();
        UpdateViewButtons();
        UpdateCommandCategoryButtons();
        ApplyCanvasTransform();
        if (skipInstallationDetection) return; // Isolated Windows UI regression harness.
        var detected = _installation.AutoDetect();
        if (detected is not null)
        {
            _configDirectory = _installation.ConfigDirectory;
            StatusText.Text = $"Grand Tactician found: {detected}";
            UpdateGameFolderStatus();
            if (!_installation.AutoDetectionConfirmed)
            {
                var answer = MessageBox.Show(
                    $"Grand Tactician installation found:\n\n{detected}\n\nUse this installation?",
                    "Grand Tactician Found", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes) _installation.ConfirmAutoDetection();
                else Dispatcher.BeginInvoke(new Action(() => LocateGameInstallation()));
            }
        }
        else
        {
            StatusText.Text = "Grand Tactician installation not detected — choose Game Folder.";
            UpdateGameFolderStatus();
        }
    }

    private async void OpenSave_Click(object sender, RoutedEventArgs e)
    {
        if (_installation.InstallationRoot is null && !LocateGameInstallation()) return;
        _configDirectory = _installation.ConfigDirectory;
        var browser = new SaveBrowserWindow(_installation) { Owner = this };
        if (browser.ShowDialog() != true || string.IsNullOrWhiteSpace(browser.SelectedSaveDirectory)) return;
        if (!await ConfirmCanAbandonWorkingChangesAsync()) return;
        await LoadSave(browser.SelectedSaveDirectory);
    }

    private async Task LoadSave(string path)
    {
        try
        {
            StatusText.Text = "Loading save…";
            IsEnabled = false;
            ClearBatchSelection();
            _workspaceSelections.Clear(); _previousBatches.Clear(); _workspaceSearches.Clear();
            _management = null;
            _editSession.Clear();
            _typedDrafts.Clear();
            _groupNameDrafts.Clear();
            InvalidateCardMeasurements();
            await _data.LoadAsync(path, _configDirectory);
            LoadDisplayMetadata();
            _savedDisplayState = DisplayState();
            _loadedSnapshots = _editSession.Capture(_data.Units);
            _validation.SetObservedExperienceRange(_data.Units);
            SaveButton.IsEnabled = !_data.IsReadOnlySave;
            SaveButton.ToolTip = _data.IsReadOnlySave ? "ZIP saves are opened read-only. Select the extracted save directory to write changes." : null;
            UpdateLimits();
            RebuildSide(initialLoad: true);
            OpenWorkspace(_nation, _workspace);
            StatusText.Text = "Preparing Tree cards…";
            await PrepareSaveCardCache();
            SetDirty(false);
            var date = string.IsNullOrWhiteSpace(_data.GameDateText) ? string.Empty : $" • {_data.GameDateText}";
            var pathStates = _data.Units.GroupBy(u => u.PathLinkStatus).ToDictionary(g => g.Key, g => g.Count());
            var pathSummary = pathStates.Count == 0 ? string.Empty : $" • path links: {pathStates.GetValueOrDefault(PathLinkStatus.Confirmed):N0} confirmed, {pathStates.GetValueOrDefault(PathLinkStatus.Ambiguous):N0} ambiguous, {pathStates.GetValueOrDefault(PathLinkStatus.Missing):N0} missing";
            var readOnly = _data.IsReadOnlySave ? " • READ-ONLY ZIP" : string.Empty;
            var orphanWarning = _data.OrphanUnitCount > 0 ? $" • WARNING: {_data.OrphanUnitCount:N0} unit(s) reference missing parent commands" : string.Empty;
            StatusText.Text = $"Loaded {_data.Units.Count:N0} combat units and {_data.Groups.Count:N0} command records{date}{pathSummary}{orphanWarning}{readOnly}.";
            var loadErrors = ScanWorkingData(ValidationSeverity.Error);
            var loadWarnings = ScanWorkingData(ValidationSeverity.Warning);
            StatusText.Text += $" Validation: {loadErrors.Count} errors, {loadWarnings.Count} warnings (magenta fields).";
            if (loadErrors.Count > 0) ShowValidationReview(loadErrors, false, "Loaded data needs correction");
            await Dispatcher.InvokeAsync(FocusInitialCommand, System.Windows.Threading.DispatcherPriority.Loaded);
            await Dispatcher.InvokeAsync(FitOob, System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Load save",ex);
            InvalidateCardMeasurements(); _data.Dispose(); _roots.Clear(); _displayModel = new DisplayOobModel(); ClearDetails();
            RefreshOobCanvas(); RefreshRoster(); RefreshAlerts(); SetDirty(false);
            MessageBox.Show(ex.ToString(), "Could not load save", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Load failed.";
        }
        finally { IsEnabled = true; }
    }

    private async void ChooseGameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmCanAbandonWorkingChangesAsync()) return;
        if (!LocateGameInstallation()) return;
        _configDirectory = _installation.ConfigDirectory;
        StatusText.Text = $"Grand Tactician: {_installation.InstallationRoot}";
        if (_data.SaveDirectory is not null && !_data.IsReadOnlySave) await LoadSave(_data.SaveDirectory);
    }

    private bool LocateGameInstallation()
    {
        var dlg = new OpenFolderDialog { Title = "Select Grand Tactician Installation Folder" };
        if (dlg.ShowDialog() != true) return false;
        if (!_installation.TrySetInstallation(dlg.FolderName))
        {
            MessageBox.Show("That folder does not look like a Grand Tactician installation. The editor expects both Config and Campaigns folders beneath it.",
                "Grand Tactician not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _configDirectory = _installation.ConfigDirectory;
        UpdateGameFolderStatus();
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();

    private async Task<bool> SaveCurrentAsync()
    {
        if (_data.SaveDirectory is null || _saveInProgress) return _data.SaveDirectory is not null;
        if (_data.IsReadOnlySave)
        {
            MessageBox.Show(this, "This save was opened from a ZIP and is read-only. Select the extracted save directory before writing changes.", "Read-only save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!CommitTypedDrafts() || !ReviewBeforeSave()) return false;
        try
        {
            _saveInProgress = true;
            StatusText.Text = "Validating, backing up, and saving transactionally…";
            IsEnabled = false;
            var result = await _data.SaveAsync();
            SaveDisplayMetadata();
            _data.MarkSavedStates(); // UI-bound state notifications stay on the UI thread.
            UpdateDirtyState();
            RefreshRoster();
            var warningText = result.Warnings.Count > 0 ? $" • {result.Warnings.Count:N0} warning(s)" : string.Empty;
            StatusText.Text = result.WroteFiles
                ? $"Saved {result.PatchedFieldCount:N0} changed field(s){warningText}. Backup: {result.BackupDirectory}"
                : "No serialized save fields changed.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Save changes",ex);
            MessageBox.Show(ex.ToString(), "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Save failed; original files were left in place or rolled back from backup.";
            return false;
        }
        finally
        {
            _saveInProgress = false;
            IsEnabled = true;
        }
    }

    private void RebuildSide(bool initialLoad = false)
    {
        _roots.Clear();
        var allRoots = _data.BuildRoots(_nation);
        _roots.AddRange(_data.Groups.Values.Where(g => g.Nation == _nation &&
            _commandClassifier.CategoryForGroup(g, _data.Groups) == _category &&
            (!_data.Groups.TryGetValue(g.ParentId, out var parent) || parent.Nation != _nation ||
             _commandClassifier.CategoryForGroup(parent, _data.Groups) != _category)));
        if (initialLoad)
        {
            foreach (var root in _roots.OfType<GroupNode>()) SetExpandedRecursive(root, true);
            _selectedNode = _roots.OfType<GroupNode>().OrderByDescending(g => g.CommandExpectedUnitCount).FirstOrDefault();
        }

        // Build a UI-only projection from canonical ParentId relationships. The canvas
        // never derives organization from its own visual children or coordinates.
        _displayModel = _displayBuilder.Build(
            _data.Groups, _data.Units, _nation, _roots.OfType<GroupNode>().Select(g => g.GroupId),
            g => _commandClassifier.CategoryForGroup(g, _data.Groups) == _category,
            u => IsUnitInCurrentCommandCategory(u));
        if (_category == CommandCategory.Unknown)
            _displayModel.UnattachedUnits.AddRange(_data.Units.Where(u => u.Nation == _nation && IsUnitInCurrentCommandCategory(u) && !_displayModel.CommandsById.ContainsKey(u.ParentId)));
        ApplyDisplayOrder();
        RefreshOobCanvas();
        RefreshRoster();
        RefreshAlerts();
    }

    private bool IsGarrisonRoot(GroupNode group) => _commandClassifier.IsGarrison(group);

    private bool IsFieldCommandRoot(GroupNode group) => _commandClassifier.IsFieldVisible(group);

    private bool IsUnitInCurrentCommandCategory(CombatUnitNode unit)
    {
        return _commandClassifier.CategoryForUnit(unit, _data.Groups) == _category;
    }
    private bool CanEdit(CombatUnitNode unit) => _commandClassifier.IsLandEditable(unit, _data.Groups);
    private void SwitchCommandCategory(CommandCategory category)
    {
        if (_category == category && _roots.Count > 0) return;
        _category = category; ClearBatchSelection(); ClearDetails();
        _selectedNode = null; RebuildSide(initialLoad: true); UpdateCommandCategoryButtons();
        if (!_showRoster) Dispatcher.BeginInvoke(new Action(FitOob));
    }
    private void FieldTree_Click(object sender, RoutedEventArgs e) { SetView(false); }
    private void FieldRoster_Click(object sender, RoutedEventArgs e) { SetView(true); }
    private void GarrisonTree_Click(object sender, RoutedEventArgs e) { SwitchCommandCategory(CommandCategory.Garrison); SetView(false); }
    private void GarrisonRoster_Click(object sender, RoutedEventArgs e) { SwitchCommandCategory(CommandCategory.Garrison); SetView(true); }
    private void NavyTree_Click(object sender, RoutedEventArgs e) { SwitchCommandCategory(CommandCategory.Fleet); SetView(false); }
    private void NavyRoster_Click(object sender, RoutedEventArgs e) { SwitchCommandCategory(CommandCategory.Fleet); SetView(true); }
    private void UnknownTree_Click(object sender, RoutedEventArgs e) { SwitchCommandCategory(CommandCategory.Unknown); SetView(false); }
    private void UnknownRoster_Click(object sender, RoutedEventArgs e) { OpenWorkspace(_nation, "Unclassified"); SetView(true); }

    private void UpdateCommandCategoryButtons()
    {
        UpdateViewButtons();
        WorkspaceNotice.Text = _category == CommandCategory.Fleet ? "NAVAL SUPPORT — WORK IN PROGRESS • Inspection only" :
            _category == CommandCategory.Unknown ? "UNKNOWN CLASSIFICATION • Inspection only" : "";
        WorkspaceNotice.Visibility = string.IsNullOrEmpty(WorkspaceNotice.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class TidyPlacement
    {
        public required DisplayCommandNode Command { get; init; }
        public double X { get; set; }
        public int Depth { get; set; }
    }

    private sealed class AttachedPlacement
    {
        public required DisplayCommandNode Parent { get; init; }
        public required IReadOnlyList<CombatUnitNode> Units { get; init; }
        public double X { get; set; }
        public int Depth { get; set; }
    }

    private sealed class TidyLayoutResult
    {
        public double RootX { get; set; }
        public List<TidyPlacement> Commands { get; } = new();
        public List<AttachedPlacement> AttachedStacks { get; } = new();
        public Dictionary<int, (double Min, double Max)> Contours { get; } = new();
    }

    private sealed record ConnectorSegment(Line Line, double X1, double Y1, double X2, double Y2);

    private readonly Dictionary<OobNode, double> _nodeSurfaceInsets = new();
    private int _layoutCommandDepth;
    private double GetSurfaceInset(OobNode node) => _nodeSurfaceInsets.GetValueOrDefault(node);
    private double GetNodeWidth(OobNode node) => (node is GroupNode ? GroupCardWidth : UnitCardWidth)
        * NodeTierScale(node);

    private double GetNodeHeight(OobNode node)
    {
        if (_nodeMeasuredHeights.TryGetValue(node, out var height) && height > 0) return height;
        return node is GroupNode ? GroupCardFallbackHeight : UnitCardFallbackHeight;
    }

    private void MeasureNodeFootprints()
    {
        var nodes = EnumerateAllLaidOutNodes().Distinct().ToList();
        var live = new HashSet<OobNode>(nodes);
        foreach (var stale in _nodeMeasuredHeights.Keys.Where(n => !live.Contains(n)).ToList())
        {
            _nodeMeasuredHeights.Remove(stale);
            _nodeSurfaceInsets.Remove(stale);
            _nodeMeasureSignatures.Remove(stale);
        }

        foreach (var node in nodes)
        {
            var signature = CardMeasureSignature(node, ShowCardDetails);
            if (_nodeMeasuredHeights.ContainsKey(node) && _nodeMeasureSignatures.GetValueOrDefault(node) == signature) continue;
            if (_modeFootprints.TryGetValue((node, ShowCardDetails, signature), out var cached)) {
                _nodeMeasuredHeights[node] = cached.Height;
                _nodeSurfaceInsets[node] = cached.Inset;
                _nodeMeasureSignatures[node] = signature;
                continue;
            }
            var element = CachedCard(node);
            var reuse = element.Parent is not null;
            // Detached templates can resolve styles and generated metric rows
            // differently from live cards. Measure under this window's resource tree.
            if (!reuse) CardMeasureHost.Children.Add(element);
            try {
                SetCardDetailVisibility(element, ShowCardDetails);
                element.ApplyTemplate();
                element.Measure(new Size(GetNodeWidth(node), double.PositiveInfinity));
                var measured = Math.Ceiling(element.DesiredSize.Height);
                if (measured <= 0 || !double.IsFinite(measured))
                    measured = node is GroupNode ? GroupCardFallbackHeight : UnitCardFallbackHeight;
                element.Arrange(new Rect(new Size(GetNodeWidth(node), measured)));
                var surface = FindNamedDescendant<Border>(element, "CardSurface");
                _nodeSurfaceInsets[node] = surface is null ? 0 : surface.TransformToAncestor(element).Transform(new Point(0, 0)).Y * NodeCardScale(node);
                _nodeMeasuredHeights[node] = measured;
                _nodeMeasureSignatures[node] = signature;
                _modeFootprints[(node, ShowCardDetails, signature)] = (measured, _nodeSurfaceInsets[node]);
            } finally { if (!reuse) CardMeasureHost.Children.Remove(element); }
        }
    }

    private static string CardMeasureSignature(OobNode node, bool details) => GetMeasureSignature(node) + "|" + details + "|" + NodeTierScale(node) + "|" + node.Name + "|" + node.IdentityCommander + "|" + node.IdentitySecondary + "|" + node.CompactStrength + "|" + node.CompactAlerts + "|" + string.Join(";", node.CardMetrics.Select(m => m.Label + m.Value));

    private static string GetMeasureSignature(OobNode node) => node switch
    {
        GroupNode group => $"G|{group.HasTransfers}|{group.HasYellowAlerts}|{group.HasOrangeAlerts}|{group.HasRedAlerts}",
        CombatUnitNode unit => $"U|{unit.IsInTransfer}",
        _ => "N"
    };

    private double GetRankGapForTier(int unitTier) => unitTier switch
    {
        16 => ArmyRankGap,
        15 => CorpsRankGap,
        14 => DivisionRankGap,
        13 => BrigadeRankGap,
        12 => RegimentRankGap,
        _ => DefaultRankGap
    };

    private Dictionary<int, double> BuildRankYPositions(IEnumerable<TidyLayoutResult> layouts)
    {
        var cards = new List<CardRowFootprint>();
        foreach (var layout in layouts) {
            foreach (var command in layout.Commands) {
                var node = command.Command.Source;
                cards.Add(new(command.Depth, GetNodeHeight(node), GetSurfaceInset(node), GetRankGapForTier(node.UnitTier)));
            }
            foreach (var attached in layout.AttachedStacks) {
                if (attached.Units.Count == 0) continue;
                var unit = attached.Units[0];
                cards.Add(new(attached.Depth, GetNodeHeight(unit), GetSurfaceInset(unit), GetRankGapForTier(attached.Parent.Source.UnitTier)));
            }
        }
        return CardLayoutGeometry.SurfaceBaselines(cards);
    }

    private void RefreshOobCanvas()
    {
        _visibleCanvasNodes.Clear();

        LayoutDisplayModel();

        _renderedCardDetails = ShowCardDetails;
        var visible = _visibleCanvasNodes.Select(CachedCard).ToHashSet();
        foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>().Where(c => !visible.Contains(c)).ToArray()) NodeCanvas.Children.Remove(card);
        foreach (var node in _visibleCanvasNodes)
        {
            var element = CachedCard(node);
            SetCardDetailVisibility(element, ShowCardDetails);
            // Height remains Auto. The template gets exactly the vertical space its content requests.
            element.Visibility = Visibility.Visible;
            Panel.SetZIndex(element, 10);
            if (element.Parent is null) NodeCanvas.Children.Add(element);
        }
        RebuildConnectorVisuals();
        ApplyCanvasTransform();
        OobDebugText.Text += $" • {NodeCanvas.Children.Count:N0} rendered";
        ScheduleCardDetailWarmup();
    }

    private void LayoutDisplayModel()
    {
        _visibleCanvasNodes.Clear();
        // Build geometry first, then measure the real template contents. Card height is
        // intentionally content-driven; the layout consumes the measured footprint rather
        // than forcing a guessed height onto the WPF element.
        static int Depth(DisplayCommandNode command) => 1 + command.Subcommands.Select(Depth).DefaultIfEmpty(0).Max();
        _layoutCommandDepth = _displayModel.Roots.Select(Depth).DefaultIfEmpty(0).Max();
        var rootLayouts = _displayModel.Roots.Select(BuildTidyLayout).ToList();
        MeasureNodeFootprints();
        var rankY = BuildRankYPositions(rootLayouts);

        // Lay out each top-level command in its own logical block. Geometry is
        // calculated independently of the viewport. The viewport is only a camera.
        var rootOffset = 0.0;
        foreach (var layout in rootLayouts)
        {
            var edges = layout.Commands.Select(c => (X: c.X, Width: GetNodeWidth(c.Command.Source)))
                .Concat(layout.AttachedStacks.Select(a => (X: a.X, Width: a.Units.Max(GetNodeWidth)))).ToArray();
            var min = edges.Min(c => c.X - c.Width / 2);
            var max = edges.Max(c => c.X + c.Width / 2);
            var shift = rootOffset - min;
            ApplyTidyLayout(layout, shift, rankY);
            rootOffset += max - min + Ui("oob.spacing.roots");
        }

        // Normalize ALL display coordinates into positive world space with generous
        // breathing room. This is deliberately based on the full display model, not
        // the currently visible viewport or collapsed-state subset.
        var standaloneY = 70.0;
        foreach (var unit in _displayModel.UnattachedUnits)
        {
            unit.CanvasX = 100 + rootOffset; unit.CanvasY = standaloneY;
            standaloneY += GetNodeHeight(unit) + UnitStackGap;
        }
        var allLaidOut = EnumerateAllLaidOutNodes().Distinct().ToList();
        if (allLaidOut.Count > 0)
        {
            var rawMinX = allLaidOut.Min(n => n.CanvasX);
            var rawMinY = allLaidOut.Min(n => n.CanvasY);
            var dx = WorldPadding - rawMinX;
            var dy = WorldPadding - rawMinY;
            foreach (var node in allLaidOut)
            {
                node.CanvasX += dx;
                node.CanvasY += dy;
            }
        }

        // Visibility is separate from geometry. Collapsing only hides descendants.
        foreach (var root in _displayModel.Roots) CollectVisibleDisplayNodes(root, ancestorsVisible: true);
        foreach (var unit in _displayModel.UnattachedUnits) _visibleCanvasNodes.Add(unit);

        if (allLaidOut.Count > 0)
        {
            var worldMinX = allLaidOut.Min(n => n.CanvasX);
            var worldMaxX = allLaidOut.Max(n => n.CanvasX + GetNodeWidth(n));
            var worldMinY = allLaidOut.Min(n => n.CanvasY);
            var worldMaxY = allLaidOut.Max(n => n.CanvasY + GetNodeHeight(n));
            var worldWidth = Math.Max(MinWorldWidth, worldMaxX + WorldPadding);
            var worldHeight = Math.Max(MinWorldHeight, worldMaxY + WorldPadding);

            // Keep the WPF visual surface viewport-sized. World size is logical only.
            // Cards are projected from world coordinates into screen coordinates by
            // ApplyCanvasTransform(), avoiding WPF clipping on enormous transformed panels.
            _worldWidth = worldWidth;
            _worldHeight = worldHeight;

            var visibleMinX = _visibleCanvasNodes.Count == 0 ? worldMinX : _visibleCanvasNodes.Min(n => n.CanvasX);
            var visibleMaxX = _visibleCanvasNodes.Count == 0 ? worldMaxX : _visibleCanvasNodes.Max(n => n.CanvasX + GetNodeWidth(n));
            var visibleMinY = _visibleCanvasNodes.Count == 0 ? worldMinY : _visibleCanvasNodes.Min(n => n.CanvasY);
            var visibleMaxY = _visibleCanvasNodes.Count == 0 ? worldMaxY : _visibleCanvasNodes.Max(n => n.CanvasY + GetNodeHeight(n));

            OobDebugText.Text = $"{_displayModel.Roots.Count:N0} display roots • {_visibleCanvasNodes.Count:N0} visible • " +
                                $"OOB {visibleMaxX-visibleMinX:N0}×{visibleMaxY-visibleMinY:N0} • world {worldWidth:N0}×{worldHeight:N0}";
        }
        else
        {
            _worldWidth = MinWorldWidth;
            _worldHeight = MinWorldHeight;
            OobDebugText.Text = $"{_displayModel.Roots.Count:N0} display roots • 0 visible";
        }

        ApplyNudges();
    }

    private IEnumerable<OobNode> EnumerateAllLaidOutNodes()
    {
        foreach (var unit in _displayModel.UnattachedUnits) yield return unit;
        foreach (var command in EnumerateDisplayCommands())
        {
            yield return command.Source;
            foreach (var unit in command.AttachedUnits) yield return unit;
        }
    }

    private TidyLayoutResult BuildTidyLayout(DisplayCommandNode command)
    {
        var branchLayouts = new List<TidyLayoutResult>();
        foreach (var child in command.Subcommands)
            branchLayouts.Add(BuildTidyLayout(child));

        if (command.AttachedUnits.Count > 0)
        {
            var attached = new TidyLayoutResult { RootX = 0 };
            attached.AttachedStacks.Add(new AttachedPlacement
            {
                Parent = command,
                Units = command.AttachedUnits,
                X = 0,
                Depth = 0
            });
            // A combat stack extends below its first card. Reserve its column at
            // every lower HQ depth so another branch cannot weave into the stack.
            var half = command.AttachedUnits.Max(u => (GetNodeWidth(u) + HorizontalGap(u)) / 2);
            for (var depth = 0; depth <= _layoutCommandDepth; depth++) attached.Contours[depth] = (-half, half);
            branchLayouts.Add(attached);
        }

        var result = new TidyLayoutResult();
        if (branchLayouts.Count == 0)
        {
            result.RootX = 0;
            result.Commands.Add(new TidyPlacement { Command = command, X = 0, Depth = 0 });
            var half = (GetNodeWidth(command.Source) + HorizontalGap(command.Source)) / 2;
            result.Contours[0] = (-half, half);
            return result;
        }

        var placedChildren = new List<(TidyLayoutResult Layout, double Shift)>();
        var combined = new Dictionary<int, (double Min, double Max)>();

        foreach (var child in branchLayouts)
        {
            var shift = 0.0;
            if (placedChildren.Count > 0)
            {
                // Child contour lives one rank below this parent. Shift only enough to
                // clear already placed siblings at every overlapping depth.
                foreach (var (childDepth, childContour) in child.Contours)
                {
                    var globalDepth = childDepth + 1;
                    if (!combined.TryGetValue(globalDepth, out var prior)) continue;
                    shift = Math.Max(shift, prior.Max - childContour.Min);
                }
            }

            placedChildren.Add((child, shift));
            foreach (var (childDepth, childContour) in child.Contours)
            {
                var globalDepth = childDepth + 1;
                var shifted = (Min: childContour.Min + shift, Max: childContour.Max + shift);
                if (combined.TryGetValue(globalDepth, out var existing))
                    combined[globalDepth] = (Math.Min(existing.Min, shifted.Min), Math.Max(existing.Max, shifted.Max));
                else
                    combined[globalDepth] = shifted;
            }
        }

        var firstRoot = placedChildren.First().Layout.RootX + placedChildren.First().Shift;
        var lastRoot = placedChildren.Last().Layout.RootX + placedChildren.Last().Shift;
        result.RootX = (firstRoot + lastRoot) / 2.0;
        result.Commands.Add(new TidyPlacement { Command = command, X = result.RootX, Depth = 0 });
        var rootHalf = (GetNodeWidth(command.Source) + HorizontalGap(command.Source)) / 2;
        result.Contours[0] = (result.RootX - rootHalf, result.RootX + rootHalf);

        foreach (var (child, shift) in placedChildren)
        {
            foreach (var p in child.Commands)
                result.Commands.Add(new TidyPlacement { Command = p.Command, X = p.X + shift, Depth = p.Depth + 1 });
            foreach (var a in child.AttachedStacks)
                result.AttachedStacks.Add(new AttachedPlacement { Parent = a.Parent, Units = a.Units, X = a.X + shift, Depth = a.Depth + 1 });
        }
        foreach (var (depth, contour) in combined) result.Contours[depth] = contour;
        return result;
    }

    private void ApplyTidyLayout(TidyLayoutResult layout, double shift, IReadOnlyDictionary<int, double> rankY)
    {
        foreach (var p in layout.Commands)
        {
            p.Command.Source.CanvasX = 100 + p.X + shift - GetNodeWidth(p.Command.Source) / 2;
            p.Command.Source.CanvasY = rankY.GetValueOrDefault(p.Depth, 70) - GetSurfaceInset(p.Command.Source);
        }
        foreach (var a in layout.AttachedStacks)
        {
            var centerX = 100 + a.X + shift;
            var y = rankY.GetValueOrDefault(a.Depth, 70) - GetSurfaceInset(a.Units[0]);
            for (var i = 0; i < a.Units.Count; i++)
            {
                var unit = a.Units[i];
                unit.CanvasX = centerX - GetNodeWidth(unit) / 2;
                unit.CanvasY = y;
                y += GetNodeHeight(unit) + UnitStackGap;
            }
        }
    }

    private void CollectVisibleDisplayNodes(DisplayCommandNode command, bool ancestorsVisible)
    {
        if (!ancestorsVisible) return;
        _visibleCanvasNodes.Add(command.Source);
        if (!command.Source.IsExpanded) return;
        foreach (var child in command.Subcommands) CollectVisibleDisplayNodes(child, true);
        foreach (var unit in command.AttachedUnits) _visibleCanvasNodes.Add(unit);
    }

    private Queue<Line> _connectorPool = new();
    private void RebuildConnectorVisuals()
    {
        _connectorPool = new Queue<Line>(ConnectorCanvas.Children.OfType<Line>());
        _connectorSegments.Clear();
        var visible = new HashSet<OobNode>(_visibleCanvasNodes);
        foreach (var command in EnumerateDisplayCommands())
        {
            var group = command.Source;
            if (!visible.Contains(group) || !group.IsExpanded) continue;
            var visibleChildren = command.Subcommands.Where(c => visible.Contains(c.Source)).ToList();
            var attached = command.AttachedUnits.Where(visible.Contains).ToList();
            var targets = new List<(double X, double Y)>();
            targets.AddRange(visibleChildren.Select(c => (c.Source.CanvasX + GetNodeWidth(c.Source) / 2, c.Source.CanvasY)));
            if (attached.Count > 0) targets.Add((attached[0].CanvasX + GetNodeWidth(attached[0]) / 2, attached[0].CanvasY));
            if (targets.Count == 0) continue;

            var parentX = group.CanvasX + GetNodeWidth(group) / 2;
            var parentY = group.CanvasY + GetNodeHeight(group);
            var firstChildY = targets.Min(t => t.Y);
            var busY = Math.Min(firstChildY - 24, parentY + 44);
            if (busY <= parentY) busY = parentY + 22;
            AddConnectorSegment(parentX, parentY, parentX, busY);
            var minX = targets.Min(t => t.X);
            var maxX = targets.Max(t => t.X);
            if (targets.Count > 1) AddConnectorSegment(minX, busY, maxX, busY);
            foreach (var target in targets)
            {
                if (targets.Count == 1 && Math.Abs(target.X - parentX) > 0.1) AddConnectorSegment(parentX, busY, target.X, busY);
                AddConnectorSegment(target.X, busY, target.X, target.Y);
            }
            for (var i = 0; i < attached.Count - 1; i++)
            {
                var x = attached[i].CanvasX + GetNodeWidth(attached[i]) / 2;
                AddConnectorSegment(x, attached[i].CanvasY + GetNodeHeight(attached[i]), x, attached[i + 1].CanvasY);
            }
        }
        while (_connectorPool.TryDequeue(out var unused)) ConnectorCanvas.Children.Remove(unused);
    }

    private IEnumerable<DisplayCommandNode> EnumerateDisplayCommands()
    {
        foreach (var root in _displayModel.Roots)
            foreach (var item in EnumerateDisplayCommands(root))
                yield return item;
    }

    private static IEnumerable<DisplayCommandNode> EnumerateDisplayCommands(DisplayCommandNode node)
    {
        yield return node;
        foreach (var child in node.Subcommands)
            foreach (var item in EnumerateDisplayCommands(child))
                yield return item;
    }

    private void AddConnectorSegment(double x1, double y1, double x2, double y2)
    {
        var line = _connectorPool.TryDequeue(out var reused) ? reused : new Line { IsHitTestVisible = false };
        line.Stroke = new SolidColorBrush(Color.FromRgb(49, 67, 80));
        line.StrokeThickness = Ui("oob.connectors.thickness"); line.Opacity = Ui("oob.connectors.opacity");
        if (line.Parent is null) ConnectorCanvas.Children.Add(line);
        _connectorSegments.Add(new ConnectorSegment(line, x1, y1, x2, y2));
    }

    private void UpdateConnectorPositions()
    {
        foreach (var segment in _connectorSegments)
        {
            segment.Line.X1 = segment.X1 * _zoom + _panX;
            segment.Line.Y1 = segment.Y1 * _zoom + _panY;
            segment.Line.X2 = segment.X2 * _zoom + _panX;
            segment.Line.Y2 = segment.Y2 * _zoom + _panY;
        }
    }

    private void Union_Click(object sender, RoutedEventArgs e) => OpenWorkspace(0, _lastWorkspaces.GetValueOrDefault(0, "Armies"));
    private void Confederacy_Click(object sender, RoutedEventArgs e) => OpenWorkspace(1, _lastWorkspaces.GetValueOrDefault(1, "Armies"));
    private void SwitchNation(int nation)
    {
        if (_nation == nation && _roots.Count > 0) return;
        _nation = nation;
        ClearBatchSelection();
        _selectedUnit = null; _selectedNode = null;
        RebuildSide();
        UpdateNationButtons();
        ClearDetails();
        if (!_showRoster) FitOob();
    }
    private void UpdateNationButtons()
    {
        UnionButton.FontWeight = _nation == 0 ? FontWeights.Bold : FontWeights.Normal;
        ConfederacyButton.FontWeight = _nation == 1 ? FontWeights.Bold : FontWeights.Normal;
        UnionButton.BorderBrush = _nation == 0 ? new SolidColorBrush(Color.FromRgb(92, 169, 181)) : new SolidColorBrush(Color.FromRgb(68, 81, 95));
        ConfederacyButton.BorderBrush = _nation == 1 ? new SolidColorBrush(Color.FromRgb(92, 169, 181)) : new SolidColorBrush(Color.FromRgb(68, 81, 95));
    }

    private void NamingScheme_Click(object sender, RoutedEventArgs e)
    {
        if (_data.SaveDirectory is null)
        {
            MessageBox.Show("Load a save first so the naming designer can preview real units and Home State abbreviations.", "Naming Scheme", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var before = _editSession.Capture(_data.Units);
        var window = new NamingSchemeWindow(_data, _naming, _nation) { Owner = this };
        window.ShowDialog();
        if (window.WorkingNamesChanged)
        {
            _editSession.RecordExternalChange("Mass rename", before);
            RebuildSide();
            UpdateDirtyState();
            StatusText.Text = "Mass rename applied to working state. Ctrl+Z undoes it; review before Save Changes.";
        }
    }

    private void OobView_Click(object sender, RoutedEventArgs e) => SetView(false);
    private void RosterView_Click(object sender, RoutedEventArgs e) => SetView(true);
    private void SetView(bool roster)
    {
        _showRoster = roster;
        OobViewPanel.Visibility = roster ? Visibility.Collapsed : Visibility.Visible;
        RosterViewPanel.Visibility = roster ? Visibility.Visible : Visibility.Collapsed;
        UpdateViewButtons();
        if (roster) RefreshRoster();
    }
    private void UpdateViewButtons()
    {
        var active = new SolidColorBrush(Color.FromRgb(92, 169, 181));
        var normal = new SolidColorBrush(Color.FromRgb(68, 81, 95));
        FieldTreeButton.BorderBrush = !_showRoster ? active : normal;
        FieldRosterButton.BorderBrush = _showRoster ? active : normal;
    }

    private void OrganizationScaleChanged(object sender, RoutedEventArgs e)
    {
        if (_applyingUiSettings) return;
        OobPresentation.RegimentalScale = RegimentalScaleCheck.IsChecked == true;
        _ui.Set("oob.presentation.regimentalScale", OobPresentation.RegimentalScale ? 1 : 0);
        foreach (var group in _data.Groups.Values) group.NotifyPresentationChanged();
        foreach (var unit in _data.Units) unit.NotifyPresentationChanged();
        InvalidateCardMeasurements();
        RebuildSide();
        StatusText.Text = OobPresentation.RegimentalScale
            ? "Regimental-scale presentation active. Native GTCW Unit_Tier IDs are unchanged."
            : "Brigade-scale presentation active. Native GTCW Unit_Tier IDs are unchanged.";
        try { _ui.Save(); } catch (Exception ex) { ErrorLog.Write("Persist UI settings",ex); StatusText.Text += " UI setting could not be persisted: " + ex.Message; }
    }

    private void UpdateGameFolderStatus()
    {
        if (_installation.InstallationRoot is not null && _installation.ConfigDirectory is not null)
        {
            GameFolderStatusText.Text = "✓";
            GameFolderStatusText.Foreground = new SolidColorBrush(Color.FromRgb(114, 179, 139));
            GameFolderStatusText.ToolTip = $"Validated Grand Tactician folder:\n{_installation.InstallationRoot}\nConfig and Campaigns detected.";
        }
        else
        {
            GameFolderStatusText.Text = "○";
            GameFolderStatusText.Foreground = new SolidColorBrush(Color.FromRgb(211, 166, 83));
            GameFolderStatusText.ToolTip = "Grand Tactician installation has not been validated.";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.S && mods.HasFlag(ModifierKeys.Control)) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
        if (e.Key == Key.F && mods.HasFlag(ModifierKeys.Control)) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
        if (e.Key == Key.Z && mods.HasFlag(ModifierKeys.Control)) { UndoWorkingEdit(); e.Handled = true; return; }
        if (e.Key == Key.Y && mods.HasFlag(ModifierKeys.Control)) { RedoWorkingEdit(); e.Handled = true; return; }
        if (e.Key == Key.Tab) { SwitchNation(_nation == 0 ? 1 : 0); e.Handled = true; return; }
        if (e.Key == Key.F && !_showRoster) { FocusSelected(); e.Handled = true; return; }
        if (e.Key == Key.Home && !_showRoster) { FitOob(); e.Handled = true; return; }
        if (_showRoster) return;
        var step = mods.HasFlag(ModifierKeys.Shift) ? 150 : 55;
        if (e.Key == Key.W) { _panY += step; e.Handled = true; }
        else if (e.Key == Key.S) { _panY -= step; e.Handled = true; }
        else if (e.Key == Key.A) { _panX += step; e.Handled = true; }
        else if (e.Key == Key.D) { _panX -= step; e.Handled = true; }
        if (e.Handled) ApplyCanvasTransform();
    }

    private void OobViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mouse = e.GetPosition(OobViewport);
        var fast = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var factor = e.Delta > 0 ? (fast ? 1.28 : 1.12) : (fast ? 1 / 1.28 : 1 / 1.12);
        var oldZoom = _zoom;
        var newZoom = Math.Clamp(_zoom * factor, 0.02, 2.50);
        var worldX = (mouse.X - _panX) / oldZoom;
        var worldY = (mouse.Y - _panY) / oldZoom;
        _zoom = newZoom;
        _panX = mouse.X - worldX * _zoom;
        _panY = mouse.Y - worldY * _zoom;
        ApplyCanvasTransform();
        e.Handled = true;
    }

    private void OobViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right && e.ChangedButton != MouseButton.Middle) return;
        _panCandidate = true; _panButton = e.ChangedButton;
        _panStart = e.GetPosition(OobViewport);
        _panStartX = _panX; _panStartY = _panY;
        if (_panButton == MouseButton.Middle)
        {
            _isPanning = true; OobViewport.CaptureMouse(); e.Handled = true;
        }
    }
    private void OobViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panCandidate && !_isPanning) return;
        var p = e.GetPosition(OobViewport);
        if (!_isPanning && _panButton == MouseButton.Right && (Math.Abs(p.X - _panStart.X) > 4 || Math.Abs(p.Y - _panStart.Y) > 4))
        {
            _isPanning = true; OobViewport.CaptureMouse();
        }
        if (!_isPanning) return;
        _panX = _panStartX + (p.X - _panStart.X);
        _panY = _panStartY + (p.Y - _panStart.Y);
        ApplyCanvasTransform();
    }
    private void OobViewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != _panButton) return;
        var wasPanning = _isPanning;
        _isPanning = false; _panCandidate = false;
        if (OobViewport.IsMouseCaptured) OobViewport.ReleaseMouseCapture();
        if (wasPanning) e.Handled = true;
    }
    private void ApplyCanvasTransform()
    {
        // Project world coordinates into the viewport ourselves. This avoids relying
        // on WPF to render a giant transformed panel, which caused viewport-dependent
        // clipping as the application window was resized.
        foreach (var child in NodeCanvas.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is not OobNode node) continue;
            Canvas.SetLeft(child, node.CanvasX * _zoom + _panX);
            Canvas.SetTop(child, node.CanvasY * _zoom + _panY);
            child.RenderTransformOrigin = new Point(0, 0);
            child.RenderTransform = new ScaleTransform(_zoom, _zoom);
        }

        UpdateConnectorPositions();
        ZoomText.Text = $"{_zoom * 100:0}%";
        UpdateZoomDetailLevels();
    }

    private bool ShowCardDetails => _zoom >= Ui("oob.zoom.detail");
    private bool? _renderedCardDetails;
    private void UpdateZoomDetailLevels()
    {
        if (_renderedCardDetails == ShowCardDetails) return;
        _renderedCardDetails = ShowCardDetails;
        // Keep the live visual/binding trees. Only detail visibility and geometry change.
        // Retain both measured footprints so subsequent crossings need no probe templates.
        WithViewportAnchor(() => {
            foreach (var card in NodeCanvas.Children.OfType<FrameworkElement>()) {
                var details = FindNamedDescendant<FrameworkElement>(card, "DetailBody");
                if (details is not null) details.Visibility = ShowCardDetails ? Visibility.Visible : Visibility.Collapsed;
            }
            LayoutDisplayModel();
            RebuildConnectorVisuals();
            ApplyCanvasTransform();
        });
    }

    // Keep the nearest visible card fixed on screen across font/detail reflow.
    // Manual nudges remain separate deltas applied by LayoutDisplayModel.
    private void ReflowCardsAtAnchor()
    {
        _renderedCardDetails = ShowCardDetails;
        InvalidateCardMeasurements();
        WithViewportAnchor(RefreshOobCanvas);
    }

    private void WithViewportAnchor(Action reflow)
    {
        var anchor = _visibleCanvasNodes.OrderBy(n =>
            Math.Pow(n.CanvasX * _zoom + _panX - OobViewport.ActualWidth / 2, 2) +
            Math.Pow(n.CanvasY * _zoom + _panY - OobViewport.ActualHeight / 2, 2)).FirstOrDefault();
        var before = anchor is null ? new Point() : new Point(anchor.CanvasX, anchor.CanvasY);
        reflow();
        if (anchor is not null) {
            _panX += (before.X - anchor.CanvasX) * _zoom;
            _panY += (before.Y - anchor.CanvasY) * _zoom;
            ApplyCanvasTransform();
        }
    }
    private static T? FindNamedDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is FrameworkElement fe && fe.Name == name && fe is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindNamedDescendant<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }
        return null;
    }

    private void FitOob_Click(object sender, RoutedEventArgs e) => FitOob();
    private void FitOob()
    {
        if (_visibleCanvasNodes.Count == 0 || OobViewport.ActualWidth <= 1 || OobViewport.ActualHeight <= 1) return;
        var maxX = _visibleCanvasNodes.Max(n => n.CanvasX + GetNodeWidth(n));
        var maxY = _visibleCanvasNodes.Max(n => n.CanvasY + GetNodeHeight(n));
        var minX = _visibleCanvasNodes.Min(n => n.CanvasX);
        var minY = _visibleCanvasNodes.Min(n => n.CanvasY);
        const double cameraPadding = 140;
        var w = Math.Max(1, maxX - minX + cameraPadding * 2);
        var h = Math.Max(1, maxY - minY + cameraPadding * 2);
        _zoom = Math.Clamp(Math.Min(OobViewport.ActualWidth / w, OobViewport.ActualHeight / h), 0.01, 1.0);
        var contentCenterX = (minX + maxX) / 2.0;
        var contentCenterY = (minY + maxY) / 2.0;
        _panX = OobViewport.ActualWidth / 2.0 - contentCenterX * _zoom;
        _panY = OobViewport.ActualHeight / 2.0 - contentCenterY * _zoom;
        ApplyCanvasTransform();
    }
    private void FocusInitialCommand()
    {
        // Initial presentation is the whole side, fully expanded and side-by-side.
        FitOob();
    }

    private void FocusSelected_Click(object sender, RoutedEventArgs e) => FocusSelected();
    private void FocusSelected()
    {
        if (_selectedNode is not null) FocusNode(_selectedNode);
    }
    private void FocusNode(OobNode node)
    {
        if (!_visibleCanvasNodes.Contains(node)) return;
        var width = GetNodeWidth(node);
        var height = GetNodeHeight(node);
        _panX = OobViewport.ActualWidth / 2 - (node.CanvasX + width / 2) * _zoom;
        _panY = OobViewport.ActualHeight / 2 - (node.CanvasY + height / 2) * _zoom;
        ApplyCanvasTransform();
    }

    private void ToggleGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupNode g) return;
        var expand = !g.IsExpanded;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) SetExpandedRecursive(g, expand); else g.IsExpanded = expand;
        RefreshOobCanvas();
        e.Handled = true;
    }
    private static void SetExpandedRecursive(GroupNode g, bool value)
    {
        g.IsExpanded = value;
        foreach (var child in g.Children.OfType<GroupNode>()) SetExpandedRecursive(child, value);
    }

    private void NodeCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin && FindInputAncestor(origin)) return;
        if ((sender as FrameworkElement)?.DataContext is not OobNode node) return;
        _selectedNode = node;
        _dragStart = e.GetPosition(OobViewport);
        if (node is CombatUnitNode u)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ToggleBatchSelection(u);
            else if (!_selectedUnits.Contains(u)) SelectOnlyUnit(u);
            _dragUnit = CanEdit(u) ? u : null; ShowUnit(u);
            if (e.ClickCount >= 2) { OpenFloatingDetail(u); e.Handled = true; }
        }
        else
        {
            _dragUnit = null;
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ClearBatchSelection();
            ShowGroup((GroupNode)node);
            StatusText.Text = $"Selected command: {node.Name}";
        }
    }
    private void UnitCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is DependencyObject input && FindInputAncestor(input)) return;
        if (e.LeftButton != MouseButtonState.Pressed || _dragUnit is null) return;
        var p = e.GetPosition(OobViewport);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        BeginUnitDrag((DependencyObject)sender, _dragUnit);
        _dragUnit = null;
    }
    private void GroupCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is DependencyObject input && FindInputAncestor(input)) return;
        if (e.LeftButton != MouseButtonState.Pressed || _selectedNode is not GroupNode group || (sender as FrameworkElement)?.DataContext != group) return;
        var p = e.GetPosition(OobViewport);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        BeginGroupDrag((DependencyObject)sender, group);
    }
    private void ShowUnit(CombatUnitNode u)
    {
        _selectedUnit = u; _selectedNode = u;
        NoSelectionPanel.Visibility = Visibility.Collapsed;
        InspectionText.Visibility = CanEdit(u) ? Visibility.Collapsed : Visibility.Visible;
        InspectionText.Text = InspectionDescription(u);
        ShowTypedDetails(u);
    }
    private void ClearDetails()
    {
        SharedDetailPanel.Children.Clear(); SharedDetailPanel.Visibility = Visibility.Collapsed;
        _selectedUnit = null; InspectionText.Visibility = Visibility.Collapsed; NoSelectionPanel.Visibility = Visibility.Visible;
    }
    private void ApplyDetails_Click(object sender, RoutedEventArgs e) => ApplyDetails();

    private bool DetailInputsDifferFromSelectedUnit() => TypedDirty;

    private bool ApplyDetails() => CommitTypedDrafts();

    private void DetailChanged(object sender, EventArgs e) { }

    private void UnitTransferAdjust_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CombatUnitNode unit) return;
        ApplyTransferEdit(unit, Math.Max(0, unit.TransferDays + SignedStep(sender)), $"Adjust transfer ETA for {unit.Name}");
        e.Handled = true;
    }

    private void DetailTransferAdjust_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUnit is null) return;
        ApplyTransferEdit(_selectedUnit, Math.Max(0, _selectedUnit.TransferDays + SignedStep(sender)), $"Adjust transfer ETA for {_selectedUnit.Name}");
    }

    private void GroupTransferAdjust_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupNode group) return;
        var delta = SignedStep(sender);
        var all = EnumerateCombatUnits(group).ToList();
        var editable = all.Where(u => CanEdit(u) && u.PathLinkStatus == PathLinkStatus.Confirmed).ToList();
        var skipped = all.Count - editable.Count;
        if (editable.Count == 0)
        {
            MessageBox.Show(this, "No subordinate units have uniquely confirmed paths.dat links, so the transfer batch was not applied.", "Transfer edit blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var changed = _editSession.Execute($"Adjust transfers under {group.Name}", editable, () =>
        {
            foreach (var unit in editable) unit.TransferDays = Math.Max(0, unit.TransferDays + delta);
        });
        if (!changed) return;
        RefreshSummaries(relayout: true, changedUnits: editable);
        UpdateDirtyState();
        if (_selectedUnit is not null && editable.Contains(_selectedUnit)) ShowUnit(_selectedUnit);
        StatusText.Text = $"Adjusted transfer ETA under {group.Name} by {delta:+#;-#;0} day(s) for {editable.Count:N0} unit(s)" + (skipped > 0 ? $"; {skipped:N0} unsafe path link(s) skipped." : ".");
        e.Handled = true;
    }

    private void TransferBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as TextBox)?.DataContext is not CombatUnitNode unit) return;
        if (int.TryParse(((TextBox)sender).Text, out var days) && days >= 0)
            ApplyTransferEdit(unit, days, $"Set transfer ETA for {unit.Name}");
        else ((TextBox)sender).Text = unit.TransferDays.ToString();
    }

    private void ApplyTransferEdit(CombatUnitNode unit, int days, string description)
    {
        if (!CanEdit(unit)) return;
        if (days == unit.TransferDays) return;
        var result = _validation.ValidateTransfer(unit, days);
        if (result.Severity == ValidationSeverity.Error)
        {
            MessageBox.Show(this, result.Message, "Transfer edit blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_editSession.Execute(description, new[] { unit }, () => unit.TransferDays = days)) return;
        RefreshSummaries(relayout: true, changedUnits: new[] { unit });
        UpdateDirtyState();
        if (_selectedUnit == unit) ShowUnit(unit);
    }

    private static IEnumerable<CombatUnitNode> EnumerateCombatUnits(OobNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is CombatUnitNode unit) yield return unit;
            else foreach (var nested in EnumerateCombatUnits(child)) yield return nested;
        }
    }

    private static int SignedStep(object sender)
    {
        var sign = int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out var s) ? s : 1;
        var mods = Keyboard.Modifiers;
        var step = mods.HasFlag(ModifierKeys.Control) && mods.HasFlag(ModifierKeys.Shift) ? 20 : mods.HasFlag(ModifierKeys.Shift) ? 10 : mods.HasFlag(ModifierKeys.Control) ? 5 : 1;
        return sign * step;
    }

    private void RefreshSummaries(bool relayout = false, IEnumerable<CombatUnitNode>? changedUnits = null)
    {
        var unitsToRefresh = changedUnits?.Distinct().ToList() ?? _data.Units.ToList();
        _data.RefreshGroupAggregates(changedUnits is null ? null : unitsToRefresh);
        foreach (var unit in unitsToRefresh) unit.RefreshDisplay();
        if (relayout) RefreshOobCanvas();
        RefreshRoster();
        RefreshAlerts();
    }

    private void RefreshRoster()
    {
        var wasSyncing = _syncingRosterSelection;
        _syncingRosterSelection = true;
        try
        {
        var search = SearchBox.Text?.Trim() ?? string.Empty;
        _rosterRows.Clear();
        var filterHq = FilterHeadquartersCheck?.IsChecked == true;

        if (_rosterListing)
        {
            var roots = _displayModel.Roots.Select(r => r.Source);
            foreach (var root in roots)
                AddRosterTreeRows(root, 0, search, false, filterHq);
        }
        else
        {
            var nodes = _roots.SelectMany(FlattenRosterNodes)
                .Where(n => n is GroupNode cg ? _commandClassifier.CategoryForGroup(cg, _data.Groups) == _category : IsUnitInCurrentCommandCategory((CombatUnitNode)n))
                .Where(n => !filterHq || n is not GroupNode)
                .Where(n => n is GroupNode g ? string.IsNullOrWhiteSpace(search) || g.Name.Contains(search, StringComparison.OrdinalIgnoreCase) : MatchesSearch((CombatUnitNode)n, search));
            foreach (var node in SortRosterNodes(nodes))
                _rosterRows.Add(new RosterRow { Node = node, Depth = 0, LandMetrics = node is GroupNode hq ? hq.IsLandCommand : node is CombatUnitNode cu && CanEdit(cu) });
        }

        foreach (var unit in _displayModel.UnattachedUnits.Where(u => MatchesSearch(u, search)))
            _rosterRows.Add(new RosterRow { Node = unit, Depth = 0, LandMetrics = false });
        RestoreRosterSelection();
        UpdateRosterModeButtons();
        }
        finally { _syncingRosterSelection = wasSyncing; }
        if (!wasSyncing) RestoreRosterSelection();
    }

    private bool AddRosterTreeRows(OobNode node, int depth, string search, bool ancestorMatched, bool filterHq)
    {
        if (node is GroupNode categoryGroup && _commandClassifier.CategoryForGroup(categoryGroup, _data.Groups) != _category) return false;
        if (node is CombatUnitNode categoryUnit && !IsUnitInCurrentCommandCategory(categoryUnit)) return false;
        var thisMatches = ancestorMatched || string.IsNullOrWhiteSpace(search) || node.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
        if (!thisMatches && !RosterSubtreeMatches(node, search)) return false;
        if (!(filterHq && node is GroupNode)) _rosterRows.Add(new RosterRow { Node = node, Depth = depth, LandMetrics = node is GroupNode hq ? hq.IsLandCommand : node is CombatUnitNode cu && CanEdit(cu) });
        if (node is GroupNode group && (group.IsExpanded || filterHq))
        {
            var children = _rosterSortKey == "OobOrder" ? OrderedChildren(group) : SortRosterNodes(group.Children);
            foreach (var child in children)
                AddRosterTreeRows(child, depth + 1, search, thisMatches, filterHq);
        }
        return true;
    }

    private static IEnumerable<OobNode> FlattenRosterNodes(OobNode node)
    {
        yield return node;
        if (node is GroupNode group)
            foreach (var child in group.Children)
                foreach (var nested in FlattenRosterNodes(child)) yield return nested;
    }

    private IEnumerable<OobNode> SortRosterNodes(IEnumerable<OobNode> nodes)
    {
        if (_rosterSortKey == "OobOrder") return nodes;
        Func<OobNode, string?> key = n => RosterSortValue(n, _rosterSortKey);
        return _rosterSortDescending
            ? nodes.OrderBy(n => key(n) is null ? 1 : 0).ThenByDescending(n => key(n), StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            : nodes.OrderBy(n => key(n) is null ? 1 : 0).ThenBy(n => key(n), StringComparer.OrdinalIgnoreCase).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static string? RosterSortValue(OobNode node, string key)
    {
        var u = node as CombatUnitNode;
        var g = node as GroupNode;
        return key switch
        {
            "Name" => node.Name,
            "TypeName" => u?.TypeName ?? g?.TierName,
            "HomeStateName" => u?.HomeStateName,
            "CommandPath" => u?.CommandPath ?? g?.CommandPath,
            "CommanderName" => u?.CommanderDisplayName ?? g?.CommanderDisplayName,
            "FieldStrength" => node.Metrics.Assigned.ToString("D12"),
            "Casualties" => node.Metrics.Casualties.ToString("D12"),
            "Maximum" => node.Metrics.Maximum.ToString("D12"),
            "Guns" => node.Metrics.Guns?.ToString("D12"),
            "StrengthPercent" => (node.Metrics.Maximum > 0 ? node.Metrics.Assigned * 100.0 / node.Metrics.Maximum : 0).ToString("000000.000", System.Globalization.CultureInfo.InvariantCulture),
            "WeaponName" => u?.WeaponName,
            "RaisedText" => u?.RaisedText,
            "ContractMonths" => u?.ContractMonths.ToString("D6"),
            "ContractRemaining" => u?.ContractRemainingMonths?.ToString("D6"),
            "Experience" => u?.ExperienceRaw.ToString("0000000000.000000", System.Globalization.CultureInfo.InvariantCulture),
            "ETA" => u?.TransferDays.ToString("D6"),
            "Readiness" => null,
            _ => node.Name
        };
    }

    private static bool RosterSubtreeMatches(OobNode node, string search)
    {
        if (node is CombatUnitNode unit) return MatchesSearch(unit, search);
        if (node.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) return true;
        return node.Children.Any(child => RosterSubtreeMatches(child, search));
    }

    private static bool MatchesSearch(CombatUnitNode unit, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return unit.SearchIndex.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (_data.SaveDirectory is not null) { if (ManagementWorkspace.Visibility == Visibility.Visible) RefreshManagement(); else RefreshRoster(); } }
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var first = _data.Units.Where(u => u.Nation == _nation && IsUnitInCurrentCommandCategory(u)).FirstOrDefault(u => MatchesSearch(u, SearchBox.Text));
        if (first is not null) NavigateToUnit(first, _showRoster); e.Handled = true;
    }

    private void RosterListing_Click(object sender, RoutedEventArgs e) { _rosterListing = true; RefreshRoster(); }
    private void RosterFlat_Click(object sender, RoutedEventArgs e) { _rosterListing = false; RefreshRoster(); }
    private void UpdateRosterModeButtons()
    {
        RosterListingButton.FontWeight = _rosterListing ? FontWeights.Bold : FontWeights.Normal;
        RosterFlatButton.FontWeight = !_rosterListing ? FontWeights.Bold : FontWeights.Normal;
    }

    private void RosterFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_data.SaveDirectory is not null) RefreshRoster();
    }

    private void RosterResetSort_Click(object sender, RoutedEventArgs e)
    {
        _rosterSortKey = "OobOrder";
        _rosterSortDescending = false;
        foreach (var column in RosterGrid.Columns) column.SortDirection = null;
        RefreshRoster();
    }

    private void RosterGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var key = string.IsNullOrWhiteSpace(e.Column.SortMemberPath) ? "Name" : e.Column.SortMemberPath;
        if (_rosterListing && key == "Name")
        {
            _rosterSortKey = "OobOrder";
            _rosterSortDescending = false;
        }
        else if (_rosterSortKey == key) _rosterSortDescending = !_rosterSortDescending;
        else { _rosterSortKey = key; _rosterSortDescending = false; }
        foreach (var column in RosterGrid.Columns) if (!ReferenceEquals(column, e.Column)) column.SortDirection = null;
        e.Column.SortDirection = _rosterSortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        RefreshRoster();
    }

    private void RosterToggleGroup_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RosterRow row || row.Group is not GroupNode group) return;
        group.IsExpanded = !group.IsExpanded;
        RefreshRoster();
        e.Handled = true;
    }

    private void RosterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingRosterSelection) return;
        _syncingRosterSelection = true;
        try
        {
            ClearBatchSelection(updateUi: false);
            foreach (var row in RosterGrid.SelectedItems.OfType<RosterRow>())
                if (row.Unit is CombatUnitNode unit) AddBatchSelection(unit, updateUi: false);
            var primary = RosterGrid.SelectedItems.OfType<RosterRow>().Select(r => r.Unit).FirstOrDefault(u => u is not null);
            if (RosterGrid.SelectedItem is RosterRow { Group: GroupNode selectedGroup }) ShowGroup(selectedGroup);
            else if (primary is not null) ShowUnit(primary);
            UpdateSelectionUi();
        }
        finally { _syncingRosterSelection = false; }
    }

    private void RestoreRosterSelection()
    {
        if (_syncingRosterSelection) return;
        _syncingRosterSelection = true;
        try
        {
            RosterGrid.SelectedItems.Clear();
            foreach (var row in _rosterRows)
                if (row.Unit is CombatUnitNode unit && _selectedUnits.Contains(unit)) RosterGrid.SelectedItems.Add(row);
            var first = RosterGrid.SelectedItems.OfType<RosterRow>().FirstOrDefault();
            if (first is not null) RosterGrid.ScrollIntoView(first);
        }
        finally { _syncingRosterSelection = false; }
    }

    private void RosterGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject input && FindInputAncestor(input)) return;
        // Double-click belongs to the cell editor. Explicit context actions navigate to OOB.
        if (RosterGrid.CurrentCell.Column is { IsReadOnly: false }) { RosterGrid.BeginEdit(); e.Handled = true; }
    }

    private void SelectOnlyUnit(CombatUnitNode unit)
    {
        ClearBatchSelection(updateUi: false);
        AddBatchSelection(unit, updateUi: false);
        UpdateSelectionUi();
    }

    private void ToggleBatchSelection(CombatUnitNode unit)
    {
        if (_selectedUnits.Contains(unit)) RemoveBatchSelection(unit, updateUi: false);
        else AddBatchSelection(unit, updateUi: false);
        UpdateSelectionUi();
    }

    private void AddBatchSelection(CombatUnitNode unit, bool updateUi = true)
    {
        if (_selectedUnits.Add(unit)) unit.SetBatchSelected(true);
        if (updateUi) UpdateSelectionUi();
    }

    private void RemoveBatchSelection(CombatUnitNode unit, bool updateUi = true)
    {
        if (_selectedUnits.Remove(unit)) unit.SetBatchSelected(false);
        if (updateUi) UpdateSelectionUi();
    }

    private void ClearBatchSelection(bool updateUi = true)
    {
        foreach (var unit in _selectedUnits.ToList()) unit.SetBatchSelected(false);
        _selectedUnits.Clear();
        if (updateUi) UpdateSelectionUi();
    }

    private void UpdateSelectionUi()
    {
        var count = _selectedUnits.Count;
        var visibleIds = _rosterRows.Where(r => r.Unit is not null).Select(r => r.Unit!.UnitId).ToHashSet();
        var hiddenCount = _showRoster ? _selectedUnits.Count(u => !visibleIds.Contains(u.UnitId)) : 0;
        SelectionCountText.Text = $"{count:N0} selected" + (hiddenCount > 0 ? $" ({hiddenCount} hidden)" : "");
        BatchEditButton.IsEnabled = ManagementWorkspace.Visibility == Visibility.Visible ? CanBatchManagement : count > 0 && _selectedUnits.All(CanEdit);
        if(ManagementWorkspace.Visibility == Visibility.Visible)UpdateManagementSelectionUi();
        else {
            EditSelectedButton.IsEnabled=count>0 && _selectedUnits.All(CanEdit) || count==0 && _selectedNode is GroupNode {IsLandCommand:true};
            ReselectBatchButton.IsEnabled=_workspace is "Armies" or "Garrisons";
            ClearSelectionButton.IsEnabled=count>0;
        }
        if (!_syncingRosterSelection && _showRoster) RestoreRosterSelection();
    }

    private void BatchEdit_Click(object sender, RoutedEventArgs e)
    {
        if(ManagementWorkspace.Visibility==Visibility.Visible){if(CanBatchManagement)EditManagement_Click(sender,e);return;}
        if (_selectedUnits.Any(u => !CanEdit(u))) return;
        var units = _selectedUnits.OrderBy(u => u.CommandPath).ThenBy(u => u.Name).ToList();
        if (units.Count == 0)
        {
            MessageBox.Show(this, "Select one or more combat units first.", "Batch Edit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _previousBatches[WorkspaceKey] = units.Select(u => u.UnitId).ToHashSet();
        var dialog = new BatchEditWindow(units, _data.WeaponOptions, _batchPlanner) { Owner = this };
        if (dialog.ShowDialog() != true || !dialog.Applied || dialog.Plan is null) return;
        var plan = dialog.Plan;
        if (!_editSession.Execute($"Batch edit {plan.ChangedUnitCount:N0} unit(s)", plan.ChangedUnits, () => _batchPlanner.Apply(plan))) return;
        RefreshSummaries(plan.Request.EtaDays.HasValue, plan.ChangedUnits);
        UpdateDirtyState();
        var skipped = plan.SkippedUnitCount > 0 ? $" • {plan.SkippedUnitCount:N0} unit(s) had skipped fields" : string.Empty;
        StatusText.Text = $"Batch working changes applied to {plan.ChangedUnitCount:N0} unit(s){skipped}. Ctrl+Z undoes the batch; Save Changes writes it to disk.";
    }

    private void RefreshAlerts()
    {
        var all = _data.Units.Where(u => u.Nation == _nation && CanEdit(u) && IsUnitInCurrentCommandCategory(u) && u.ReadinessStatus != ReadinessLevel.Normal)
            .OrderByDescending(u => u.ReadinessStatus).ThenBy(u => u.StrengthPercent).ThenBy(u => u.Name).ToList();
        _alerts.Clear(); foreach (var u in all) _alerts.Add(new ReadinessAlertItem(u));
        var yellow = all.Count(u => u.ReadinessStatus == ReadinessLevel.Yellow); var orange = all.Count(u => u.ReadinessStatus == ReadinessLevel.Orange); var red = all.Count(u => u.ReadinessStatus == ReadinessLevel.Red);
        YellowCountText.Text = $"● {yellow:N0}"; OrangeCountText.Text = $"● {orange:N0}"; RedCountText.Text = $"● {red:N0}";
        AlertSummaryText.Text = $"{all.Count:N0} flagged";
    }
    private void AlertList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AlertList.SelectedItem is ReadinessAlertItem a) NavigateToUnit(a.Unit, false);
    }

    private void NavigateToUnit(CombatUnitNode unit, bool roster)
    {
        var targetCategory = _commandClassifier.CategoryForUnit(unit, _data.Groups);
        if (_nation != unit.Nation) { _nation = unit.Nation; UpdateNationButtons(); }
        if (_category != targetCategory) SwitchCommandCategory(targetCategory);
        SelectOnlyUnit(unit); _selectedUnit = unit; _selectedNode = unit; ShowUnit(unit);
        if (roster)
        {
            SetView(true); SelectOnlyUnit(unit); RefreshRoster(); return;
        }
        SetView(false);
        ExpandAncestors(unit);
        RefreshOobCanvas();
        _selectedUnit = unit;
        _selectedNode = unit;
        StatusText.Text = $"Selected unit: {unit.Name} • {unit.CommandPath}";
        FocusNode(unit);
    }
    private void ExpandAncestors(CombatUnitNode unit)
    {
        var id = unit.ParentId; var guard = 0;
        while (id >= 0 && _data.Groups.TryGetValue(id, out var g) && guard++ < 32) { g.IsExpanded = true; id = g.ParentId; }
    }

    private void ViewInRoster_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is CombatUnitNode u) NavigateToUnit(u, true);
    }
    private void ViewGroupInRoster_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not GroupNode g) return;
        SearchBox.Text = g.Name; SetView(true); RefreshRoster();
    }
    private void RosterViewInOob_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is RosterRow { Unit: CombatUnitNode u }) NavigateToUnit(u, false);
    }
    private void FocusNode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is OobNode n) { SetView(false); _selectedNode = n; FocusNode(n); }
    }
    private void OpenDetailCard_Click(object sender, RoutedEventArgs e) { if ((sender as MenuItem)?.DataContext is CombatUnitNode u) OpenFloatingDetail(u); }
    private void RosterOpenDetail_Click(object sender, RoutedEventArgs e) { if (RosterGrid.SelectedItem is RosterRow { Unit: CombatUnitNode u }) OpenFloatingDetail(u); }
    private void SelectedOpenDetail_Click(object sender, RoutedEventArgs e) { if (_selectedUnit is not null) OpenFloatingDetail(_selectedUnit); }
    private void UnitCard_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if ((sender as FrameworkElement)?.DataContext is CombatUnitNode u) OpenFloatingDetail(u); }

    private static string InspectionDescription(CombatUnitNode u) =>
        $"INSPECTION ONLY — DATA MAPPING WIP\n\n{u.Name}\n{u.CommandPath}\nCommander: {u.CommanderName}\n\nRaw save fields\nUnit_ID: {u.UnitId}\nParent_ID: {u.ParentId}\nUnit_type: {u.UnitType}\nUnit_Tier: {u.UnitTier}\nState_ID: {u.StateId}\nWeapon_ID: {u.WeaponId}\nTotal_Men: {u.TotalMenRaw}\nSickRatio: {u.CasualtyRatioRaw:R}\nTransfer_Time: {u.TransferTimeRaw:R}\n\nLand strength, weapon and contract interpretations are unavailable for this asset.";
    private void OpenFloatingDetail(CombatUnitNode u)
    {
        if (CanEdit(u)) { OpenTypedCard(u); return; }
        if (!CanEdit(u))
        {
            new Window { Owner = this, Title = u.Name + " — Inspection", Width = 440, Height = 540, Background = (Brush)FindResource("SurfaceBrush"), Foreground = (Brush)FindResource("TextBrush"), Content = new TextBox { Text = InspectionDescription(u), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) } }.Show();
            return;
        }
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = u.Name, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = u.CommandPath, Foreground = new SolidColorBrush(Color.FromRgb(125, 145, 160)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 12) });
        foreach (var (label, value) in new[]
        {
            ("Field Strength", u.FieldStrength.ToString("N0")), ("Casualties", u.Casualties.ToString("N0")), ("Manpower Strength", $"{u.StrengthPercent}%"),
            ("Weapon", u.WeaponName), ("Home State", u.HomeStateName), ("Commander", u.CommanderName), ("Raised", u.RaisedText), ("Contract", $"{u.ContractMonths} months"), ("Contract Remaining", u.ContractRemainingText), ("Experience", u.ExperienceRaw.ToString("0.###")), ("Transfer ETA", $"{u.TransferDays} days"), ("Transfer Link", $"{u.PathLinkStatus}: {u.PathLinkMessage}")
        })
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var l = new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(142, 154, 167)) }; var v = new TextBlock { Text = value, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1); grid.Children.Add(l); grid.Children.Add(v); panel.Children.Add(grid);
        }
        var win = new Window { Title = u.Name, Width = 420, Height = 430, Background = new SolidColorBrush(Color.FromRgb(23, 31, 39)), Foreground = Brushes.White, Content = new ScrollViewer { Content = panel }, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        win.Show();
    }

    private void UpdateLimits()
    {
        var m = _data.ReadUnitMaximums(); UnitLimitsText.Text = $"INF {m["Infantry"]:N0}   CAV {m["Cavalry"]:N0}   ART {m["Artillery"]:N0}";
    }
    private bool HasPendingWorkingChanges => _data.HasUnsavedChanges || DisplayDirty || DetailInputsDifferFromSelectedUnit();
    private void UpdateDirtyState() => SetDirty(_data.HasUnsavedChanges || DisplayDirty || TypedDirty);

    private void UndoWorkingEdit()
    {
        if (!_editSession.Undo(out var description)) return;
        RebuildSide();
        if (_selectedUnit is not null) ShowUnit(_selectedUnit);
        else if (_selectedNode is GroupNode selectedGroup) ShowGroup(selectedGroup);
        UpdateDirtyState();
        StatusText.Text = $"Undid: {description}.";
        _management=null;if(ManagementWorkspace.Visibility==Visibility.Visible)RefreshManagement();
    }

    private void RedoWorkingEdit()
    {
        if (!_editSession.Redo(out var description)) return;
        RebuildSide();
        if (_selectedUnit is not null) ShowUnit(_selectedUnit);
        else if (_selectedNode is GroupNode selectedGroup) ShowGroup(selectedGroup);
        UpdateDirtyState();
        StatusText.Text = $"Redid: {description}.";
        _management=null;if(ManagementWorkspace.Visibility==Visibility.Visible)RefreshManagement();
    }

    private async Task<bool> ConfirmCanAbandonWorkingChangesAsync()
    {
        if (!HasPendingWorkingChanges) return true;
        if (_data.IsReadOnlySave)
        {
            var readOnlyChoice = MessageBox.Show(this, "This read-only save has working changes that cannot be written back to the ZIP. Discard those working changes?", "Unsaved working changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return readOnlyChoice == MessageBoxResult.Yes;
        }

        var choice = MessageBox.Show(this, "You have unsaved working changes.\n\nYes = Save them now\nNo = Discard them\nCancel = Stay on this save", "Unsaved working changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.No) return true;
        return await SaveCurrentAsync();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyIndicator.Text = dirty ? "● UNSAVED" : string.Empty;
        Title = "Aide-de-Camp 0.8.10" + (dirty ? " *" : "");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        if (_saveInProgress) { e.Cancel = true; return; }
        if (!HasPendingWorkingChanges) { base.OnClosing(e); return; }

        e.Cancel = true;
        if (_data.IsReadOnlySave)
        {
            var discard = MessageBox.Show(this, "This read-only save has unsaved working changes. Discard them and close?", "Unsaved working changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (discard == MessageBoxResult.Yes) RequestDeferredClose();
            return;
        }

        var choice = MessageBox.Show(this, "You have unsaved working changes.\n\nYes = Save and close\nNo = Discard and close\nCancel = Keep editing", "Unsaved working changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.No) { RequestDeferredClose(); return; }
        if (choice != MessageBoxResult.Yes) return;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (await SaveCurrentAsync()) RequestDeferredClose();
        }));
    }

    // OnClosing is still inside WPF's close transaction. Calling Close() from it
    // re-enters Window.VerifyNotClosing and produces the shutdown exception.
    private void RequestDeferredClose()
    {
        _allowClose = true;
        Dispatcher.BeginInvoke(new Action(Close), System.Windows.Threading.DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e) { _cardCacheClosed = true; InvalidateCardMeasurements(); _data.Dispose(); base.OnClosed(e); }
    private void RosterShowChanges_Click(object sender, RoutedEventArgs e)
    {
        if (RosterGrid.SelectedItem is not RosterRow { Unit: CombatUnitNode unit }) return;
        var fields = new (string Key, string Label)[]
        {
            ("Name", "Unit name"), ("HomeState", "Home state"), ("FieldStrength", "Field strength"), ("Casualties", "Casualties"),
            ("Weapon", "Weapon"), ("Contract", "Contract length"), ("ContractRemaining", "Contract remaining"),
            ("Experience", "Experience"), ("ETA", "Transfer ETA"), ("Parent", "Command assignment")
        };
        var changes = fields.Select(f => unit.GetEditTooltip(f.Key, f.Label)).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        MessageBox.Show(this, changes.Count == 0 ? "No edits recorded for this unit in the current session." : string.Join("\n\n", changes),
            $"Changes — {unit.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

}
