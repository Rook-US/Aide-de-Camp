using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AideDeCamp.Models;
using AideDeCamp.Services;

namespace AideDeCamp;

public sealed class BatchEditWindow : Window
{
    private readonly IReadOnlyList<CombatUnitNode> _units;
    private readonly BatchEditPlanner _planner;
    private readonly CheckBox _etaEnabled = new() { Content = "ETA" };
    private readonly TextBox _etaBox = new() { Width = 90 };
    private readonly CheckBox _contractEnabled = new() { Content = "Contract Length" };
    private readonly TextBox _contractBox = new() { Width = 90 };
    private readonly CheckBox _remainingEnabled = new() { Content = "Contract Remaining" };
    private readonly TextBox _remainingBox = new() { Width = 90 };
    private readonly CheckBox _experienceEnabled = new() { Content = "Experience" };
    private readonly TextBox _experienceBox = new() { Width = 90 };
    private readonly CheckBox _weaponEnabled = new() { Content = "Weapon" };
    private readonly ComboBox _weaponCombo = new() { Width = 260, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly CheckBox _allowUnknownWeapon = new() { Content = "Allow weapon assignment when compatibility is unknown", Margin = new Thickness(155, 4, 0, 2) };
    private readonly TextBlock _preview = new() { Margin = new Thickness(0, 12, 0, 6), Foreground = new SolidColorBrush(Color.FromRgb(180, 195, 208)), TextWrapping = TextWrapping.Wrap };
    private readonly Button _applyButton = new() { Content = "Apply Working Changes", MinWidth = 150, IsDefault = true };

    public bool Applied { get; private set; }
    public BatchEditPlan? Plan { get; private set; }
    public int ChangedCount => Plan?.ChangedUnitCount ?? 0;

    public BatchEditWindow(IReadOnlyList<CombatUnitNode> units, IReadOnlyList<WeaponOption> weapons, BatchEditPlanner planner)
    {
        _units = units;
        _planner = planner;
        Title = $"Batch Edit — {units.Count} Units";
        Width = 570;
        Height = 575;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(23, 31, 39));
        Foreground = Brushes.White;

        var editorBackground = new SolidColorBrush(Color.FromRgb(28, 38, 48));
        var editorForeground = new SolidColorBrush(Color.FromRgb(243, 246, 248));
        foreach (var box in new[] { _etaBox, _contractBox, _remainingBox, _experienceBox })
        {
            box.Background = editorBackground;
            box.Foreground = editorForeground;
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(82, 97, 112));
        }
        _weaponCombo.Background = editorBackground;
        _weaponCombo.Foreground = editorForeground;
        _weaponCombo.BorderBrush = new SolidColorBrush(Color.FromRgb(82, 97, 112));
        var itemStyle = new Style(typeof(ComboBoxItem), (Style)FindResource(typeof(ComboBoxItem)));
        itemStyle.Setters.Add(new Setter(ComboBoxItem.BackgroundProperty, editorBackground));
        itemStyle.Setters.Add(new Setter(ComboBoxItem.ForegroundProperty, editorForeground));
        itemStyle.Setters.Add(new Setter(ComboBoxItem.PaddingProperty, new Thickness(6, 4, 6, 4)));
        _weaponCombo.ItemContainerStyle = itemStyle;
        _weaponCombo.ItemsSource = weapons;
        if (weapons.Count > 0) _weaponCombo.SelectedIndex = 0;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = $"{units.Count:N0} combat unit{(units.Count == 1 ? "" : "s")} selected", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = "Check multiple fields to change them together. Only checked fields change. Preview and Apply use the same per-unit execution plan; skipped fields are never silently forced.", Foreground = new SolidColorBrush(Color.FromRgb(136, 153, 167)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

        root.Children.Add(FieldRow(_etaEnabled, _etaBox, "days"));
        root.Children.Add(FieldRow(_contractEnabled, _contractBox, "months"));
        root.Children.Add(FieldRow(_remainingEnabled, _remainingBox, "months remaining"));
        root.Children.Add(FieldRow(_experienceEnabled, _experienceBox, "0–100"));
        root.Children.Add(FieldRow(_weaponEnabled, _weaponCombo, string.Empty));
        root.Children.Add(_allowUnknownWeapon);

        root.Children.Add(new TextBlock
        {
            Text = "ETA requires a uniquely confirmed paths.dat link. Contract Remaining preserves the enlistment date by changing Contract Length. Known incompatible weapons are skipped; unknown compatibility requires the override above.",
            Foreground = new SolidColorBrush(Color.FromRgb(130, 146, 160)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        root.Children.Add(_preview);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 80, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        buttons.Children.Add(_applyButton);
        root.Children.Add(buttons);
        Content = root;

        foreach (var check in new[] { _etaEnabled, _contractEnabled, _remainingEnabled, _experienceEnabled, _weaponEnabled })
        {
            check.Checked += (_, _) =>
            {
                if (check == _contractEnabled) _remainingEnabled.IsChecked = false;
                if (check == _remainingEnabled) _contractEnabled.IsChecked = false;
                UpdatePreview();
            };
            check.Unchecked += (_, _) => UpdatePreview();
        }
        _allowUnknownWeapon.Checked += (_, _) => UpdatePreview();
        _allowUnknownWeapon.Unchecked += (_, _) => UpdatePreview();
        foreach (var box in new[] { _etaBox, _contractBox, _remainingBox, _experienceBox }) box.TextChanged += (_, _) => UpdatePreview();
        _weaponCombo.SelectionChanged += (_, _) => UpdatePreview();
        _applyButton.Click += Apply_Click;
        UpdatePreview();
    }

    private static Grid FieldRow(CheckBox enabled, Control editor, string suffix)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        enabled.VerticalAlignment = VerticalAlignment.Center;
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(enabled);
        grid.Children.Add(editor);
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            var text = new TextBlock { Text = suffix, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(130, 146, 160)) };
            Grid.SetColumn(text, 2);
            grid.Children.Add(text);
        }
        return grid;
    }

