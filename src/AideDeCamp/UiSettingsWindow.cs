using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AideDeCamp.Services;
using Microsoft.Win32;

namespace AideDeCamp;

public sealed class UiSettingsWindow : Window
{
    private readonly UiSettingsService _settings;
    private readonly Action _apply;
    private readonly StackPanel _content = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private bool _rebuilding;
    public UiSettingsWindow(UiSettingsService settings, Action apply)
    {
        _settings = settings; _apply = apply;
        Title = "UI Settings — live preview"; Width = 650; Height = 800; MinWidth = 570; MinHeight = 400;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        void Button(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => Run(action); buttons.Children.Add(b); }
        Button("Reset ALL", () => { settings.Reset(); Rebuild(); Changed(); });
        Button("Save Config", () => { settings.Save(); });
        Button("Import Config", () => { var d = new OpenFileDialog { Filter = "UI config (*.json)|*.json" }; if (d.ShowDialog(this) == true) { settings.Import(d.FileName); Rebuild(); Changed(); } });
        Button("Export Config", () => { var d = new SaveFileDialog { Filter = "UI config (*.json)|*.json", FileName = "Aide-de-Camp.UI.json" }; if (d.ShowDialog(this) == true) settings.Export(d.FileName); });
        root.Children.Add(new ScrollViewer { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _timer.Tick += (_, _) => { _timer.Stop(); _apply(); };
        Closing += (_, e) => { _timer.Stop(); _apply(); try { settings.Save(); } catch (Exception ex) { ErrorLog.Write("Save UI settings",ex); e.Cancel = true; MessageBox.Show(this, ex.Message, "Could not save UI config"); } };
        Rebuild();
    }
    private void Run(Action action) { try { action(); } catch (Exception ex) { ErrorLog.Write("Read or write UI settings",ex); MessageBox.Show(this, ex.Message, "UI config"); } }
    private void Changed() { if (_rebuilding) return; _timer.Stop(); _timer.Start(); }
    private void Rebuild()
    {
        _rebuilding = true; _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = "Changes preview live and persist on close. Name, commander, Home State and type always remain visible. Metrics hide below the detail zoom threshold. Counters always reserve layout space. Roster rows grow to fit text.\nSpacing labels follow the displayed brigade/regimental scale. Use the toolbar to switch scale or nudge a branch.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        foreach (var section in UiSettingsService.Parameters.GroupBy(p => p.Section))
        {
            var header = new DockPanel { Margin = new Thickness(0,12,0,5) };
            var reset = new Button { Content = "Reset section", HorizontalAlignment = HorizontalAlignment.Right };
            reset.Click += (_, _) => { _settings.Reset(section.Key); Rebuild(); Changed(); };
            DockPanel.SetDock(reset, Dock.Right); header.Children.Add(reset);
            header.Children.Add(new TextBlock { Text = section.Key switch { "oob.presentation" => "Organization labels (native IDs preserved)", "oob.cards" => "Unit and HQ cards", "oob.natoCounters" => "Floating NATO counters", "oob.spacing" => "Formation spacing", "oob.connectors" => "Hierarchy connectors", "oob.zoom" => "Zoom detail", "roster.density" => "Roster readability", "roster.hierarchy" => "Roster hierarchy", "theme.text" => "Application text", _ => section.Key }, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }); _content.Children.Add(header);
            foreach (var p in section)
            {
                var row = new Grid { Margin = new Thickness(0,3,0,3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(235) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
                var label=new StackPanel{VerticalAlignment=VerticalAlignment.Center};label.Children.Add(new TextBlock{Text=p.Label,TextWrapping=TextWrapping.Wrap});
                if(!p.Toggle)label.Children.Add(new TextBlock{Text=$"Range {p.Min:0.##}–{p.Max:0.##} · default {p.Default:0.##}",FontSize=10,Opacity=.7});row.Children.Add(label);
                if (p.Toggle)
                {
                    var check = new CheckBox { IsChecked = _settings.Get(p.Key) >= .5, VerticalAlignment = VerticalAlignment.Center };
                    check.Click += (_, _) => { _settings.Set(p.Key, check.IsChecked == true ? 1 : 0); Changed(); }; Grid.SetColumn(check, 1); row.Children.Add(check);
                }
                else
                {
                    var box = new TextBox { Text = _settings.Get(p.Key).ToString("0.##", CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Center };
                    void Commit() { if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= p.Min && v <= p.Max) { _settings.Set(p.Key, v); Changed(); } else box.Text = _settings.Get(p.Key).ToString("0.##", CultureInfo.InvariantCulture); }
                    box.ToolTip = $"{p.Min} to {p.Max}"; box.LostKeyboardFocus += (_, _) => Commit(); box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { Commit(); e.Handled = true; } }; Grid.SetColumn(box, 1); row.Children.Add(box);
                }
                var individual = new Button { Content = "Reset", Padding = new Thickness(4) }; individual.Click += (_, _) => { _settings.Set(p.Key, p.Default); Rebuild(); Changed(); }; Grid.SetColumn(individual, 2); row.Children.Add(individual); _content.Children.Add(row);
            }
        }
        _rebuilding = false;
    }
}
