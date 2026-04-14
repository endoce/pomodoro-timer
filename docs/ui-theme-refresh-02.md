# UI Theme Refresh 02: Fresh Mint

This update applies a consistent **Fresh Mint** theme to the current Tkinter UI while keeping layout and behavior unchanged.

## Selected theme

The theme uses a light mint background, soft neutral cards, and clear accent colors for timer and status states.

## Palette

- App background: `#F4FFF7`
- Main content/card background: `#F8F8F8`
- Info label/card background: `#EAF7F0`
- Primary text: `#3D4C4A`
- Secondary text: `#6B7C78`
- Timer text: `#FF6B6B`
- Status text:
  - Idle: `#7F8C8D`
  - Working: `#2ECC71`
  - Break: `#3498DB`
  - Long Break: `#5DADE2`
  - Paused: `#F39C12`
- Buttons:
  - Start: `#27AE60`
  - Pause: `#F39C12`
  - Reset: `#5DADE2`
  - Skip: `#E67E22`
  - Clear Stats: `#85C1E9`
  - Settings: `#AAB7B8`
- Border accent: `#2F3E3B`

## Color mapping in the UI

- **Timer text** keeps the strongest visual emphasis using `#FF6B6B`.
- **Status/Stats/Cycle info cards** use a shared light info-card background (`#EAF7F0`) and consistent bordered card styling.
- **Status label text** changes by mode using the defined Idle/Working/Break/Long Break/Paused colors.
- **Button colors** are mapped per action for quick recognition while keeping white text for readability.
