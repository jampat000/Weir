namespace Weir.Tray;

/// <summary>
/// The tray's context menu on screen. It is built once, from the first <see cref="TrayMenu"/> description, and every later
/// description only updates the items, so a click handler is added once per item however often the state changes.
/// </summary>
sealed class TrayMenuView : IDisposable
{
    private readonly Dictionary<TrayMenuItem, ToolStripMenuItem> _items = [];
    private readonly Dictionary<TrayMenuItem, Action> _clicks;
    private readonly HashSet<TrayMenuItem> _staysOpen;
    private bool _keepOpen;

    /// <param name="clicks">What each item does when clicked. An item with no entry does nothing.</param>
    /// <param name="staysOpen">Items that flip a setting: the menu stays open after one is clicked, showing the new state.</param>
    internal TrayMenuView(Dictionary<TrayMenuItem, Action> clicks, HashSet<TrayMenuItem> staysOpen)
    {
        _clicks = clicks;
        _staysOpen = staysOpen;
        Strip.ShowItemToolTips = true;
        Strip.ItemClicked += (_, e) => _keepOpen = e.ClickedItem is ToolStripMenuItem item && _staysOpen.Contains(KeyOf(item));
        Strip.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && _keepOpen)
            {
                e.Cancel = true;
            }
            _keepOpen = false;
        };
    }

    internal ContextMenuStrip Strip { get; } = new();

    internal void Show(IReadOnlyList<TrayMenuEntry> entries)
    {
        if (_items.Count == 0)
        {
            Build(entries);
        }
        foreach (var entry in entries.Where(entry => entry.Item is not null))
        {
            var item = _items[entry.Item!.Value];
            item.Text = entry.Text;
            item.Enabled = entry.Enabled;
            item.ToolTipText = entry.ToolTip;
            if (entry.Checked is { } isChecked)
            {
                item.Checked = isChecked;
            }
        }
    }

    public void Dispose() => Strip.Dispose();

    private void Build(IReadOnlyList<TrayMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Item is not { } key)
            {
                Strip.Items.Add(new ToolStripSeparator());
                continue;
            }
            var item = new ToolStripMenuItem { Tag = key };
            if (entry.Bold)
            {
                item.Font = new Font(item.Font, FontStyle.Bold);
            }
            if (_clicks.TryGetValue(key, out var click))
            {
                item.Click += (_, _) => click();
            }
            _items[key] = item;
            Strip.Items.Add(item);
        }
    }

    private static TrayMenuItem KeyOf(ToolStripMenuItem item) => (TrayMenuItem)item.Tag!;
}
