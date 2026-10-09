using System.Windows;
using AideDeCamp.Models;

namespace AideDeCamp;

public partial class MainWindow
{
    private bool _oobVisibilityDirty;

    private List<GroupNode> RosterExpandableGroups(int? nativeTier = null) =>
        _displayModel.CommandsById.Values.Select(command => command.Source)
            .Where(group => group.Children.Count > 0 && (nativeTier is null || group.UnitTier == nativeTier))
            .Distinct().ToList();

    private void UpdateRosterCollapseButtons()
    {
        if (RosterCollapseAllButton is null) return;
        var listing = _rosterListing && FilterHeadquartersCheck?.IsChecked != true;
        Update(RosterCollapseAllButton, RosterExpandableGroups(), "All", listing);
        Update(RosterCollapseCorpsButton, RosterExpandableGroups(15), OobPresentation.TierName(15, true), listing);
        Update(RosterCollapseDivisionsButton, RosterExpandableGroups(14), OobPresentation.TierName(14, true), listing);

        static void Update(System.Windows.Controls.Button button, List<GroupNode> groups, string label, bool listing)
        {
            var collapse = groups.Any(group => group.IsExpanded);
            var verb = collapse ? "Collapse" : "Uncollapse";
            button.Content = $"{verb} {label}";
            button.IsEnabled = listing && groups.Count > 0;
            button.ToolTip = listing
                ? label == "All" ? $"{verb} every command in this roster."
                    : $"{verb} all {label.ToLowerInvariant()} commands in this roster."
                : "Use Listing view with headquarters visible to collapse commands.";
        }
    }

    private void ToggleRosterGroups(int? nativeTier)
    {
        if (!_rosterListing || FilterHeadquartersCheck.IsChecked == true) return;
        var groups = RosterExpandableGroups(nativeTier);
        if (groups.Count == 0) return;
        var expand = !groups.Any(group => group.IsExpanded);
        foreach (var group in groups) group.IsExpanded = expand;
        _oobVisibilityDirty = true;
        RefreshRoster();
    }

    private void RosterCollapseAll_Click(object sender, RoutedEventArgs e) => ToggleRosterGroups(null);
    private void RosterCollapseCorps_Click(object sender, RoutedEventArgs e) => ToggleRosterGroups(15);
    private void RosterCollapseDivisions_Click(object sender, RoutedEventArgs e) => ToggleRosterGroups(14);
}
