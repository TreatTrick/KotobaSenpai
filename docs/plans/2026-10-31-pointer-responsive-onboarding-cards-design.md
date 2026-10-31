# Pointer-Responsive Onboarding Cards

Approved on 2026-10-31.

## Context

The dictionary onboarding step contains a static three-layer card illustration. The window selection and ready steps also show a static target-window preview card. The model setup visual is an interactive form, so it is not part of this effect.

## Design

- Add a reusable WPF surface for passive visual cards. Render its card content onto a tessellated 3D face and use pointer position to create a shallow indentation beneath the pointer with a subtle raised rim around it.
- Move the backing layers slightly with the pointer for parallax. Smoothly restore the face and backing layers when the pointer leaves. Keep the card's layout bounds stable.
- Apply the effect to the UniDic illustration and the onboarding target-window preview in the selection and ready steps. Do not transform the model configuration form or its controls.
- Keep depth and material lighting restrained so card text and preview details remain readable. The interaction stays enabled regardless of Windows animation and high-contrast settings, as requested.
- Add no user-facing strings or external dependencies.

## Alternatives

- A whole-card tilt with a radial highlight is simpler and keeps ordinary WPF text rendering, but only suggests a local press through lighting.
- Layer-only parallax is the lightest option, but does not create a depressed contact area.
- The selected tessellated face provides actual local depth while remaining limited to passive card surfaces.

## Verification

Build the WPF app and run its test project. Add focused coverage for neutral geometry and the local depression/raised rim. Manually inspect pointer movement at the center and edges, pointer exit reset, the window preview, and text legibility.
