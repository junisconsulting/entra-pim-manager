---
paths:
  - "README.md"
  - "docs/images/**"
  - "src/Entra-PIM-Manager.App.Avalonia/Views/TrayPopupWindow.axaml"
  - "scripts/*.ps1"
  - "packaging/intune/**"
---

The README's graphics in `docs/images/*.svg` state facts too: `hero.svg` draws the tray panel from
`TrayPopupWindow.axaml` (with the version in its footer), `rollout.svg` the setup and rollout path
(`create-app-registration.ps1`, the consent link, `install-entra-pim-manager.ps1` and its
parameters). A change to the panel, the setup or the rollout updates the matching SVG and its alt
text in `README.md` in the same commit. A grep for stale docs covers `docs/images`, not only the
Markdown files: twice the text was updated and the graphic was not.
