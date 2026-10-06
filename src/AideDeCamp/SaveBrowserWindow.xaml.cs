using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AideDeCamp.Models;
using AideDeCamp.Services;
using Microsoft.Win32;

namespace AideDeCamp;

public partial class SaveBrowserWindow : Window
{
    private readonly GameInstallationService _installation;
    private IReadOnlyList<CampaignSaveGroup> _campaigns = Array.Empty<CampaignSaveGroup>();
    public string? SelectedSaveDirectory { get; private set; }

    public SaveBrowserWindow(GameInstallationService installation)
    {
        InitializeComponent();
        _installation = installation;
        InstallPathText.Text = installation.InstallationRoot ?? string.Empty;
        Scan();
    }

    private void Scan()
    {
        _campaigns = _installation.ScanActiveCampaigns();
        CampaignList.ItemsSource = _campaigns;
        SaveList.ItemsSource = null;
        HintText.Text = _campaigns.Count == 0 ? "No active campaign saves were found." : $"{_campaigns.Count:N0} active campaign{(_campaigns.Count == 1 ? "" : "s")} found.";
        if (_campaigns.Count > 0) CampaignList.SelectedIndex = 0;
    }

    private void CampaignList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CampaignList.SelectedItem is CampaignSaveGroup group)
        {
            SaveList.ItemsSource = group.Saves;
            if (group.Saves.Count > 0) SaveList.SelectedIndex = 0;
            HintText.Text = $"{group.Saves.Count:N0} save{(group.Saves.Count == 1 ? "" : "s")} in selected campaign.";
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => Scan();
    private void SaveList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelected();
    private void OpenSelected_Click(object sender, RoutedEventArgs e) => AcceptSelected();

    private void AcceptSelected()
    {
        if (SaveList.SelectedItem is not SaveEntry save) { HintText.Text = "Select a save first."; return; }
        SelectedSaveDirectory = save.SaveDirectory;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Grand Tactician Save Folder" };
        if (dlg.ShowDialog() != true) return;
        SelectedSaveDirectory = dlg.FolderName;
        DialogResult = true;
    }
}
