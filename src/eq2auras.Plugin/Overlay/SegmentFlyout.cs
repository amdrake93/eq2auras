using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Inherits the source window's font; width pre-measured so expanding a group never snaps it;
    /// opens up-and-left from the chip (WPF clamps it on-screen). No scrollbar chrome — the wheel scrolls.
    internal sealed class SegmentFlyout
    {
        private const double LeadWidth = 14;   // the dot/Σ column — shared so labels align
        private const double DurationWidth = 52;

        private readonly Popup _popup;
        private readonly Action<SegmentSelection, string> _onPick;
        private readonly FontFamily _fontFamily;
        private readonly double _fontSize;

        public SegmentFlyout(UIElement target, VisualStyle style, SegmentListing listing, SegmentSelection current, bool returnToCurrent,
            Action<SegmentSelection, string> onPick, Action<bool> onKnobToggled)
        {
            _onPick = onPick;
            _fontFamily = style?.Font;
            _fontSize = style?.RowText ?? 13.0;

            var body = new StackPanel();
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
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,   // no raw chrome — the wheel scrolls
                Content = groups,
            });

            body.Children.Add(Rule());
            var knob = new ThemeCheckbox("Return to Current when a fight starts", returnToCurrent) { Margin = new Thickness(5, 0, 5, 0) };
            knob.Toggled += b => onKnobToggled?.Invoke(b);
            body.Children.Add(knob);

            double width = MeasureWidth(listing);
            var shell = new Border
            {
                Width = width,
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
                Placement = PlacementMode.Custom,          // up-and-left (below); WPF clamps on-screen
                CustomPopupPlacementCallback = UpAndLeft,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = shell,
            };
        }

        public void Show() { _popup.IsOpen = true; }
        public void Close() { _popup.IsOpen = false; }

        // Open above the chip, right edge aligned to the chip's right edge so it extends LEFT over the
        // window's own top-right, not off to the right; WPF nudges it fully on-screen from here.
        private static CustomPopupPlacement[] UpAndLeft(Size popupSize, Size targetSize, Point offset)
            => new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, -popupSize.Height), PopupPrimaryAxis.Horizontal) };

        private void Pick(SegmentSelection sel, string label)
        {
            _onPick?.Invoke(sel, label);
            _popup.IsOpen = false;
        }

        private void AddZoneGroup(Panel parent, ZoneGroup zone, SegmentSelection current)
        {
            var groupBody = new StackPanel { Visibility = zone.IsCurrent ? Visibility.Visible : Visibility.Collapsed };

            var caret = new TextBlock { Text = zone.IsCurrent ? "▾" : "▸", Foreground = Theme.TextMuted, FontSize = 9, Width = 12, VerticalAlignment = VerticalAlignment.Center };
            var zoneName = new TextBlock { Text = zone.ZoneName, Foreground = Theme.TextLabel, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ApplyFont(zoneName, _fontSize - 2);
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 7, 8, 2) };
            nameRow.Children.Add(caret);
            nameRow.Children.Add(zoneName);
            var header = new Border { Child = nameRow, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            Hoverable(header, selected: false);
            header.MouseLeftButtonUp += (s, e) =>
            {
                bool show = groupBody.Visibility != Visibility.Visible;
                groupBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                caret.Text = show ? "▾" : "▸";
            };
            parent.Children.Add(header);

            bool allSelected = zone.All.Available && current.Kind == SegmentKind.Historical && current.IsAll && current.ZoneKey == zone.All.ZoneKey;
            var allEntry = zone.All;
            groupBody.Children.Add(FightItem(allEntry, "All", allEntry.Available, allSelected,
                () => Pick(SegmentSelection.HistoricalAll(allEntry.ZoneKey), "All — " + zone.ZoneName)));

            foreach (var fight in zone.Fights)
            {
                var f = fight;
                bool sel = current.Kind == SegmentKind.Historical && !current.IsAll && current.ZoneKey == f.ZoneKey && current.StartTicks == f.StartTicks;
                groupBody.Children.Add(FightItem(f, f.Title, enabled: true, sel,
                    () => Pick(SegmentSelection.Historical(f.ZoneKey, f.StartTicks), f.Title)));
            }

            parent.Children.Add(groupBody);
        }

        private UIElement SectionLabel(string text) => new TextBlock
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
                Text = enabled ? label : label + "  (needs “Zone All listing”)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = enabled ? (selected ? Theme.TextPrimary : Theme.TextLabel) : Theme.TextMuted,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ApplyFont(text, _fontSize);
            var border = new Border
            {
                Child = text,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(8, 0, 8, 0),
                CornerRadius = new CornerRadius(3),
                Background = selected ? Theme.ItemSelected : Brushes.Transparent,
            };
            if (enabled) { border.Cursor = Cursors.Hand; border.MouseLeftButtonUp += (s, e) => onClick(); Hoverable(border, selected); }
            return border;
        }

        private UIElement FightItem(SegmentEntry entry, string label, bool enabled, bool selected, Action onClick)
        {
            var lead = entry.IsAll
                ? (UIElement)new TextBlock { Text = "Σ", Width = LeadWidth, TextAlignment = TextAlignment.Center, Foreground = enabled ? Theme.AccentBlue : Theme.TextMuted, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }
                : new Border { Width = LeadWidth, Child = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = OutcomeBrush(entry.Outcome) }, VerticalAlignment = VerticalAlignment.Center };

            var name = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = enabled ? (selected ? Theme.TextPrimary : Theme.TextLabel) : Theme.TextMuted,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ApplyFont(name, _fontSize - 1);

            string trailing = !enabled ? "unavailable" : (entry.DurationSeconds > 0 ? FormatDuration(entry.DurationSeconds) : "");
            var duration = new TextBlock
            {
                Text = trailing,
                Width = DurationWidth,
                TextAlignment = TextAlignment.Right,
                Foreground = Theme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ApplyFont(duration, _fontSize - 2);

            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(lead, Dock.Left);
            DockPanel.SetDock(duration, Dock.Right);   // right-aligned duration column
            row.Children.Add(lead);
            row.Children.Add(duration);
            row.Children.Add(name);   // fills the middle, trims

            var border = new Border
            {
                Child = row,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(20, 0, 8, 0),
                CornerRadius = new CornerRadius(3),
                Background = selected ? Theme.ItemSelected : Brushes.Transparent,
            };
            if (enabled) { border.Cursor = Cursors.Hand; border.MouseLeftButtonUp += (s, e) => onClick(); Hoverable(border, selected); }
            return border;
        }

        /// Hover highlight (SPEC §The theme system — the selectable list-item's state set): a selected
        /// row keeps its lit background; an unselected one lights on enter and clears on leave.
        private static void Hoverable(Border border, bool selected)
        {
            border.MouseEnter += (s, e) => { if (!selected) border.Background = Theme.ItemSelected; };
            border.MouseLeave += (s, e) => { if (!selected) border.Background = Brushes.Transparent; };
        }

        private void ApplyFont(TextBlock text, double size)
        {
            if (_fontFamily != null) text.FontFamily = _fontFamily;
            text.FontSize = size;
        }

        // Pre-measure so expanding a long-named group never snaps the flyout: the widest label across
        // every row (visible or collapsed) sets the width, capped; anything past it ellipsis-trims.
        private double MeasureWidth(SegmentListing listing)
        {
            double max = TextWidth("Return to Current when a fight starts", _fontSize) + 30;   // the knob is the usual floor
            max = Math.Max(max, TextWidth("Zonewide  (needs “Zone All listing”)", _fontSize) + 40);
            foreach (var z in listing.Zones)
            {
                max = Math.Max(max, TextWidth(z.ZoneName, _fontSize - 2) + 40);
                max = Math.Max(max, TextWidth("All", _fontSize - 1) + 30 + LeadWidth + DurationWidth + 20);
                foreach (var f in z.Fights)
                    max = Math.Max(max, TextWidth(f.Title, _fontSize - 1) + 30 + LeadWidth + DurationWidth + 20);
            }
            return Math.Max(224, Math.Min(340, max));
        }

        private double TextWidth(string text, double size)
        {
            var typeface = new Typeface(_fontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1.0);
            return ft.WidthIncludingTrailingWhitespace;
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
