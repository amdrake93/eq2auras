using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The segment picker flyout (SPEC §Segments), opened from the header chip. Current + Zonewide
    /// on top (Zonewide disabled/greyed when the current zone has no "Zone All listing"), then
    /// per-zone collapsible groups — each an All (a disabled placeholder when the zone lacks one)
    /// then its fights with a win/partial/wipe dot — and a footer "Return to Current" checkbox.
    internal sealed class SegmentFlyout
    {
        private readonly Popup _popup;
        private readonly Action<SegmentSelection, string> _onPick;

        public SegmentFlyout(UIElement target, SegmentListing listing, SegmentSelection current, bool returnToCurrent,
            Action<SegmentSelection, string> onPick, Action<bool> onKnobToggled)
        {
            _onPick = onPick;

            var body = new StackPanel { MinWidth = 224 };
            body.Children.Add(SectionLabel("Segment"));
            body.Children.Add(TopItem("Current", current.Kind == SegmentKind.Current, enabled: true,
                onClick: () => Pick(SegmentSelection.Current(), "Current")));
            body.Children.Add(TopItem("Zonewide", current.Kind == SegmentKind.Zonewide, enabled: listing.ZonewideAvailable,
                onClick: () => Pick(SegmentSelection.Zonewide(), "Zonewide")));

            var groups = new StackPanel();
            foreach (var zone in listing.Zones) AddZoneGroup(groups, zone, current);
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 250,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = groups,
            });

            body.Children.Add(Rule());
            var knob = new ThemeCheckbox("Return to Current when a fight starts", returnToCurrent) { Margin = new Thickness(5, 0, 5, 0) };
            knob.Toggled += b => onKnobToggled?.Invoke(b);
            body.Children.Add(knob);

            var shell = new Border
            {
                Background = Theme.Surface(0xF2),
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 4, 2, 6),
                Child = body,
            };
            _popup = new Popup
            {
                PlacementTarget = target,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = shell,
            };
        }

        public void Show() { _popup.IsOpen = true; }

        private void Pick(SegmentSelection sel, string label)
        {
            _onPick?.Invoke(sel, label);
            _popup.IsOpen = false;
        }

        private void AddZoneGroup(Panel parent, ZoneGroup zone, SegmentSelection current)
        {
            var groupBody = new StackPanel { Visibility = zone.IsCurrent ? Visibility.Visible : Visibility.Collapsed };

            var caret = new TextBlock { Text = zone.IsCurrent ? "▾" : "▸", Foreground = Theme.TextMuted, FontSize = 9, Width = 12, VerticalAlignment = VerticalAlignment.Center };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7, 8, 2) };
            nameRow.Children.Add(caret);
            nameRow.Children.Add(new TextBlock { Text = zone.ZoneName, Foreground = Theme.TextLabel, FontSize = 10, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var header = new Border { Child = nameRow, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            header.MouseLeftButtonUp += (s, e) =>
            {
                bool show = groupBody.Visibility != Visibility.Visible;
                groupBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                caret.Text = show ? "▾" : "▸";
            };
            parent.Children.Add(header);

            bool allSelected = zone.All.Available && current.Kind == SegmentKind.Historical
                && current.ZoneKey == zone.All.ZoneKey && current.StartTicks == zone.All.StartTicks;
            var allEntry = zone.All;
            groupBody.Children.Add(FightItem(allEntry, "All", allEntry.Available, allSelected,
                () => Pick(SegmentSelection.Historical(allEntry.ZoneKey, allEntry.StartTicks), "All — " + zone.ZoneName)));

            foreach (var fight in zone.Fights)
            {
                var f = fight;
                bool sel = current.Kind == SegmentKind.Historical && current.ZoneKey == f.ZoneKey && current.StartTicks == f.StartTicks;
                groupBody.Children.Add(FightItem(f, f.Title, enabled: true, sel,
                    () => Pick(SegmentSelection.Historical(f.ZoneKey, f.StartTicks), f.Title)));
            }

            parent.Children.Add(groupBody);
        }

        private static UIElement SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 9,
            Foreground = Theme.TextMuted,
            Margin = new Thickness(13, 8, 13, 3),
        };

        private static UIElement Rule() => new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(13, 6, 13, 4) };

        private UIElement TopItem(string label, bool selected, bool enabled, Action onClick)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = enabled ? (selected ? Theme.TextPrimary : Theme.TextLabel) : Theme.TextMuted,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (!enabled) text.Text = label + "  (needs “Zone All listing”)";
            var border = new Border
            {
                Child = text,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(3),
                Background = selected ? Theme.ItemSelected : Brushes.Transparent,
            };
            if (enabled) { border.Cursor = Cursors.Hand; border.MouseLeftButtonUp += (s, e) => onClick(); }
            return border;
        }

        private UIElement FightItem(SegmentEntry entry, string label, bool enabled, bool selected, Action onClick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (entry.IsAll)
                row.Children.Add(new TextBlock { Text = "Σ", Width = 12, TextAlignment = TextAlignment.Center, Foreground = enabled ? Theme.AccentBlue : Theme.TextMuted, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            else
                row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Margin = new Thickness(3, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Fill = OutcomeBrush(entry.Outcome) });

            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = enabled ? (selected ? Theme.TextPrimary : Theme.TextLabel) : Theme.TextMuted,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            });

            string trailing = !enabled ? "  (unavailable)" : (entry.DurationSeconds > 0 ? "  " + FormatDuration(entry.DurationSeconds) : "");
            if (trailing.Length > 0)
                row.Children.Add(new TextBlock { Text = trailing, FontSize = 10, Foreground = Theme.TextMuted, VerticalAlignment = VerticalAlignment.Center });

            var border = new Border
            {
                Child = row,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(20, 0, 8, 0),
                CornerRadius = new CornerRadius(3),
                Background = selected ? Theme.ItemSelected : Brushes.Transparent,
            };
            if (enabled) { border.Cursor = Cursors.Hand; border.MouseLeftButtonUp += (s, e) => onClick(); }
            return border;
        }

        private static Brush OutcomeBrush(EncounterOutcome o)
        {
            switch (o)
            {
                case EncounterOutcome.Win: return Brushes.MediumSeaGreen;
                case EncounterOutcome.Partial: return Brushes.Goldenrod;
                case EncounterOutcome.Wipe: return Brushes.IndianRed;
                default: return Brushes.Transparent;
            }
        }

        private static string FormatDuration(double seconds)
        {
            int s = (int)seconds;
            return (s / 60) + ":" + (s % 60).ToString("D2");
        }
    }
}
