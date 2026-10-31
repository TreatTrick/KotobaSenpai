# Phrase Popup Appearance and Placement

Approved on 2026-10-31.

The popup background has a configurable transparency from 0% to 100%, defaulting to 50%. Text stays opaque. The setting belongs to the existing overlay appearance settings, persists through the shared settings service, and updates open popups through the existing appearance refresh event. Background layers retain dynamic theme resources and render separately from content.

The popup height is limited to two thirds of the selected monitor's work area, measured in physical pixels and converted to WPF DIPs. Long content remains scrollable. Short content continues to size to its content.

Placement prefers the horizontal side with more available space, with an 8 DIP gap. If only one side can fit the popup, that side wins. If neither side fits, the popup uses available space above or below the anchor, reducing its height when necessary to keep the source visible. All bounds remain inside the selected monitor's work area, including negative monitor coordinates and scaled displays.

Alternatives considered: changing window opacity would also fade text; retaining vertical-first placement would continue to obstruct source text when clamped near screen edges.

Validation covers preference defaults, invalid values, persistence and failed saves; horizontal placement, vertical fallbacks, height limits, monitor bounds and DPI; background-only opacity and theme updates; scrolling and mouse transfer between the source and popup.