    private void UpdatePreview()
    {
        var hasChange = _etaEnabled.IsChecked == true || _contractEnabled.IsChecked == true || _remainingEnabled.IsChecked == true || _experienceEnabled.IsChecked == true || _weaponEnabled.IsChecked == true;
        if (!hasChange)
        {
            Plan = null;
            _preview.Text = $"{_units.Count:N0} selected • no fields chosen";
            _preview.ToolTip = "Choose one or more fields to preview.";
            _applyButton.IsEnabled = false;
            return;
        }

        if (!TryBuildRequest(out var request, out var parseErrors))
        {
            Plan = null;
            _preview.Text = $"{_units.Count:N0} selected • {parseErrors.Count:N0} error(s)";
            _preview.ToolTip = "ERRORS\n" + string.Join("\n", parseErrors);
            _applyButton.IsEnabled = false;
            return;
        }

        Plan = _planner.Build(request!, _units);
        var skippedText = Plan.SkippedUnitCount > 0 ? $" • {Plan.SkippedUnitCount:N0} with skipped field(s)" : string.Empty;
        var warningText = Plan.WarningUnitCount > 0 ? $" • {Plan.WarningUnitCount:N0} warning unit(s)" : string.Empty;
        _preview.Text = $"{_units.Count:N0} selected • {Plan.ChangedUnitCount:N0} will change{skippedText}{warningText}";
        _preview.ToolTip = Plan.BuildTooltip();
        _applyButton.IsEnabled = Plan.Errors.Count == 0 && Plan.ChangedUnitCount > 0;
    }

    private bool TryBuildRequest(out BatchEditRequest? request, out List<string> errors)
    {
        errors = new List<string>();
        int? eta = null;
        int? contract = null;
        int? remaining = null;
        double? experience = null;
        WeaponOption? weapon = null;

        if (_etaEnabled.IsChecked == true)
        {
            if (!TryNonNegativeInt(_etaBox.Text, out var value)) errors.Add("ETA must be a whole number of days, zero or greater.");
            else eta = value;
        }
        if (_contractEnabled.IsChecked == true)
        {
            if (!TryNonNegativeInt(_contractBox.Text, out var value)) errors.Add("Contract Length must be a whole number of months, zero or greater.");
            else contract = value;
        }
        if (_remainingEnabled.IsChecked == true)
        {
            if (!TryNonNegativeInt(_remainingBox.Text, out var value)) errors.Add("Contract Remaining must be a whole number of months, zero or greater.");
            else remaining = value;
        }
        if (_experienceEnabled.IsChecked == true)
        {
            if (!double.TryParse(_experienceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0 || value > 100)
                errors.Add("Experience must be between 0 and 100.");
            else experience = value;
        }
        if (_weaponEnabled.IsChecked == true)
        {
            weapon = _weaponCombo.SelectedItem as WeaponOption;
            if (weapon is null) errors.Add("Select a weapon.");
        }

        request = errors.Count == 0
            ? new BatchEditRequest(eta, contract, remaining, experience, weapon, _allowUnknownWeapon.IsChecked == true)
            : null;
        return errors.Count == 0;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        if (!_applyButton.IsEnabled || Plan is null) return;
        Applied = true;
        DialogResult = true;
    }

    private static bool TryNonNegativeInt(string? text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
}
