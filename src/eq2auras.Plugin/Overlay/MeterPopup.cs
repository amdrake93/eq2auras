using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The meter's right-click popup (SPEC Part III §Configuration): family-column PRIMARY and
    /// SECONDARY metric toggle-grids, a lifecycle cluster (Lock · New meter · Remove meter,
    /// destructive red), and a corner ✕ dismiss. Replaces the raw ContextMenu. Transient:
    /// StaysOpen=false dismisses on outside click; picking a lifecycle action closes it.
    internal sealed class MeterPopup
    {
        public sealed class Callbacks
        {
            public Action<string> PrimaryToggled;    // new key, or null to clear
            public Action<string> SecondaryToggled;   // new key, or null to clear
            public Action Lock;
            public Action NewMeter;
            public Action RemoveMeter;
        }

        private readonly Popup _popup;
        private readonly Callbacks _cb;
        private readonly List<KeyValuePair<string, MetricGridItem>> _primaryItems = new List<KeyValuePair<string, MetricGridItem>>();
        private readonly List<KeyValuePair<string, MetricGridItem>> _secondaryItems = new List<KeyValuePair<string, MetricGridItem>>();
        private string _primaryKey;
        private string _secondaryKey;

        public MeterPopup(UIElement placementTarget, string primaryKey, string secondaryKey, Func<bool> canRemove, Callbacks cb)
        {
            _cb = cb;
            _primaryKey = primaryKey;
            _secondaryKey = secondaryKey;

            var body = new StackPanel();
            body.Children.Add(SectionLabel("Primary metric"));
            body.Children.Add(BuildGrid(isPrimary: true));
            body.Children.Add(Rule());
            body.Children.Add(SectionLabel("Secondary metric"));
            body.Children.Add(BuildGrid(isPrimary: false));
            body.Children.Add(Rule());
            body.Children.Add(BuildLifecycle(canRemove));

            var dismiss = new ThemeButton("✕")
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            dismiss.Click += () => _popup.IsOpen = false;

            var overlay = new Grid();
            overlay.Children.Add(body);
            overlay.Children.Add(dismiss);

            var shell = new Border
            {
                Background = Theme.Surface(0xF2),        // near-solid popup surface
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 4, 2, 4),
                Child = overlay
            };

            _popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Bottom,   // anchored to the header edge, not the cursor — drops below the header rather than covering the meter at a random spot (SPEC §Configuration: "anchored to the window you right-clicked")
                StaysOpen = false,       // dismiss on outside click
                AllowsTransparency = true,
                Child = shell
            };
        }

        public void Show() { _popup.IsOpen = true; }

        private UIElement BuildGrid(bool isPrimary)
        {
            var items = isPrimary ? _primaryItems : _secondaryItems;
            var grid = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 2, 11, 10) };
            foreach (var family in MetricRegistry.All.Select(m => m.Category).Distinct())
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                col.Children.Add(FamilyHeader(family));
                foreach (var metric in MetricRegistry.All.Where(m => m.Category == family))
                {
                    string key = metric.Key;
                    bool selected = isPrimary ? key == _primaryKey : key == _secondaryKey;
                    var item = new MetricGridItem(metric.Label, selected);
                    items.Add(new KeyValuePair<string, MetricGridItem>(key, item));
                    item.Toggled += () => OnToggle(isPrimary, key);
                    col.Children.Add(item);
                }
                grid.Children.Add(col);
            }
            return grid;
        }

        private void OnToggle(bool isPrimary, string key)
        {
            // Toggle: clicking the lit one clears (null); clicking another switches to it.
            if (isPrimary)
            {
                _primaryKey = _primaryKey == key ? null : key;
                RefreshSection(_primaryItems, _primaryKey);
                _cb.PrimaryToggled(_primaryKey);
            }
            else
            {
                _secondaryKey = _secondaryKey == key ? null : key;
                RefreshSection(_secondaryItems, _secondaryKey);
                _cb.SecondaryToggled(_secondaryKey);
            }
        }

        // Single-selection per section: exactly the item whose key == the section's current
        // selection lights; a null selection (cleared) lights nothing.
        private static void RefreshSection(List<KeyValuePair<string, MetricGridItem>> items, string selectedKey)
        {
            foreach (var pair in items) pair.Value.Selected = pair.Key == selectedKey;
        }

        private static UIElement SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 9,
            Foreground = Theme.TextMuted,
            Margin = new Thickness(13, 11, 13, 3)
        };

        private static UIElement FamilyHeader(string family)
        {
            var color = OverlayTheme.FromArgbInt(MeterFamilyColors.ArgbFor(family));
            return new TextBlock
            {
                Text = family,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(6, 4, 6, 4)
            };
        }

        private static UIElement Rule() => new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(13, 0, 13, 0) };

        private UIElement BuildLifecycle(Func<bool> canRemove)
        {
            var lockBtn = new ThemeButton("Lock");
            lockBtn.Click += () => { _cb.Lock(); _popup.IsOpen = false; };
            var newBtn = new ThemeButton("New meter") { Margin = new Thickness(7, 0, 0, 0) };
            newBtn.Click += () => { _cb.NewMeter(); _popup.IsOpen = false; };
            // Destructive red, and disabled (greyed, non-interactive) on the last meter — the
            // disabled visual is set at construction, not left for a hover to force (SPEC §Configuration:
            // Remove meter is "blocked when only one meter remains"). A fresh popup per open, so
            // canRemove() here reflects the current window count.
            var removeBtn = new ThemeButton("Remove meter", ThemeButton.Variant.Destructive, canRemove())
            {
                Margin = new Thickness(14, 0, 0, 0)
            };
            removeBtn.Click += () => { if (canRemove()) { _cb.RemoveMeter(); _popup.IsOpen = false; } };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(11, 8, 11, 9) };
            row.Children.Add(lockBtn);
            row.Children.Add(newBtn);
            row.Children.Add(removeBtn);
            return row;
        }
    }
}
