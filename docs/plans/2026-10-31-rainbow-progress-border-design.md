# Rainbow Progress Border Design

## Context

The dictionary-install onboarding step already uses a rainbow gradient for the progress fill. The approved change adds a continuously moving rainbow outline around that control while preserving its existing fill and progress behavior.

## Design

- Add a thin rainbow border over the existing progress bar without changing `Value`, `Maximum`, `IsIndeterminate`, `Background`, or `Foreground`.
- Animate only the border brush. Keep the animation running when the dictionary reaches 100% and shows its ready state.
- Follow the app's existing motion preferences: leave the border static when Windows client-area animation is disabled or high contrast is enabled.
- Start the animation while the dictionary step is loaded, stop it when unloaded, and react to live changes in the relevant Windows settings.
- Add no new user-facing text, business state, or dependencies.

## Verification

Build the WPF app and run the app test project. Confirm that existing dirty changes to the onboarding progress fill remain present.
