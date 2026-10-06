using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using AideDeCamp.Services;

namespace AideDeCamp;

// Rebuild only when a virtualized cell actually changes row/column/mode. Never
// populate a Border from a one-shot Loaded handler or retain another row's editor.
public sealed class RosterCellHost : ContentControl
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(nameof(Field), typeof(string), typeof(RosterCellHost), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty EditingProperty = DependencyProperty.Register(nameof(Editing), typeof(bool), typeof(RosterCellHost), new PropertyMetadata(false, Changed));
    public string Field { get => (string)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public bool Editing { get => (bool)GetValue(EditingProperty); set => SetValue(EditingProperty, value); }
    public RosterCellHost() {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => Refresh();
        DataContextChanged += (_, _) => Refresh();
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RosterCellHost)d).Refresh();
    private void Refresh() {
        if (Window.GetWindow(this) is MainWindow window && DataContext is Models.RosterRow row) {
            // The owning column is authoritative during cell recycling; Field may
            // briefly be empty while the template is being loaded or disconnected.
            DependencyObject? ancestor = this;
            while (ancestor is not null && ancestor is not DataGridCell)
                ancestor = ancestor is Visual ? VisualTreeHelper.GetParent(ancestor) : LogicalTreeHelper.GetParent(ancestor);
            var key = ancestor is DataGridCell cell ? RosterFields.FromColumn(cell.Column?.SortMemberPath) : Field;
            if (!RosterFields.IsSupported(key)) { Content = null; return; }
            Content = window.CreateRosterCell(row, key!, Editing);
            if (Editing && Content is Control editor) Dispatcher.BeginInvoke(new Action(() => {
                if (!IsLoaded || !Editing || !ReferenceEquals(Content, editor)) return;
                editor.ApplyTemplate();
                var text = editor as TextBox ?? (editor is ComboBox combo ? combo.Template.FindName("PART_EditableTextBox", combo) as TextBox : null);
                if (text is not null) { text.Focus(); text.SelectAll(); } else editor.Focus();
            }), DispatcherPriority.Input);
        } else Content = null;
    }
}
